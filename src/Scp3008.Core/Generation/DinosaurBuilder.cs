using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// Builds one dinosaur of the Camping ground out of ordinary tinted Furniture. The body is solid volumes (ellipsoids and tapering tubes)
/// filled with one Furniture type on that type's own grid, hollow inside; small PlayCubes are its teeth, eyes, horns and spikes.
/// The seed picks the body's Furniture type, the stance, the swing of the tail and the turn of the head.
/// In its own frame u runs forward, v to the left and z up, in metres from the ground below the middle of its body.
/// </summary>
internal sealed class DinosaurBuilder
{
    private readonly LandmarkBuilder _b;
    private readonly SeededRandom _random;
    private readonly Vector2 _forward;
    private readonly Vector2 _left;
    private readonly float _heading;
    private readonly Vector2 _at;
    private readonly DinosaurKind _kind;
    private readonly FurnitureType _body;
    private readonly float _cu, _cv, _cz;
    private readonly Dictionary<(int, int, int), int> _cells = [];

    public DinosaurBuilder(LandmarkBuilder b, Vector2 at, float heading, DinosaurKind kind)
    {
        _b = b;
        _random = b.Random;
        _at = at;
        _heading = heading;
        _kind = kind;
        _forward = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        _left = new Vector2(-_forward.Y, _forward.X);
        MainTint = kind switch
        {
            DinosaurKind.TyrannosaurusRex => (int)FurniturePalettes.DinosaurTints.OrangeRed,
            DinosaurKind.Brachiosaurus => (int)FurniturePalettes.DinosaurTints.Green,
            _ => (int)FurniturePalettes.DinosaurTints.Teal,
        };
        FurnitureType[] bodies = [FurnitureType.CushionBin, FurnitureType.Armchair, FurnitureType.BlanketStack];
        _body = bodies[_random.Next(bodies.Length)];
        var spec = FurnitureCatalog.Spec(_body);
        (_cu, _cv, _cz) = (spec.Width, spec.Depth, spec.Height);
    }

    public int MainTint { get; }

    private const int Cream = (int)FurniturePalettes.DinosaurTints.Cream;
    private const int Dark = (int)FurniturePalettes.DinosaurTints.Dark;
    private const int Yellow = (int)FurniturePalettes.DinosaurTints.Yellow;

    public void Build()
    {
        var swing = _random.Range(-1.6f, 1.6f);
        var headYaw = _random.Range(-0.9f, 0.9f);
        var lift = _random.Range(-0.5f, 0.5f);
        var stride = _random.Range(-0.8f, 0.8f);
        switch (_kind)
        {
            case DinosaurKind.TyrannosaurusRex: Rex(swing, headYaw, lift, stride); break;
            case DinosaurKind.Brachiosaurus: Brachiosaurus(swing, headYaw, lift, stride); break;
            default: Triceratops(swing, headYaw, lift, stride); break;
        }
        Emit();
    }

    private void Rex(float swing, float headYaw, float lift, float stride)
    {
        Ellipsoid(new(0f, 0f, 3.6f), new(2.7f, 1.4f, 1.9f), MainTint);
        Tube(new(-2.2f, 0f, 3.4f), new(-6.0f, swing, 2.2f), 1.2f, 0.35f, MainTint);
        Tube(new(2.0f, 0f, 4.4f), new(3.6f, headYaw * 0.3f, 5.0f + lift), 1.0f, 0.9f, MainTint);
        Ellipsoid(new(4.6f, headYaw, 5.2f + lift), new(1.4f, 0.8f, 0.8f), MainTint);
        Ellipsoid(new(4.6f, headYaw, 4.5f + lift), new(1.2f, 0.6f, 0.3f), MainTint);
        foreach (var side in new[] { -1f, 1f })
        {
            var s = stride * side;
            Tube(new(0.2f + s, side * 1.0f, 2.2f), new(0.4f + s, side * 1.0f, 0f), 0.95f, 0.7f, MainTint);
            Ellipsoid(new(0.9f + s, side * 1.0f, 0.3f), new(0.9f, 0.5f, 0.3f), MainTint);
            Tube(new(2.2f, side * 1.2f, 3.0f), new(2.9f, side * 1.3f, 2.2f), 0.35f, 0.3f, MainTint);
        }
        var head = new Vector3(4.6f, headYaw, 5.2f + lift);
        Eyes(head + new Vector3(0.7f, 0f, 0.35f), 0.85f);
        for (var k = 0; k < 4; k++)
            foreach (var side in new[] { -1f, 1f })
                Accent(new(head.X + 0.6f + k * 0.3f, head.Y + side * 0.45f, 4.55f + lift - 0.1f), Cream);
        for (var k = 0; k < 5; k++)
            Accent(new(-1.6f - k * 0.9f, Mathf(-1.6f - k * 0.9f, swing, -2.2f, -6.0f), 3.2f - k * 0.2f + 0.5f - 0.35f * k * 0f), Yellow);
    }

