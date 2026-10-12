using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>The generated Furniture of a Store: its pieces, the Displays they stand in, and how each cell is laid out.</summary>
internal sealed record FurnitureLayout(IReadOnlyList<FurniturePiece> Pieces, IReadOnlyList<Display> Displays, IReadOnlyList<CellLayout> Cells);

/// <summary>
/// Fills every department cell with Displays. Each cell is laid out on its own from a stream
/// derived from the seed and the cell, inside a 1.5 m margin, so neighbouring cells are separated
/// by 3 m aisles. A cell picks one of its Department type's layout archetypes, which divides it
/// into rooms (by a room partition), aligned halls and strips, or free-standing sets on an open
/// floor; each of those is a Display furnished from one theme. Halls and strips run the same way
/// across a whole Department, so their rows line up from cell to cell; rooms and sets turn freely.
/// Nothing is placed under a Pillar or in the walkway ring around it.
/// </summary>
internal static class FurnitureGenerator
{
    private const float Margin = 1.5f;

    public static FurnitureLayout Generate(int seed, StoreSpace space, DepartmentMap departments, IReadOnlyList<Pillar> pillars)
    {
        var store = new StoreFurniture(space, pillars.Select(p => (p.Position, HalfWidth: p.ClearHalfWidth)).ToArray());
        var cellSize = departments.CellSize;
        var rowQuarters = departments.All.Select(d => SeededRandom.For(seed, stream: 3, d.Id).Next(2)).ToArray();
        var cells = new CellLayout[departments.CellsPerSide * departments.CellsPerSide];
        for (var y = 0; y < departments.CellsPerSide; y++)
            for (var x = 0; x < departments.CellsPerSide; x++)
            {
                var department = departments.AtCell(x, y);
                var cellOrigin = new Vector2(x * cellSize, y * cellSize);
                var cell = new Cell(store, department, x, y, cellOrigin, cellSize, SeededRandom.For(seed, stream: 2, x, y));
                var frame = Frame.Turned(cellOrigin, cellSize, cellSize, rowQuarters[department.Id]);
                cells[y * departments.CellsPerSide + x] = LayOut(cell, frame.Then(new Frame(new Vector2(Margin), 0)), cellSize - 2f * Margin);
            }
        return new FurnitureLayout(store.Pieces.AsReadOnly(), store.Displays.AsReadOnly(), Array.AsReadOnly(cells));
    }

    /// <summary>Lays out the square interior of a cell, of the given size, by one of its Department type's archetypes.</summary>
    private static CellLayout LayOut(Cell cell, Frame interior, float size)
    {
        var random = cell.Random;
        var archetype = Weighted(random, LayoutArchetypes.Of(cell.Department.Type), a => a.Weight);
        var partition = RoomPartition.None;
        switch (archetype.Kind)
        {
            case ArchetypeKind.Hall:
                Furnish(cell, archetype, interior, size, size, PickTheme(cell, archetype.Themes), aligned: true);
                break;
            case ArchetypeKind.Split:
            {
                var first = MathF.Round(size * random.Range(0.45f, 0.65f) * 2f) / 2f;
                Furnish(cell, archetype, interior, size, first, PickTheme(cell, archetype.Themes), aligned: true);
                Furnish(cell, archetype, interior.Then(new Frame(new Vector2(0f, first), 0)), size, size - first,
                    PickTheme(cell, archetype.SecondThemes), aligned: true);
                break;
            }
            case ArchetypeKind.Rooms:
            {
                partition = Weighted(random, archetype.Partitions, p => p.Weight).Partition;
                var turned = interior.Then(Frame.Turned(Vector2.Zero, size, size, random.Next(4)));
                foreach (var (corner, width, depth) in Rooms(partition, size))
                {
                    var quarters = random.Next(4);
                    var room = turned.Then(Frame.Turned(corner, width, depth, quarters));
                    var (w, d) = (quarters & 1) == 0 ? (width, depth) : (depth, width);
                    Furnish(cell, archetype, room, w, d, PickTheme(cell, archetype.Themes), aligned: false);
                }
                break;
            }
            case ArchetypeKind.OpenFloor:
            {
                partition = RoomPartition.OpenFloor;
                // A set of its own size somewhere in each quarter of the cell, so sets stand apart at
                // uneven spacings; now and then a quarter stays open floor.
                var half = size / 2f;
                foreach (var quarter in new[] { Vector2.Zero, new Vector2(half, 0f), new Vector2(0f, half), new Vector2(half) })
                {
                    if (random.Chance(0.15f))
                        continue;
                    var extent = new Vector2(random.Range(4.2f, half - 0.4f), random.Range(4.2f, half - 0.4f));
                    var low = quarter + new Vector2(random.Range(0.2f, half - 0.2f - extent.X), random.Range(0.2f, half - 0.2f - extent.Y));
                    var quarters = random.Next(4);
                    var (w, d) = (quarters & 1) == 0 ? (extent.X, extent.Y) : (extent.Y, extent.X);
                    Furnish(cell, archetype, interior.Then(Frame.Turned(low, extent.X, extent.Y, quarters)), w, d,
                        PickTheme(cell, archetype.Themes), aligned: false);
                }
                break;
            }
        }
        return new CellLayout(cell.X, cell.Y, archetype.Id, partition);
    }

