using System.Numerics;

namespace Scp3008.Core;

internal static class RestockSight
{
    // shortcut: omnidirectional sight and catalogue boxes; add a facing cone when sneaking needs it.
    internal const float Range = 40;
    private const float EyeHeight = 1.6f;

    public static bool CanSee(StoreSpace space, Villager villager, FurniturePiece spot,
        IReadOnlyList<FurniturePiece> furniture, IReadOnlyList<Pillar> pillars, IReadOnlyList<FogBank> fog) =>
        CanSee(space, villager, spot, (eye, target) => furniture.Any(p => Blocks(space, p, eye, target)), pillars, fog);

    /// <summary>As above, with the Furniture already bucketed around the villager.</summary>
    public static bool CanSee(StoreSpace space, Villager villager, FurniturePiece spot,
        SightBlockers furniture, IReadOnlyList<Pillar> pillars, IReadOnlyList<FogBank> fog) =>
        CanSee(space, villager, spot, furniture.AnyBlocks, pillars, fog);

    private static bool CanSee(StoreSpace space, Villager villager, FurniturePiece spot,
        Func<Vector3, Vector3, bool> blocked, IReadOnlyList<Pillar> pillars, IReadOnlyList<FogBank> fog)
    {
        if (space.Distance(villager.Position, spot.Position) > villager.ViewRange)
            return false;
        // Every sight line starts at the eye: fog thick there hides everything.
        if (fog.Count > 0 && FogBank.CombinedDensityAt(space, fog, villager.Position) >= 0.6f)
            return false;
        var eye = new Vector3(villager.Position, EyeHeight + villager.Elevation);
        foreach (var corner in FurnitureCatalog.Corners(spot))
        {
            var delta = space.Delta(villager.Position, new Vector2(corner.X, corner.Y));
            var target = new Vector3(villager.Position + delta, corner.Z);
            var pillared = false;
            for (var i = 0; i < pillars.Count && !pillared; i++)
            {
                var p = pillars[i];
                pillared = Intersects(eye, target, new Vector3(villager.Position + space.Delta(villager.Position, p.Position), 0),
                    new Vector3(-p.Width / 2, -p.Width / 2, 0), new Vector3(p.Width / 2, p.Width / 2, p.Height));
            }
            if (pillared)
                continue;
            if (blocked(eye, target))
                continue;
            // Banks that end short of the sight line add nothing to it.
            var (banks, thickest) = Reaching(space, fog, villager.Position, delta);
            if (thickest < 0.599f)
                return true;
            var samples = Math.Max(1, (int)MathF.Ceiling(delta.Length()));
            var fogged = false;
            for (var i = 0; i <= samples; i++)
                if (FogBank.CombinedDensityAt(space, banks, villager.Position + delta * i / samples) >= 0.6f)
                {
                    fogged = true;
                    break;
                }
            if (!fogged)
                return true;
        }
        return false;
    }

