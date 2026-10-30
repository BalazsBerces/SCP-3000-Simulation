using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// Where Villagers can walk, and the shortest way between two places. The Store floor is a wrapping grid of
/// <see cref="CellSize"/> cells; a cell is blocked when its centre lies within <see cref="Clearance"/> of a Pillar or
/// of Furniture a Villager cannot step over or walk under. Each chunk's cells are worked out when first needed and
/// again after Furniture in it changes (<see cref="Update"/>).
/// </summary>
internal sealed partial class Navigation
{
    public const float CellSize = 0.5f;

    // Half a cell's diagonal (0.354 m) and a little more: a point in a free cell, and so a line sampled finely through
    // free cells, keeps off every footprint.
    public const float Clearance = 0.4f;

    // Furniture standing this high off the floor or higher (shelves on a wall, hanging signs) is walked under.
    private const float WalkUnderElevation = 1.8f;

    // shortcut: a search gives up after this many cells (about a 120 m detour through a cluttered floor); add a coarse
    // chunk-level graph when Villagers start crossing Sectors.
    private const int MaxExpansions = 250_000;

    // A target inside Furniture moves to the nearest free cell at most this many cells away.
    private const int MaxSnapCells = 40;

    private const float LineSampleStep = 0.05f;

    private readonly StoreSpace _space;
    private readonly Func<IReadOnlyList<FurniturePiece>> _pieces;
    private readonly IReadOnlyList<Pillar> _pillars;
    private readonly int _cellsPerSide;
    private readonly int _cellsPerChunk;
    // Chunk index → its cells, true where blocked; null until worked out.
    private readonly bool[]?[] _blocked;
    // A goal whose free region is smaller than this is checked for a way in before the search starts.
    private const int PocketCells = 1024;
    // Search bookkeeping, one page per chunk, reused between searches: a cell's stamp is 2 × search when it has a
    // cost in that search, one more once it is closed.
    private sealed record SearchPage(int[] Stamp, float[] Cost, int[] From);
    private readonly SearchPage?[] _pages;
    private int _search;
    private readonly Queue<int> _pocket = new();
    // The cells reachable from the last start, when they are fewer than PocketCells (else null), while nothing changed.
    private int _version;
    private int _pocketStart = -1;
    private int _pocketVersion;
    private HashSet<int>? _startPocket;
    // The bounds of the footprints the last Update took away or put down; null when not known (as good as everywhere).
    private List<(Vector2 Min, Vector2 Max)>? _changedAreas = [];
    // A route passing this close to a changed footprint is found again.
    private const float ChangeMargin = 1f;
    private FurnitureIndex? _climbIndex;
    // Chunk index → the footprints reaching into it, by piece id (Pillars by a negative id).
    private Dictionary<int, Dictionary<int, Footprint>>? _footprints;

    private readonly record struct Footprint(Vector2 Position, Vector2 Across, Vector2 Front, Vector2 Min, Vector2 Max, Vector2 BoundsMin, Vector2 BoundsMax);

    public Navigation(StoreSpace space, Func<IReadOnlyList<FurniturePiece>> pieces, IReadOnlyList<Pillar> pillars)
    {
        _space = space;
        _pieces = pieces;
        _pillars = pillars;
        _cellsPerSide = (int)MathF.Round(space.Size / CellSize);
        _cellsPerChunk = (int)MathF.Round(space.ChunkSize / CellSize);
        _blocked = new bool[space.ChunkCount][];
        _pages = new SearchPage[space.ChunkCount];
    }

    /// <summary>Takes Furniture changes into account: the chunks the removed and added pieces reach are worked out again.</summary>
    public void Update(IEnumerable<FurniturePiece> removed, IEnumerable<FurniturePiece> added)
    {
        _changedAreas = [];
        if (_footprints is null)
        {
            _changedAreas = null;
            return; // Nothing worked out yet: the next query reads the current Furniture.
        }
        foreach (var piece in removed)
            if (FootprintOf(piece) is { } footprint)
            {
                _changedAreas?.Add((footprint.BoundsMin, footprint.BoundsMax));
                foreach (var chunk in ChunksOf(footprint))
                {
                    if (_footprints.TryGetValue(chunk, out var inChunk) && inChunk.Remove(piece.Id, out var stored) &&
                        stored == footprint && _blocked[chunk] is { } cells)
                        Clear(chunk, cells, footprint, inChunk);
                    else
                        _blocked[chunk] = null;
                    _version++;
                }
            }
        foreach (var piece in added)
        {
            var footprint = FootprintOf(piece);
            if (footprint is { } found)
                _changedAreas?.Add((found.BoundsMin, found.BoundsMax));
            Add(piece.Id, footprint);
        }
    }