    /// <summary>The rooms of a partition of a square of the given size: corner and extent of each.</summary>
    private static IEnumerable<(Vector2 Corner, float Width, float Depth)> Rooms(RoomPartition partition, float size)
    {
        var half = size / 2f;
        return partition switch
        {
            RoomPartition.OneRoom => [(Vector2.Zero, size, size)],
            RoomPartition.TwoRooms => [(Vector2.Zero, size, half), (new Vector2(0f, half), size, half)],
            RoomPartition.ThreeRooms => [(Vector2.Zero, size, half), (new Vector2(0f, half), half, half), (new Vector2(half, half), half, half)],
            RoomPartition.FourRooms => [(Vector2.Zero, half, half), (new Vector2(half, 0f), half, half), (new Vector2(0f, half), half, half), (new Vector2(half, half), half, half)],
            _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null),
        };
    }

    private static void Furnish(Cell cell, Archetype archetype, Frame frame, float width, float depth, Theme theme, bool aligned)
    {
        var spacing = cell.Random.Range(archetype.Spacing.Min, archetype.Spacing.Max);
        DisplayFurnisher.Furnish(cell, archetype, frame, width, depth, theme, spacing, cell.NextPalette(), aligned);
    }

    /// <summary>A theme among the given ones, by the weights of the cell's Department type.</summary>
    private static Theme PickTheme(Cell cell, Theme[] themes)
    {
        var weights = ThemeCatalog.ThemesOf(cell.Department.Type);
        return Weighted(cell.Random, themes, t => weights.First(w => w.Theme == t).Weight);
    }

    internal static T Weighted<T>(SeededRandom random, IReadOnlyList<T> items, Func<T, float> weight)
    {
        var roll = random.NextFloat() * items.Sum(weight);
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll < 0f)
                return item;
        }
        return items[^1];
    }
}

/// <summary>Everything placed so far in the Store, and what must be kept clear.</summary>
internal sealed class StoreFurniture(StoreSpace space, (Vector2 Centre, float HalfWidth)[] keepClear)
{
    public StoreSpace Space { get; } = space;
    public (Vector2 Centre, float HalfWidth)[] KeepClear { get; } = keepClear;
    public List<FurniturePiece> Pieces { get; } = [];
    public List<Display> Displays { get; } = [];
}

/// <summary>
/// A local coordinate frame inside the Store, turned by whole quarter turns: maps local
/// positions and rotations to unwrapped Store positions and rotations.
/// </summary>
internal readonly record struct Frame(Vector2 Origin, int Quarters)
{
    private const float QuarterTurn = MathF.PI / 2f;

    /// <summary>
    /// The rectangle of the given extent at <paramref name="corner"/> of the parent, turned about
    /// its centre. Turned by an odd number of quarters, its local width is the parent's depth.
    /// </summary>
    public static Frame Turned(Vector2 corner, float width, float depth, int quarters)
    {
        quarters &= 3;
        var local = (quarters & 1) == 0 ? new Vector2(width, depth) : new Vector2(depth, width);
        return new Frame(corner + new Vector2(width, depth) / 2f - Turn(local / 2f, quarters), quarters);
    }

    public Vector2 Map(Vector2 local) => Origin + Turn(local, Quarters);

    public float MapRotation(float local) => local + Quarters * QuarterTurn;

    /// <summary>The frame <paramref name="inner"/>, given in this frame's local coordinates, in Store coordinates.</summary>
    public Frame Then(Frame inner) => new(Map(inner.Origin), (Quarters + inner.Quarters) & 3);

    // A quarter turn takes east to south, matching the rotation convention of Furniture.
    private static Vector2 Turn(Vector2 v, int quarters) => (quarters & 3) switch
    {
        0 => v,
        1 => new Vector2(-v.Y, v.X),
        2 => -v,
        _ => new Vector2(v.Y, -v.X),
    };
}

/// <summary>
/// Places Furniture in one department cell, keeping it inside the cell's margin, out of the
/// squares kept clear around the Pillars, and never letting pieces on the floor overlap. Pieces
/// are staged per Display and only kept when the whole Display is committed.
/// </summary>
internal sealed class Cell(StoreFurniture store, Department department, int x, int y, Vector2 origin, float size, SeededRandom random)
{
    private const float Margin = 1.5f;
    private const float BoundsTolerance = 0.1f;
    private readonly List<Footprint> _floor = [];
    private readonly List<Footprint> _stagedFloor = [];
    private readonly List<FurniturePiece> _staged = [];
    private readonly List<int> _palettes = [];

    public SeededRandom Random { get; } = random;
    public Department Department { get; } = department;
    public int X { get; } = x;
    public int Y { get; } = y;

