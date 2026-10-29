using System.Numerics;

namespace Scp3008.Core;

internal static class RestockSight
{
    // shortcut: omnidirectional sight and catalogue boxes; add a facing cone when sneaking needs it.
    internal const float Range = 40;
    private const float EyeHeight = 1.6f;

    public static bool CanSee(StoreSpace space, Villager villager, FurniturePiece spot,
        IReadOnlyList<FurniturePiece> furniture, IReadOnlyList<Pillar> pillars, IReadOnlyList<FogBank> fog)
    {
        if (space.Distance(villager.Position, spot.Position) > villager.ViewRange)
            return false;
        var eye = new Vector3(villager.Position, EyeHeight + villager.Elevation);
        foreach (var corner in FurnitureCatalog.Corners(spot))
        {
            var delta = space.Delta(villager.Position, new Vector2(corner.X, corner.Y));
            var target = new Vector3(villager.Position + delta, corner.Z);
            if (pillars.Any(p => Intersects(eye, target, new Vector3(villager.Position + space.Delta(villager.Position, p.Position), 0),
                    new Vector3(-p.Width / 2, -p.Width / 2, 0), new Vector3(p.Width / 2, p.Width / 2, p.Height))))
                continue;
            if (furniture.Any(p => Blocks(space, p, eye, target)))
                continue;
            var samples = Math.Max(1, (int)MathF.Ceiling(delta.Length()));
            var fogged = false;
            for (var i = 0; i <= samples; i++)
                if (FogBank.CombinedDensityAt(space, fog, villager.Position + delta * i / samples) >= 0.6f)
                {
                    fogged = true;
                    break;
                }
            if (!fogged)
                return true;
        }
        return false;
    }

    private static bool Blocks(StoreSpace space, FurniturePiece piece, Vector3 eye, Vector3 target)
    {
        var spec = FurnitureCatalog.Spec(piece);
        if (spec.IsFloorCovering)
            return false;
        if (space.Distance(new Vector2(eye.X, eye.Y), piece.Position) > Range + MathF.Max(0, eye.Z - EyeHeight) * 2 + Math.Max(spec.Width, Math.Max(spec.Depth, spec.Height)))
            return false;
        var centre = new Vector3(new Vector2(eye.X, eye.Y) + space.Delta(new Vector2(eye.X, eye.Y), piece.Position), piece.Elevation);
        Vector3 Local(Vector3 point)
        {
            var d = point - centre;
            var (sin, cos) = MathF.SinCos(piece.Rotation);
            var across = d.X * cos + d.Y * sin;
            var forward = -d.X * sin + d.Y * cos;
            var (tiltSin, tiltCos) = MathF.SinCos(piece.Tilt);
            var up = d.Z * tiltCos + forward * tiltSin;
            var depth = forward * tiltCos - d.Z * tiltSin;
            var (rollSin, rollCos) = MathF.SinCos(piece.Roll);
            return new Vector3(across * rollCos + up * rollSin, depth, -across * rollSin + up * rollCos);
        }
        return Intersects(Local(eye), Local(target), Vector3.Zero,
            new Vector3(-spec.Width / 2, -spec.Depth / 2, 0), new Vector3(spec.Width / 2, spec.Depth / 2, spec.Height));
    }

    private static bool Intersects(Vector3 from, Vector3 to, Vector3 centre, Vector3 min, Vector3 max)
    {
        from -= centre;
        var direction = to - centre - from;
        float enter = 0, exit = 1;
        for (var axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-6f)
            {
                if (from[axis] < min[axis] || from[axis] > max[axis])
                    return false;
                continue;
            }
            var a = (min[axis] - from[axis]) / direction[axis];
            var b = (max[axis] - from[axis]) / direction[axis];
            enter = Math.Max(enter, Math.Min(a, b));
            exit = Math.Min(exit, Math.Max(a, b));
            if (enter > exit)
                return false;
        }
        return exit > 0.001f && enter < 0.999f;
    }

    public static bool Overlaps(StoreSpace space, FurniturePiece a, FurniturePiece b)
    {
        var delta = space.Delta(a.Position, b.Position);
        var specA = FurnitureCatalog.Spec(a);
        var specB = FurnitureCatalog.Spec(b);
        var reach = new Vector3(specA.Width, specA.Depth, specA.Height).Length() + new Vector3(specB.Width, specB.Depth, specB.Height).Length();
        if (delta.LengthSquared() > reach * reach)
            return false;
        var cornersA = FurnitureCatalog.Corners(a).Select(c => c - new Vector3(a.Position, 0)).ToArray();
        var cornersB = FurnitureCatalog.Corners(b).Select(c => c - new Vector3(b.Position - delta, 0)).ToArray();
        // Separating axes for the two rotated footprints, plus height; tilted pieces keep conservative bounds.
        Vector3[] axes = [new(MathF.Cos(a.Rotation), MathF.Sin(a.Rotation), 0), new(-MathF.Sin(a.Rotation), MathF.Cos(a.Rotation), 0),
            new(MathF.Cos(b.Rotation), MathF.Sin(b.Rotation), 0), new(-MathF.Sin(b.Rotation), MathF.Cos(b.Rotation), 0), Vector3.UnitZ];
        return axes.All(axis => cornersA.Max(c => Vector3.Dot(c, axis)) > cornersB.Min(c => Vector3.Dot(c, axis)) + 0.01f &&
            cornersB.Max(c => Vector3.Dot(c, axis)) > cornersA.Min(c => Vector3.Dot(c, axis)) + 0.01f);
    }
}