    private void Brachiosaurus(float swing, float headYaw, float lift, float stride)
    {
        const float top = 5.9f;
        Ellipsoid(new(0f, 0f, 4.2f), new(3.3f, 1.5f, 1.7f), MainTint);
        Tube(new(-2.8f, 0f, 3.8f), new(-8.5f, swing, 0.9f), 1.0f, 0.3f, MainTint);
        var lean = lift * 1.5f;
        Tube(new(2.6f, 0f, 5.0f), new(4.8f + lean, headYaw * 0.3f, 13.2f), 1.1f, 0.55f, MainTint);
        var head = new Vector3(5.5f + lean, headYaw, 14.0f);
        Ellipsoid(head, new(0.95f, 0.5f, 0.5f), MainTint);
        foreach (var along in new[] { -1f, 1f })
            foreach (var side in new[] { -1f, 1f })
            {
                var s = stride * along * side;
                Tube(new(along * 2.0f + s, side * 1.1f, 3.5f), new(along * 2.0f + s, side * 1.1f, 0f), 0.9f, 0.85f, MainTint);
            }
        Eyes(head + new Vector3(0.4f, 0f, 0.15f), 0.45f);
        for (var k = 0; k < 4; k++)
            Accent(new(5.9f + lean + k * 0.0f, headYaw + (k - 1.5f) * 0.18f, 13.5f), Cream);
        // A ridge of yellow spikes along the back and the neck.
        for (var k = 0; k < 5; k++)
            Accent(new(-2.2f + k * 1.1f, 0f, top + 0.15f - 0.35f * MathF.Abs(k - 2) * 0.5f), Yellow);
        BuildStair();
    }

    /// <summary>A stair on the flank, climbing from beside the tail to the back, whose top is a Lookout.</summary>
    private void BuildStair()
    {
        const int steps = 12;
        const float cz = 4.2f;
        for (var i = 0; i < steps; i++)
        {
            var u = -8.4f + i * 0.72f;
            var z = i * 0.5f;
            var inside = 1f - (u / 3.3f) * (u / 3.3f) - ((z + 0.25f - cz) / 1.7f) * ((z + 0.25f - cz) / 1.7f);
            var edge = inside > 0f ? 1.5f * MathF.Sqrt(inside) : 0f;
            var v = MathF.Max(0.9f, edge + 0.55f);
            if (i == steps - 1)
                v = 1.7f;
            Place(FurnitureType.BlanketStack, new Vector3(u, v, z), _heading + MathF.PI / 2f, Cream);
        }
        // A saddle of mattresses on the back, and the Lookout on it.
        Place(FurnitureType.Mattress, new Vector3(-0.6f, 0.2f, 5.75f), _heading, Cream);
        var at = _at + _forward * -0.6f + _left * 0.2f;
        _b.Lookouts.Add(new Vector3(at.X, at.Y, 5.75f + FurnitureCatalog.Spec(FurnitureType.Mattress).Height));
    }