    public bool Place(Frame frame, FurnitureType type, int variant, Vector2 local, float rotation, float elevation, int tint)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        var footprint = new Footprint(frame.Map(local), frame.MapRotation(rotation), spec.Width, spec.Depth);
        if (!Inside(footprint) || InKeptClear(footprint))
            return false;
        if (elevation == 0f && !spec.IsFloorCovering)
        {
            foreach (var other in _floor)
                if (footprint.Overlaps(other))
                    return false;
            foreach (var other in _stagedFloor)
                if (footprint.Overlaps(other))
                    return false;
            _stagedFloor.Add(footprint);
        }
        var heading = (float)Math.IEEERemainder(footprint.Rotation, 2.0 * Math.PI);
        _staged.Add(new FurniturePiece(0, type, store.Space.Wrap(footprint.Centre), elevation, heading, variant, Tint: tint));
        return true;
    }

    /// <summary>A palette for the next Display, differing from the cell's other Displays while palettes last.</summary>
    public int NextPalette()
    {
        var count = FurniturePalettes.All.Count;
        var palette = Random.Next(count);
        for (var i = 0; i < count && _palettes.Contains(palette); i++)
            palette = (palette + 1) % count;
        _palettes.Add(palette);
        return palette;
    }

    /// <summary>Keeps the staged pieces as one Display with its rectangle given in Store coordinates.</summary>
    public void Commit(Theme theme, Vector2 centre, Vector2 extent, int palette)
    {
        var id = store.Displays.Count;
        store.Displays.Add(new Display(id, theme, Department.Id, X, Y, store.Space.Wrap(centre), extent, palette));
        foreach (var piece in _staged)
            store.Pieces.Add(piece with { Id = store.Pieces.Count, DisplayId = id });
        _floor.AddRange(_stagedFloor);
        Discard();
    }

    public void Discard()
    {
        _staged.Clear();
        _stagedFloor.Clear();
    }

    private bool Inside(Footprint footprint)
    {
        var low = origin + new Vector2(Margin - BoundsTolerance);
        var high = origin + new Vector2(size - Margin + BoundsTolerance);
        foreach (var corner in footprint.Corners())
            if (corner.X < low.X || corner.Y < low.Y || corner.X > high.X || corner.Y > high.Y)
                return false;
        return true;
    }

    /// <summary>Whether the footprint's axis-aligned bounds reach into a square kept clear.</summary>
    private bool InKeptClear(Footprint footprint)
    {
        var corners = footprint.Corners();
        var low = new Vector2(corners.Min(c => c.X), corners.Min(c => c.Y));
        var high = new Vector2(corners.Max(c => c.X), corners.Max(c => c.Y));
        foreach (var (centre, halfWidth) in store.KeepClear)
        {
            // The square's image nearest this cell, in case it lies across a Seam.
            var near = footprint.Centre + store.Space.Delta(footprint.Centre, centre);
            if (high.X > near.X - halfWidth && low.X < near.X + halfWidth &&
                high.Y > near.Y - halfWidth && low.Y < near.Y + halfWidth)
                return true;
        }
        return false;
    }
}

/// <summary>A rotated footprint rectangle on the floor.</summary>
internal readonly record struct Footprint(Vector2 Centre, float Rotation, float Width, float Depth)
{
    // Pieces may touch; only overlaps deeper than this count.
    private const float Slack = 0.005f;

    public Vector2[] Corners()
    {
        var along = new Vector2(MathF.Cos(Rotation), MathF.Sin(Rotation));
        var across = new Vector2(-along.Y, along.X);
        var halfWidth = along * (Width / 2f - Slack);
        var halfDepth = across * (Depth / 2f - Slack);
        return [Centre + halfWidth + halfDepth, Centre + halfWidth - halfDepth, Centre - halfWidth - halfDepth, Centre - halfWidth + halfDepth];
    }

    /// <summary>Separating-axis test of two rotated rectangles.</summary>
    public bool Overlaps(Footprint other)
    {
        var reach = (Width + Depth + other.Width + other.Depth) / 2f;
        if (Vector2.DistanceSquared(Centre, other.Centre) > reach * reach)
            return false;
        var mine = Corners();
        var theirs = other.Corners();
        return !(Separated(mine, theirs, mine) || Separated(mine, theirs, theirs));
    }

    private static bool Separated(Vector2[] a, Vector2[] b, Vector2[] axesFrom)
    {
        for (var i = 0; i < 2; i++)
        {
            var edge = axesFrom[i + 1] - axesFrom[i];
            var axis = new Vector2(-edge.Y, edge.X);
            var (minA, maxA) = Project(a, axis);
            var (minB, maxB) = Project(b, axis);
            if (maxA <= minB || maxB <= minA)
                return true;
        }
        return false;
    }

    private static (float Min, float Max) Project(Vector2[] corners, Vector2 axis)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var corner in corners)
        {
            var d = Vector2.Dot(corner, axis);
            min = MathF.Min(min, d);
            max = MathF.Max(max, d);
        }
        return (min, max);
    }
}
