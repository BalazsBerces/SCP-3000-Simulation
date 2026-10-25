using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// Where Villagers can walk, and the shortest way between two places. The Store floor is a wrapping grid of
/// <see cref="CellSize"/> cells; a cell is blocked when its centre lies within <see cref="Clearance"/> of a Pillar or
/// of Furniture a Villager cannot step over or walk under. Each chunk's cells are worked out when first needed and
/// again after Furniture in it changes (<see cref="Update"/>).
/// </summary>
internal sealed class Navigation
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
    private readonly Dictionary<int, bool[]> _blocked = [];
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
    }

    /// <summary>Takes Furniture changes into account: the chunks the removed and added pieces reach are worked out again.</summary>
    public void Update(IEnumerable<FurniturePiece> removed, IEnumerable<FurniturePiece> added)
    {
        if (_footprints is null)
            return; // Nothing worked out yet: the next query reads the current Furniture.
        foreach (var piece in removed)
            if (FootprintOf(piece) is { } footprint)
                foreach (var chunk in ChunksOf(footprint))
                {
                    if (_footprints.TryGetValue(chunk, out var inChunk))
                        inChunk.Remove(piece.Id);
                    _blocked.Remove(chunk);
                }
        foreach (var piece in added)
            Add(piece.Id, FootprintOf(piece));
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
        var open = new PriorityQueue<int, float>();
        var cost = new Dictionary<int, float> { [start] = 0f };
        var cameFrom = new Dictionary<int, int>();
        var closed = new HashSet<int>();
        open.Enqueue(start, Estimate(start, goal));
        while (open.TryDequeue(out var cell, out _))
        {
            if (cell == goal)
            {
                var cells = new List<int> { goal };
                while (cells[^1] != start)
                    cells.Add(cameFrom[cells[^1]]);
                cells.Reverse();
                return cells;
            }
            if (!closed.Add(cell))
                continue;
            if (closed.Count > MaxExpansions)
                return null;
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
                    if (closed.Contains(next))
                        continue;
                    var nextCost = cost[cell] + (dx != 0 && dy != 0 ? MathF.Sqrt(2f) : 1f);
                    if (cost.TryGetValue(next, out var known) && known <= nextCost)
                        continue;
                    cost[next] = nextCost;
                    cameFrom[next] = cell;
                    open.Enqueue(next, nextCost + Estimate(next, goal));
                }
        }
        return null;
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
        if (!_blocked.TryGetValue(chunk, out var cells))
            _blocked[chunk] = cells = BuildChunk(chunk);
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
        var chunkX = chunk % _space.ChunksPerSide;
        var chunkY = chunk / _space.ChunksPerSide;
        foreach (var footprint in footprints.Values)
            for (var y = (int)MathF.Floor(footprint.BoundsMin.Y / CellSize); y <= (int)MathF.Floor(footprint.BoundsMax.Y / CellSize); y++)
            {
                if (Wrap(y) / _cellsPerChunk != chunkY)
                    continue;
                for (var x = (int)MathF.Floor(footprint.BoundsMin.X / CellSize); x <= (int)MathF.Floor(footprint.BoundsMax.X / CellSize); x++)
                {
                    if (Wrap(x) / _cellsPerChunk != chunkX)
                        continue;
                    var delta = _space.Delta(footprint.Position, new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize));
                    var across = Vector2.Dot(delta, footprint.Across);
                    var front = Vector2.Dot(delta, footprint.Front);
                    if (across > footprint.Min.X && across < footprint.Max.X && front > footprint.Min.Y && front < footprint.Max.Y)
                        cells[Wrap(y) % _cellsPerChunk * _cellsPerChunk + Wrap(x) % _cellsPerChunk] = true;
                }
            }
        return cells;
    }

    private void Add(int id, Footprint? footprint)
    {
        if (_footprints is null || footprint is not { } found)
            return;
        foreach (var chunk in ChunksOf(found))
        {
            if (!_footprints.TryGetValue(chunk, out var inChunk))
                _footprints[chunk] = inChunk = [];
            inChunk[id] = found;
            _blocked.Remove(chunk);
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