    private void Triceratops(float swing, float headYaw, float lift, float stride)
    {
        Ellipsoid(new(0f, 0f, 2.1f), new(2.8f, 1.5f, 1.5f), MainTint);
        Tube(new(-2.5f, 0f, 2.0f), new(-4.5f, swing * 0.6f, 1.2f), 0.9f, 0.3f, MainTint);
        var head = new Vector3(3.6f, headYaw, 2.0f + lift * 0.3f);
        Ellipsoid(head, new(1.3f, 0.9f, 0.8f), MainTint);
        Ellipsoid(new(2.6f, headYaw * 0.6f, 3.0f + lift * 0.3f), new(0.4f, 1.6f, 1.4f), MainTint);
        foreach (var along in new[] { -1f, 1f })
            foreach (var side in new[] { -1f, 1f })
            {
                var s = stride * along * side * 0.6f;
                Tube(new(along * 1.7f + s, side * 1.0f, 1.7f), new(along * 1.7f + s, side * 1.0f, 0f), 0.75f, 0.7f, MainTint);
            }
        Eyes(head + new Vector3(0.7f, 0f, 0.4f), 0.7f);
        foreach (var side in new[] { -1f, 1f })
            for (var k = 0; k < 3; k++)
                Accent(new(head.X + 0.9f + k * 0.35f, head.Y + side * 0.55f, head.Z + 0.5f + k * 0.35f), Cream);
        for (var k = 0; k < 2; k++)
            Accent(new(head.X + 1.25f + k * 0.25f, head.Y, head.Z + 0.35f + k * 0.3f), Cream);
        for (var k = -4; k <= 4; k++)
            Accent(new(2.6f - 0.15f * MathF.Abs(k) * 0f, headYaw * 0.6f + k * 0.38f, 4.45f + lift * 0.3f - 0.05f * k * k * 0.25f), Yellow);
    }

    /// <summary>The interpolated sideways swing of a tail at a position along it.</summary>
    private static float Mathf(float u, float swing, float from, float to) => swing * Math.Clamp((u - from) / (to - from), 0f, 1f);

    private void Eyes(Vector3 at, float apart)
    {
        foreach (var side in new[] { -1f, 1f })
            Accent(new(at.X, at.Y + side * apart * 0.5f, at.Z), Dark);
    }

    // --- Volumes ---

    private void Ellipsoid(Vector3 c, Vector3 r, int tint) =>
        Fill(c - r, c + r, p => Sq((p.X - c.X) / r.X) + Sq((p.Y - c.Y) / r.Y) + Sq((p.Z - c.Z) / r.Z) <= 1f, tint);

    private void Tube(Vector3 a, Vector3 b, float ra, float rb, int tint)
    {
        var axis = b - a;
        var length2 = axis.LengthSquared();
        var reach = MathF.Max(ra, rb);
        Fill(Vector3.Min(a, b) - new Vector3(reach), Vector3.Max(a, b) + new Vector3(reach), p =>
        {
            var t = Math.Clamp(Vector3.Dot(p - a, axis) / length2, 0f, 1f);
            return Vector3.Distance(p, a + axis * t) <= ra + (rb - ra) * t;
        }, tint);
    }

    private static float Sq(float x) => x * x;

    private void Fill(Vector3 low, Vector3 high, Func<Vector3, bool> inside, int tint)
    {
        for (var iu = (int)MathF.Floor(low.X / _cu); iu <= (int)MathF.Ceiling(high.X / _cu); iu++)
            for (var iv = (int)MathF.Floor(low.Y / _cv); iv <= (int)MathF.Ceiling(high.Y / _cv); iv++)
                for (var iz = Math.Max(0, (int)MathF.Floor(low.Z / _cz)); iz <= (int)MathF.Ceiling(high.Z / _cz); iz++)
                    if (inside(new Vector3(iu * _cu, iv * _cv, (iz + 0.5f) * _cz)))
                        _cells[(iu, iv, iz)] = tint;
    }

    private void Emit()
    {
        var around = new (int, int, int)[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        foreach (var ((iu, iv, iz), tint) in _cells.OrderBy(c => c.Key))
        {
            // Hollow: a cell with a neighbour on every side, or the ground beneath it, is out of sight.
            if (around.All(d => _cells.ContainsKey((iu + d.Item1, iv + d.Item2, iz + d.Item3)) || (iz + d.Item3 < 0)))
                continue;
            var at = _at + _forward * (iu * _cu) + _left * (iv * _cv);
            _b.Put(_body, at, iz * _cz, _heading, tint: tint);
        }
    }

    private void Place(FurnitureType type, Vector3 uvz, float rotation, int tint)
    {
        var at = _at + _forward * uvz.X + _left * uvz.Y;
        _b.Put(type, at, uvz.Z, rotation, tint: tint);
    }

    /// <summary>A small PlayCube, centred on a point, for the teeth, eyes, horns and spikes.</summary>
    private void Accent(Vector3 uvz, int tint)
    {
        var size = FurnitureCatalog.Spec(FurnitureType.PlayCube).Height;
        Place(FurnitureType.PlayCube, new Vector3(uvz.X, uvz.Y, MathF.Max(0f, uvz.Z - size / 2f)), _heading, tint);
    }
}