    /// <summary>A number that changes whenever the free cells may have changed.</summary>
    public int Version => _version;

    /// <summary>
    /// Answers <paramref name="query"/> as if <paramref name="piece"/> were taken away, then leaves everything as it was,
    /// <see cref="Version"/> included, so nothing worked out before has to be worked out again.
    /// </summary>
    public T Without<T>(FurniturePiece piece, Func<T> query)
    {
        if (_footprints is null || FootprintOf(piece) is not { } footprint)
        {
            Update([piece], []);
            try { return query(); }
            finally { Update([], [piece]); }
        }
        var chunks = ChunksOf(footprint);
        var blocked = chunks.Select(chunk => (chunk, _blocked[chunk])).ToArray();
        foreach (var (chunk, cells) in blocked)
            _blocked[chunk] = (bool[]?)cells?.Clone();
        var (version, changedAreas) = (_version, _changedAreas);
        var (pocketStart, pocketVersion, startPocket) = (_pocketStart, _pocketVersion, _startPocket);
        Update([piece], []);
        try { return query(); }
        finally
        {
            Add(piece.Id, footprint);
            foreach (var (chunk, cells) in blocked)
                _blocked[chunk] = cells;
            // Versions passed while the piece was away are never seen again: whatever was worked out with them is dropped.
            (_version, _changedAreas) = (version, changedAreas);
            (_pocketStart, _pocketVersion, _startPocket) = (pocketStart, pocketVersion, startPocket);
        }
    }

    /// <summary>
    /// Whether a route from <paramref name="from"/> through <paramref name="waypoints"/> passes by Furniture the last
    /// <see cref="Update"/> took away or put down, so a better or the only way may now be different.
    /// </summary>
    public bool Crosses(Vector2 from, IReadOnlyList<Vector2> waypoints)
    {
        if (_changedAreas is null)
            return true;
        var start = from;
        for (var i = 0; i <= waypoints.Count; i++)
        {
            var end = i < waypoints.Count ? start + _space.Delta(start, waypoints[i]) : start;
            foreach (var (min, max) in _changedAreas)
                if (SegmentNear(start, end, min, max))
                    return true;
            start = end;
        }
        return false;
    }

