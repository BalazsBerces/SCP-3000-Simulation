using System.Numerics;

namespace Scp3008.Core;

/// <summary>A piece of Furniture that fell in a collapse: where it stood and where it came to rest on the floor.</summary>
public sealed record FallenPiece(FurniturePiece From, FurniturePiece To);

/// <summary>
/// A structure broke the stability rules and came down (ADR 0002): <paramref name="Broken"/> gave way under more than its
/// load capacity, and every piece in <paramref name="Fallen"/> lost its support and fell to the floor, damaged by the fall.
/// </summary>
/// <param name="Position">Where the collapse began: the first piece that gave way, or the support that was taken away.</param>
public sealed record StructureCollapsed(StoreClock At, Vector2 Position, IReadOnlyList<FallenPiece> Fallen, IReadOnlyList<int> Broken) : DomainEvent(At);

/// <summary>
/// Rule-based structural stability (ADR 0002): a piece of Furniture stands on the floor or rests on the pieces under it, and
/// passes its weight and the load on it down to them in equal shares. A piece that carries more than its load capacity, less
/// its damage, gives way (its damage becomes total), and a piece left with nothing under it falls to the floor.
/// Only the structures touching a changed piece are checked, so a world stands as generated until something in it changes.
/// </summary>
internal static class Stability
{
    /// <summary>How close, in metres, the bottom of a piece must come to the top of another, or to the floor, to rest on it.</summary>
    private const float Contact = 0.05f;
    private const float CellSize = 4f;
    /// <summary>How far, in cells, from a change the structures touching it are looked for.</summary>
    private const int Reach = 5;

    /// <summary>How far from a changed pose, along either axis, <see cref="Settle"/> looks at Furniture.</summary>
    public const float RegionReach = (Reach + 1) * CellSize;

    private readonly record struct Bounds(Vector2 Min, Vector2 Max, float Bottom, float Top);

    /// <summary>The weight, in kg, that a piece can carry on it when undamaged.</summary>
    public static float LoadCapacity(FurniturePiece piece)
    {
        var spec = FurnitureCatalog.Spec(piece);
        // Fixed pieces are part of the building, and a flat piece (a rug, a tray) has nothing in it to give way.
        if (spec.IsFixed || spec.Height < 0.1f)
            return float.PositiveInfinity;
        return piece.Type switch
        {
            FurnitureType.CardboardBox or FurnitureType.LabelledBox => 60,
            FurnitureType.FlatPackBox => 300,
            // Warehouse racks are made to carry pallets: a stack of five with boxes stands.
            FurnitureType.PalletRack => 3000,
            FurnitureType.Mattress => 400,
            _ when spec.IsLamp => 5,
            _ => FurnitureResources.Weight(piece) * 10,
        };
    }