    /// <summary>The banks that reach the line from <paramref name="from"/> along <paramref name="delta"/>, and the thickest their fog can be on it.</summary>
    private static (List<FogBank> Banks, float Thickest) Reaching(StoreSpace space, IReadOnlyList<FogBank> fog, Vector2 from, Vector2 delta)
    {
        var reaching = new List<FogBank>(0);
        var clear = 1f;
        foreach (var bank in fog)
        {
            var offset = space.Delta(from, bank.Position);
            var along = delta.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(offset, delta) / delta.LengthSquared(), 0, 1) : 0;
            var nearest = (offset - delta * along).Length();
            if (bank.Radius > 0 && nearest > bank.Radius * 1.001f + 0.01f)
                continue;
            reaching.Add(bank);
            // Fog only thins away from a bank's centre.
            clear *= bank.Radius > 0 ? 1f - bank.Density * FogBank.Falloff(nearest / bank.Radius) : 0f;
        }
        return (reaching, 1f - clear);
    }

    internal static bool Blocks(StoreSpace space, FurniturePiece piece, Vector3 eye, Vector3 target)
    {
        var spec = FurnitureCatalog.Spec(piece);
        if (spec.IsFloorCovering)
            return false;
        var offset = space.Delta(new Vector2(eye.X, eye.Y), piece.Position);
        return Blocks(piece, spec, offset, offset.Length(), BoxReach(spec), eye, target);
    }

    /// <summary>However a piece is turned, its box stays within this reach of its base centre.</summary>
    internal static float BoxReach(FurnitureSpec spec) => new Vector3(spec.Width / 2, spec.Depth / 2, spec.Height).Length() + 0.01f;

    /// <summary>As above, with the wrapped <paramref name="offset"/> from the eye to the piece, its length and the piece's <see cref="BoxReach"/> known.</summary>
    internal static bool Blocks(FurniturePiece piece, FurnitureSpec spec, Vector2 offset, float distance, float reach, Vector3 eye, Vector3 target)
    {
        if (distance > Range + MathF.Max(0, eye.Z - EyeHeight) * 2 + Math.Max(spec.Width, Math.Max(spec.Depth, spec.Height)))
            return false;
        // Cheap rejection by the reach of the box.
        var sight = new Vector2(target.X - eye.X, target.Y - eye.Y);
        var along = sight.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(offset, sight) / sight.LengthSquared(), 0, 1) : 0;
        if ((offset - sight * along).LengthSquared() > reach * reach)
            return false;
        var centre = new Vector3(new Vector2(eye.X, eye.Y) + offset, piece.Elevation);
        var (sin, cos) = MathF.SinCos(piece.Rotation);
        // Most pieces stand upright: no tilt or roll to turn by.
        var upright = piece.Tilt == 0 && piece.Roll == 0;
        var (tiltSin, tiltCos) = upright ? (0f, 1f) : MathF.SinCos(piece.Tilt);
        var (rollSin, rollCos) = upright ? (0f, 1f) : MathF.SinCos(piece.Roll);
        Vector3 Local(Vector3 point)
        {
            var d = point - centre;
            var across = d.X * cos + d.Y * sin;
            var forward = -d.X * sin + d.Y * cos;
            if (upright)
                return new Vector3(across, forward, d.Z);
            var up = d.Z * tiltCos + forward * tiltSin;
            var depth = forward * tiltCos - d.Z * tiltSin;
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

/// <summary>
/// The Furniture that may block sight lines from one viewpoint, bucketed on a local grid by the reach of
/// each piece's box, so a sight line only tests the pieces it passes near. Reused between viewpoints.
/// </summary>
internal sealed class SightBlockers
{
    private const float Cell = 2;
    // Covers rounding at cell borders: a piece is bucketed a little wider than it can reach.
    private const float Margin = 0.05f;
    private StoreSpace _space = null!;
    private Vector2 _origin;
    private float _half;
    private int _side;
    private FurniturePiece[] _pieces = [];
    // Per piece: its spec, its wrapped offset from the origin with its length, and its box reach.
    private FurnitureSpec[] _specs = [];
    private Vector2[] _offsets = [];
    private float[] _distances = [];
    private float[] _reaches = [];
    private int[] _bounds = [];
    private int[] _cellStart = [];
    private int[] _fill = [];
    private int[] _entries = [];
    private int[] _seen = [];
    private int _query;

    /// <summary>Buckets <paramref name="furniture"/> on a grid <paramref name="extent"/> metres each way around <paramref name="origin"/>.</summary>
    public void Prepare(StoreSpace space, Vector2 origin, IReadOnlyList<FurniturePiece> furniture, float extent)
    {
        _space = space;
        _origin = origin;
        _side = Math.Max(1, (int)MathF.Ceiling(2 * extent / Cell));
        _half = _side * Cell / 2;
        var cells = _side * _side;
        if (_cellStart.Length < cells + 1)
        {
            _cellStart = new int[cells + 1];
            _fill = new int[cells];
        }
        else
            Array.Clear(_cellStart, 0, cells + 1);
        if (_pieces.Length < furniture.Count)
        {
            _pieces = new FurniturePiece[furniture.Count];
            _specs = new FurnitureSpec[furniture.Count];
            _offsets = new Vector2[furniture.Count];
            _distances = new float[furniture.Count];
            _reaches = new float[furniture.Count];
            _bounds = new int[furniture.Count * 4];
            _seen = new int[furniture.Count];
        }
        var count = 0;
        var total = 0;
        foreach (var piece in furniture)
        {
            var spec = FurnitureCatalog.Spec(piece);
            if (spec.IsFloorCovering)
                continue;
            // However the piece is turned, its box stays within this reach of its base centre.
            var reach = new Vector3(spec.Width / 2, spec.Depth / 2, spec.Height).Length() + Margin;
            var offset = space.Delta(origin, piece.Position);
            var b = count * 4;
            _bounds[b] = CellOf(offset.X - reach);
            _bounds[b + 1] = CellOf(offset.X + reach);
            _bounds[b + 2] = CellOf(offset.Y - reach);
            _bounds[b + 3] = CellOf(offset.Y + reach);
            for (var y = _bounds[b + 2]; y <= _bounds[b + 3]; y++)
                for (var x = _bounds[b]; x <= _bounds[b + 1]; x++)
                    _cellStart[y * _side + x + 1]++;
            total += (_bounds[b + 1] - _bounds[b] + 1) * (_bounds[b + 3] - _bounds[b + 2] + 1);
            _pieces[count] = piece;
            _specs[count] = spec;
            _offsets[count] = offset;
            _distances[count] = offset.Length();
            _reaches[count] = RestockSight.BoxReach(spec);
            _seen[count] = 0;
            count++;
        }
        _query = 0;
        for (var i = 0; i < cells; i++)
            _cellStart[i + 1] += _cellStart[i];
        if (_entries.Length < total)
            _entries = new int[total];
        Array.Copy(_cellStart, _fill, cells);
        for (var i = 0; i < count; i++)
        {
            var b = i * 4;
            for (var y = _bounds[b + 2]; y <= _bounds[b + 3]; y++)
                for (var x = _bounds[b]; x <= _bounds[b + 1]; x++)
                    _entries[_fill[y * _side + x]++] = i;
        }
    }

    /// <summary>Whether any bucketed piece blocks the line from <paramref name="eye"/> to <paramref name="target"/>.</summary>
    public bool AnyBlocks(Vector3 eye, Vector3 target)
    {
        _query++;
        // Walk every grid cell the sight line passes through; cells beyond the grid clamp to its border, as pieces do.
        var from = _space.Delta(_origin, new Vector2(eye.X, eye.Y));
        var to = from + new Vector2(target.X - eye.X, target.Y - eye.Y);
        float gx = (from.X + _half) / Cell, gy = (from.Y + _half) / Cell;
        float dx = (to.X - from.X) / Cell, dy = (to.Y - from.Y) / Cell;
        int x = (int)MathF.Floor(gx), y = (int)MathF.Floor(gy);
        int endX = (int)MathF.Floor(gx + dx), endY = (int)MathF.Floor(gy + dy);
        int stepX = Math.Sign(endX - x), stepY = Math.Sign(endY - y);
        var nextX = stepX == 0 ? float.PositiveInfinity : ((stepX > 0 ? x + 1 : x) - gx) / dx;
        var nextY = stepY == 0 ? float.PositiveInfinity : ((stepY > 0 ? y + 1 : y) - gy) / dy;
        var deltaX = stepX == 0 ? float.PositiveInfinity : stepX / dx;
        var deltaY = stepY == 0 ? float.PositiveInfinity : stepY / dy;
        while (true)
        {
            if (CellBlocks(x, y, eye, target))
                return true;
            if (x == endX && y == endY)
                return false;
            if (y == endY || (x != endX && nextX < nextY))
            {
                x += stepX;
                nextX += deltaX;
            }
            else
            {
                y += stepY;
                nextY += deltaY;
            }
        }
    }

    private bool CellBlocks(int x, int y, Vector3 eye, Vector3 target)
    {
        var cell = Math.Clamp(y, 0, _side - 1) * _side + Math.Clamp(x, 0, _side - 1);
        for (var i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
        {
            var piece = _entries[i];
            if (_seen[piece] == _query)
                continue;
            _seen[piece] = _query;
            // Seen from the origin, as every Villager sees from where they stand, the offsets worked out hold.
            if (eye.X == _origin.X && eye.Y == _origin.Y
                    ? RestockSight.Blocks(_pieces[piece], _specs[piece], _offsets[piece], _distances[piece], _reaches[piece], eye, target)
                    : RestockSight.Blocks(_space, _pieces[piece], eye, target))
                return true;
        }
        return false;
    }

    private int CellOf(float offset) => Math.Clamp((int)MathF.Floor((offset + _half) / Cell), 0, _side - 1);
}