    private bool SegmentNear(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
    {
        var half = (max - min) / 2 + new Vector2(ChangeMargin);
        var centre = a + _space.Delta(a, (min + max) / 2);
        var direction = b - a;
        float enter = 0, exit = 1;
        for (var axis = 0; axis < 2; axis++)
        {
            float lo = centre[axis] - half[axis], hi = centre[axis] + half[axis];
            if (MathF.Abs(direction[axis]) < 1e-6f)
            {
                if (a[axis] < lo || a[axis] > hi)
                    return false;
                continue;
            }
            var t1 = (lo - a[axis]) / direction[axis];
            var t2 = (hi - a[axis]) / direction[axis];
            enter = MathF.Max(enter, MathF.Min(t1, t2));
            exit = MathF.Min(exit, MathF.Max(t1, t2));
            if (enter > exit)
                return false;
        }
        return true;
    }

    /// <summary>Whether a Villager can stand at a Store position (it may be unwrapped).</summary>
    public bool IsWalkable(Vector2 position)
    {
        var (x, y) = CellOf(position);
        return !IsBlocked(x, y);
    }

    /// <summary>The position itself if a Villager can stand there, else the centre of the nearest free cell; wrapped.</summary>
    public Vector2 NearestWalkable(Vector2 position)
    {
        position = _space.Wrap(position);
        var (x, y) = CellOf(position);
        if (!IsBlocked(x, y))
            return position;
        for (var ring = 1; ring <= MaxSnapCells; ring++)
        {
            Vector2? best = null;
            var bestDistance = float.MaxValue;
            for (var dy = -ring; dy <= ring; dy++)
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || IsBlocked(Wrap(x + dx), Wrap(y + dy)))
                        continue;
                    var centre = Centre(Wrap(x + dx), Wrap(y + dy));
                    var distance = _space.Delta(position, centre).LengthSquared();
                    if (distance < bestDistance)
                        (best, bestDistance) = (centre, distance);
                }
            if (best is { } found)
                return found;
        }
        return position;
    }

    /// <summary>
    /// The shortest way from <paramref name="from"/> to a free <paramref name="to"/> by the wrapped floor, as the
    /// corners to walk to in turn, ending at <paramref name="to"/>; all wrapped. Null if there is no way.
    /// </summary>
    public List<Vector2>? FindPath(Vector2 from, Vector2 to)
    {
        var cells = FindCells(CellIndex(from), CellIndex(to));
        if (cells is null)
            return null;
        // Unwrap the cell centres into one continuous line from the start, so a way across a seam stays one straight run.
        var points = new List<Vector2>(cells.Count + 1) { from };
        for (var i = 1; i < cells.Count - 1; i++)
            points.Add(points[^1] + _space.Delta(points[^1], Centre(cells[i] % _cellsPerSide, cells[i] / _cellsPerSide)));
        points.Add(points[^1] + _space.Delta(points[^1], to));
        return StringPull(points).Select(_space.Wrap).ToList();
    }

    private List<int>? FindCells(int start, int goal)
    {
        if (start == goal)
            return [start, goal];
        // A goal shut in a small pocket, or a start, would otherwise cost a search of everything reachable from the other.
        if (StartPocket(start) is { } reachable)
        {
            if (!reachable.Contains(goal))
                return null;
        }
        else if (InPocket(goal, start))
            return null;
        var search = NextSearch();
        var open = new PriorityQueue<int, float>();
        var (page, index) = PageOf(start);
        page.Stamp[index] = 2 * search;
        page.Cost[index] = 0f;
        var closed = 0;
        open.Enqueue(start, Estimate(start, goal));
        while (open.TryDequeue(out var cell, out _))
        {
            if (cell == goal)
            {
                var cells = new List<int> { goal };
                while (cells[^1] != start)
                {
                    var (from, at) = PageOf(cells[^1]);
                    cells.Add(from.From[at]);
                }
                cells.Reverse();
                return cells;
            }
            var (cellPage, cellIndex) = PageOf(cell);
            if (cellPage.Stamp[cellIndex] == 2 * search + 1)
                continue;
            cellPage.Stamp[cellIndex] = 2 * search + 1;
            if (++closed > MaxExpansions)
                return null;
            var cellCost = cellPage.Cost[cellIndex];
            int x = cell % _cellsPerSide, y = cell / _cellsPerSide;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = Wrap(x + dx), ny = Wrap(y + dy);
                    // No cutting a corner: a diagonal step needs both cells beside it free.
                    if (IsBlocked(nx, ny) || (dx != 0 && dy != 0 && (IsBlocked(nx, y) || IsBlocked(x, ny))))
                        continue;
                    var next = ny * _cellsPerSide + nx;
                    var (nextPage, nextIndex) = PageOf(next);
                    var stamp = nextPage.Stamp[nextIndex];
                    if (stamp == 2 * search + 1)
                        continue;
                    var nextCost = cellCost + (dx != 0 && dy != 0 ? MathF.Sqrt(2f) : 1f);
                    if (stamp == 2 * search && nextPage.Cost[nextIndex] <= nextCost)
                        continue;
                    nextPage.Stamp[nextIndex] = 2 * search;
                    nextPage.Cost[nextIndex] = nextCost;
                    nextPage.From[nextIndex] = cell;
                    open.Enqueue(next, nextCost + Estimate(next, goal));
                }
        }
        return null;
    }

    /// <summary>
    /// True when no way leads from <paramref name="start"/> to <paramref name="goal"/> because the goal is blocked or
    /// lies in a free region of fewer than <see cref="PocketCells"/> cells that the start cannot step into.
    /// Steps between free cells go both ways, so the goal's region is flooded from the goal.
    /// </summary>
    private bool InPocket(int goal, int start)
    {
        int gx = goal % _cellsPerSide, gy = goal / _cellsPerSide;
        if (IsBlocked(gx, gy))
            return true;
        var search = NextSearch();
        _pocket.Clear();
        var (page, index) = PageOf(goal);
        page.Stamp[index] = 2 * search;
        _pocket.Enqueue(goal);
        var seen = 1;
        while (_pocket.TryDequeue(out var cell))
        {
            int x = cell % _cellsPerSide, y = cell / _cellsPerSide;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = Wrap(x + dx), ny = Wrap(y + dy);
                    var next = ny * _cellsPerSide + nx;
                    // The start may stand in a blocked cell; it still steps out to a free one beside it.
                    if (next == start && (dx == 0 || dy == 0 || (!IsBlocked(nx, y) && !IsBlocked(x, ny))))
                        return false;
                    if (IsBlocked(nx, ny) || (dx != 0 && dy != 0 && (IsBlocked(nx, y) || IsBlocked(x, ny))))
                        continue;
                    var (nextPage, nextIndex) = PageOf(next);
                    if (nextPage.Stamp[nextIndex] == 2 * search)
                        continue;
                    nextPage.Stamp[nextIndex] = 2 * search;
                    if (++seen >= PocketCells)
                        return false;
                    _pocket.Enqueue(next);
                }
        }
        return true;
    }

    /// <summary>Every cell reachable from <paramref name="start"/> when they are fewer than <see cref="PocketCells"/>; else null.</summary>
    private HashSet<int>? StartPocket(int start)
    {
        if (start == _pocketStart && _version == _pocketVersion)
            return _startPocket;
        _pocketStart = start;
        _pocketVersion = _version;
        _startPocket = null;
        var search = NextSearch();
        var cells = new List<int>();
        _pocket.Clear();
        _pocket.Enqueue(start);
        var (page, index) = PageOf(start);
        page.Stamp[index] = 2 * search;
        cells.Add(start);
        while (_pocket.TryDequeue(out var cell))
        {
            int x = cell % _cellsPerSide, y = cell / _cellsPerSide;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = Wrap(x + dx), ny = Wrap(y + dy);
                    if (IsBlocked(nx, ny) || (dx != 0 && dy != 0 && (IsBlocked(nx, y) || IsBlocked(x, ny))))
                        continue;
                    var next = ny * _cellsPerSide + nx;
                    var (nextPage, nextIndex) = PageOf(next);
                    if (nextPage.Stamp[nextIndex] == 2 * search)
                        continue;
                    nextPage.Stamp[nextIndex] = 2 * search;
                    cells.Add(next);
                    if (cells.Count >= PocketCells)
                        return null;
                    _pocket.Enqueue(next);
                }
        }
        return _startPocket = [.. cells];
    }

    private int NextSearch()
    {
        if (++_search >= int.MaxValue / 2)
        {
            Array.Clear(_pages);
            _search = 1;
        }
        return _search;
    }

    private (SearchPage Page, int Index) PageOf(int cell)
    {
        int x = cell % _cellsPerSide, y = cell / _cellsPerSide;
        var chunk = y / _cellsPerChunk * _space.ChunksPerSide + x / _cellsPerChunk;
        var size = _cellsPerChunk * _cellsPerChunk;
        var page = _pages[chunk] ??= new SearchPage(new int[size], new float[size], new int[size]);
        return (page, y % _cellsPerChunk * _cellsPerChunk + x % _cellsPerChunk);
    }

    /// <summary>Drops every corner the line from the last kept one can see past, leaving the corners of a taut path.</summary>
    private List<Vector2> StringPull(List<Vector2> points)
    {
        var kept = new List<Vector2>();
        var anchor = 0;
        for (var i = 1; i < points.Count - 1; i++)
            if (!InSight(points[anchor], points[i + 1]))
            {
                kept.Add(points[i]);
                anchor = i;
            }
        kept.Add(points[^1]);
        return kept;
    }

    private bool InSight(Vector2 from, Vector2 to)
    {
        var own = CellOf(from);
        var steps = (int)MathF.Ceiling(Vector2.Distance(from, to) / LineSampleStep);
        for (var k = 1; k <= steps; k++)
        {
            var (x, y) = CellOf(Vector2.Lerp(from, to, (float)k / steps));
            if ((x, y) != own && IsBlocked(x, y))
                return false;
        }
        return true;
    }

    // Octile distance over the wrapped grid, in cells.
    private float Estimate(int a, int b)
    {
        var dx = Math.Abs(a % _cellsPerSide - b % _cellsPerSide);
        var dy = Math.Abs(a / _cellsPerSide - b / _cellsPerSide);
        dx = Math.Min(dx, _cellsPerSide - dx);
        dy = Math.Min(dy, _cellsPerSide - dy);
        return Math.Max(dx, dy) + (MathF.Sqrt(2f) - 1f) * Math.Min(dx, dy);
    }

    private bool IsBlocked(int x, int y)
    {
        var chunk = y / _cellsPerChunk * _space.ChunksPerSide + x / _cellsPerChunk;
        var cells = _blocked[chunk] ??= BuildChunk(chunk);
        return cells[y % _cellsPerChunk * _cellsPerChunk + x % _cellsPerChunk];
    }

    private bool[] BuildChunk(int chunk)
    {
        var cells = new bool[_cellsPerChunk * _cellsPerChunk];
        if (_footprints is null)
        {
            _footprints = [];
            foreach (var piece in _pieces())
                Add(piece.Id, FootprintOf(piece));
            foreach (var pillar in _pillars)
                Add(-1 - pillar.Id, PillarFootprint(pillar));
        }
        if (!_footprints.TryGetValue(chunk, out var footprints))
            return cells;
        foreach (var footprint in footprints.Values)
            Paint(chunk, cells, footprint, true);
        return cells;
    }

    /// <summary>Sets the cells of a chunk under a footprint, only those also in <paramref name="within"/>'s bounds if given.</summary>
    private void Paint(int chunk, bool[] cells, Footprint footprint, bool value, Footprint? within = null)
    {
        var chunkX = chunk % _space.ChunksPerSide;
        var chunkY = chunk / _space.ChunksPerSide;
        for (var y = (int)MathF.Floor(footprint.BoundsMin.Y / CellSize); y <= (int)MathF.Floor(footprint.BoundsMax.Y / CellSize); y++)
        {
            if (Wrap(y) / _cellsPerChunk != chunkY || within is { } rows && !Spans(rows.BoundsMin.Y, rows.BoundsMax.Y, y))
                continue;
            for (var x = (int)MathF.Floor(footprint.BoundsMin.X / CellSize); x <= (int)MathF.Floor(footprint.BoundsMax.X / CellSize); x++)
            {
                if (Wrap(x) / _cellsPerChunk != chunkX || within is { } columns && !Spans(columns.BoundsMin.X, columns.BoundsMax.X, x))
                    continue;
                var delta = _space.Delta(footprint.Position, new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize));
                var across = Vector2.Dot(delta, footprint.Across);
                var front = Vector2.Dot(delta, footprint.Front);
                if (across > footprint.Min.X && across < footprint.Max.X && front > footprint.Min.Y && front < footprint.Max.Y)
                    cells[Wrap(y) % _cellsPerChunk * _cellsPerChunk + Wrap(x) % _cellsPerChunk] = value;
            }
        }
    }

    // Whether a cell lies in the cell span of [min, max], the Store wrapping.
    private bool Spans(float min, float max, int cell)
    {
        var from = (int)MathF.Floor(min / CellSize);
        return Wrap(Wrap(cell) - Wrap(from)) <= (int)MathF.Floor(max / CellSize) - from;
    }

    /// <summary>
    /// Takes a footprint off a built chunk: the cells under it are free again unless another footprint of the chunk
    /// (<paramref name="rest"/>) covers them too, the same as building the chunk anew without it.
    /// </summary>
    private void Clear(int chunk, bool[] cells, Footprint gone, Dictionary<int, Footprint> rest)
    {
        Paint(chunk, cells, gone, false);
        var centre = (gone.BoundsMin + gone.BoundsMax) / 2;
        var half = (gone.BoundsMax - gone.BoundsMin) / 2 + new Vector2(CellSize);
        foreach (var other in rest.Values)
        {
            var reach = half + (other.BoundsMax - other.BoundsMin) / 2;
            var delta = _space.Delta(centre, (other.BoundsMin + other.BoundsMax) / 2);
            if (MathF.Abs(delta.X) <= reach.X && MathF.Abs(delta.Y) <= reach.Y)
                Paint(chunk, cells, other, true, gone);
        }
    }

    private void Add(int id, Footprint? footprint)
    {
        if (_footprints is null || footprint is not { } found)
            return;
        foreach (var chunk in ChunksOf(found))
        {
            if (!_footprints.TryGetValue(chunk, out var inChunk))
                _footprints[chunk] = inChunk = [];
            var fresh = !inChunk.TryGetValue(id, out var stored);
            inChunk[id] = found;
            // A built chunk takes the new footprint as it is; one that stood elsewhere before is worked out again.
            if (_blocked[chunk] is { } cells && (fresh || stored == found))
                Paint(chunk, cells, found, true);
            else
                _blocked[chunk] = null;
            _version++;
        }
    }

    private HashSet<int> ChunksOf(Footprint footprint)
    {
        var chunks = new HashSet<int>();
        var size = _space.ChunkSize;
        var perSide = _space.ChunksPerSide;
        for (var y = (int)MathF.Floor(footprint.BoundsMin.Y / size); y <= (int)MathF.Floor(footprint.BoundsMax.Y / size); y++)
            for (var x = (int)MathF.Floor(footprint.BoundsMin.X / size); x <= (int)MathF.Floor(footprint.BoundsMax.X / size); x++)
                chunks.Add((y % perSide + perSide) % perSide * perSide + (x % perSide + perSide) % perSide);
        return chunks;
    }

    /// <summary>A piece's footprint grown by the clearance, in its own across/front frame; null if it is walked over or under.</summary>
    private Footprint? FootprintOf(FurniturePiece piece)
    {
        if (FurnitureCatalog.Spec(piece).IsFloorCovering || piece.Elevation >= WalkUnderElevation)
            return null;
        var across = new Vector2(MathF.Cos(piece.Rotation), MathF.Sin(piece.Rotation));
        var front = new Vector2(-MathF.Sin(piece.Rotation), MathF.Cos(piece.Rotation));
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var corner in FurnitureCatalog.Corners(piece))
        {
            var offset = new Vector2(corner.X, corner.Y) - piece.Position;
            var local = new Vector2(Vector2.Dot(offset, across), Vector2.Dot(offset, front));
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        return Bounded(piece.Position, across, front, min - new Vector2(Clearance), max + new Vector2(Clearance));
    }

    private static Footprint PillarFootprint(Pillar pillar)
    {
        var half = new Vector2(pillar.Width / 2f + Clearance);
        return Bounded(pillar.Position, Vector2.UnitX, Vector2.UnitY, -half, half);
    }

    private static Footprint Bounded(Vector2 position, Vector2 across, Vector2 front, Vector2 min, Vector2 max)
    {
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);
        foreach (var u in new[] { min.X, max.X })
            foreach (var v in new[] { min.Y, max.Y })
            {
                var corner = position + u * across + v * front;
                low = Vector2.Min(low, corner);
                high = Vector2.Max(high, corner);
            }
        return new Footprint(position, across, front, min, max, low, high);
    }

    private (int X, int Y) CellOf(Vector2 position)
    {
        var wrapped = _space.Wrap(position);
        return (Math.Min((int)(wrapped.X / CellSize), _cellsPerSide - 1), Math.Min((int)(wrapped.Y / CellSize), _cellsPerSide - 1));
    }

    private int CellIndex(Vector2 position)
    {
        var (x, y) = CellOf(position);
        return y * _cellsPerSide + x;
    }

    private static Vector2 Centre(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    private int Wrap(int cell) => (cell % _cellsPerSide + _cellsPerSide) % _cellsPerSide;
}