    /// <summary>
    /// Settles the structures touching the changed poses: returns the pieces that gave way and the pieces that fell, with their
    /// resting poses. <paramref name="held"/> pieces are carried by people: they rest on nothing and carry nothing.
    /// </summary>
    public static (IReadOnlyList<int> Broken, IReadOnlyList<FallenPiece> Fallen, Vector2 Origin) Settle(
        IReadOnlyList<FurniturePiece> furniture, StoreSpace space, IReadOnlyCollection<FurniturePiece> changed, Func<int, bool> held)
    {
        // shortcut: scans every piece's cell on each check; keep a standing grid if checks show up in the tick time.
        // A structure reaching past the region is cut at its edge: pieces resting on what lies outside count as hung there.
        var cells = (int)MathF.Round(space.Size / CellSize);
        int Cell(float v) => (((int)MathF.Floor(v / CellSize) % cells) + cells) % cells;
        var region = new HashSet<(int, int)>();
        foreach (var pose in changed)
            for (var x = -Reach; x <= Reach; x++)
                for (var y = -Reach; y <= Reach; y++)
                    region.Add((Cell(pose.Position.X + x * CellSize), Cell(pose.Position.Y + y * CellSize)));
        var pieces = furniture.Where(p => !held(p.Id) && region.Contains((Cell(p.Position.X), Cell(p.Position.Y)))).ToDictionary(p => p.Id);
        var bounds = pieces.Values.ToDictionary(p => p.Id, Measure);
        var grid = new Dictionary<(int, int), List<int>>();
        foreach (var piece in pieces.Values)
            foreach (var cell in Cells(piece.Position, bounds[piece.Id], cells))
                (grid.TryGetValue(cell, out var list) ? list : grid[cell] = []).Add(piece.Id);

        IEnumerable<int> Near(Vector2 position, Bounds box) =>
            Cells(position, box, cells).SelectMany(c => grid.TryGetValue(c, out var l) ? l : []).Distinct();
        bool Overlap(Vector2 aAt, Bounds a, Vector2 bAt, Bounds b)
        {
            var d = space.Delta(aAt, bAt);
            return a.Min.X < d.X + b.Max.X && d.X + b.Min.X < a.Max.X && a.Min.Y < d.Y + b.Max.Y && d.Y + b.Min.Y < a.Max.Y;
        }
        bool On(Vector2 aAt, Bounds a, Vector2 bAt, Bounds b) =>
            b.Bottom < a.Bottom - Contact && a.Bottom <= b.Top + Contact && Overlap(aAt, a, bAt, b);
        bool RestsOn(int upper, int lower) =>
            upper != lower && On(pieces[upper].Position, bounds[upper], pieces[lower].Position, bounds[lower]);

        // The structures touching the change: every piece connected to it by resting on another.
        var structure = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (var pose in changed)
        {
            var box = Measure(pose);
            foreach (var id in Near(pose.Position, box).Where(id => Overlap(pose.Position, box, pieces[id].Position, bounds[id])))
                if (structure.Add(id)) queue.Enqueue(id);
        }
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var other in Near(pieces[id].Position, bounds[id]))
                if ((RestsOn(id, other) || RestsOn(other, id)) && structure.Add(other))
                    queue.Enqueue(other);
        }

        var supports = structure.ToDictionary(id => id, id => Near(pieces[id].Position, bounds[id]).Where(o => structure.Contains(o) && RestsOn(id, o)).ToList());
        // A raised piece with nothing under it that did not rest on a moved piece either hangs on a wall, a Pillar or a net.
        var moved = new Dictionary<(int, int), List<(FurniturePiece Pose, Bounds Box)>>();
        foreach (var pose in changed)
            foreach (var cell in Cells(pose.Position, Measure(pose), cells))
                (moved.TryGetValue(cell, out var list) ? list : moved[cell] = []).Add((pose, Measure(pose)));
        bool Mounted(int id) => supports[id].Count == 0 && !Cells(pieces[id].Position, bounds[id], cells)
            .SelectMany(c => moved.TryGetValue(c, out var l) ? l : [])
            .Any(c => c.Pose.Id != id && On(pieces[id].Position, bounds[id], c.Pose.Position, c.Box));
        var grounded = structure.Where(id => bounds[id].Bottom <= Contact || FurnitureCatalog.Spec(pieces[id]).IsFixed || Mounted(id)).ToHashSet();
        var standing = new HashSet<int>(structure);
        var broken = new List<int>();
        var fallen = new List<int>();
        var origin = changed.First().Position;
        while (true)
        {
            // Everything left with nothing under it falls, and whatever rested only on it follows.
            for (var falling = true; falling;)
            {
                falling = false;
                foreach (var id in standing.Where(id => !grounded.Contains(id) &&
                             !supports[id].Any(s => standing.Contains(s) && !broken.Contains(s))).ToArray())
                {
                    standing.Remove(id);
                    fallen.Add(id);
                    falling = true;
                }
            }
            // Loads pass down from the top, shared equally among a piece's supports.
            var load = standing.ToDictionary(id => id, _ => 0f);
            foreach (var id in standing.OrderByDescending(id => bounds[id].Bottom))
            {
                var under = supports[id].Where(s => standing.Contains(s) && !broken.Contains(s)).ToList();
                foreach (var s in under)
                    load[s] += (FurnitureResources.Weight(pieces[id]) + load[id]) / under.Count;
            }
            // The lowest overloaded piece gives way first; what it carried then falls. A Landmark's pieces stand as built
            // (its heaps would not stand otherwise) and only fall when their support is taken away.
            var overloaded = standing.Where(id => !broken.Contains(id) && pieces[id].LandmarkId < 0 &&
                    load[id] > LoadCapacity(pieces[id]) * (1f - pieces[id].Damage))
                .OrderBy(id => bounds[id].Bottom).ThenBy(id => id).Take(1).ToList();
            if (overloaded.Count == 0)
                break;
            if (broken.Count == 0)
                origin = pieces[overloaded[0]].Position;
            broken.Add(overloaded[0]);
        }

        var landed = fallen.Select(id => new FallenPiece(pieces[id], Land(pieces[id], bounds[id], origin, space))).ToList();
        return (broken, landed, origin);
    }

    /// <summary>
    /// Where a falling piece comes to rest: on the floor, thrown out from where the collapse began by a share of its fall,
    /// a tall piece toppled onto its front or back, a low one landing upright, a little turned; the fall damages it.
    /// </summary>
    private static FurniturePiece Land(FurniturePiece piece, Bounds box, Vector2 origin, StoreSpace space)
    {
        var spec = FurnitureCatalog.Spec(piece);
        var jitter = (float)((uint)HashCode.Combine(piece.Id, 3008) % 1000) / 1000f;
        var away = space.Delta(origin, piece.Position);
        var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away)
            : new Vector2(MathF.Cos(jitter * MathF.Tau), MathF.Sin(jitter * MathF.Tau));
        var front = new Vector2(-MathF.Sin(piece.Rotation), MathF.Cos(piece.Rotation));
        var topples = spec.Height > MathF.Max(spec.Width, spec.Depth) * 0.8f;
        var tilt = topples ? (Vector2.Dot(direction, front) >= 0 ? MathF.PI / 2f : -MathF.PI / 2f) : 0f;
        var throw_ = MathF.Min(box.Bottom * 0.3f, 3f) + (topples ? spec.Height / 2f : 0f);
        var rested = piece with
        {
            Position = space.Wrap(piece.Position + direction * throw_), Elevation = 0f,
            Rotation = piece.Rotation + (jitter - 0.5f) * 0.6f, Tilt = tilt, Roll = 0f,
            Damage = MathF.Min(1f, piece.Damage + Math.Clamp(box.Bottom * 0.1f, 0.1f, 0.6f)),
        };
        return rested with { Elevation = -FurnitureCatalog.Corners(rested).Min(c => c.Z) };
    }

    /// <summary>A piece's box: its footprint around its position, and the heights of its bottom and top.</summary>
    private static Bounds Measure(FurniturePiece piece)
    {
        var corners = FurnitureCatalog.Corners(piece with { Position = Vector2.Zero });
        return new(new(corners.Min(c => c.X), corners.Min(c => c.Y)), new(corners.Max(c => c.X), corners.Max(c => c.Y)),
            corners.Min(c => c.Z), corners.Max(c => c.Z));
    }

    private static IEnumerable<(int, int)> Cells(Vector2 position, Bounds box, int cells)
    {
        int Cell(float v) => (int)MathF.Floor(v / CellSize);
        var (x0, x1) = (Cell(position.X + box.Min.X), Cell(position.X + box.Max.X));
        var (y0, y1) = (Cell(position.Y + box.Min.Y), Cell(position.Y + box.Max.Y));
        for (var x = x0; x <= Math.Min(x1, x0 + cells - 1); x++)
            for (var y = y0; y <= Math.Min(y1, y0 + cells - 1); y++)
                yield return (((x % cells) + cells) % cells, ((y % cells) + cells) % cells);
    }
}
