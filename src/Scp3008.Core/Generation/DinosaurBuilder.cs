using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// Builds one dinosaur of the Camping ground as a sculpture of whole, recognisable Furniture, tinted. The body, neck and tail are
/// rings of pieces round a curved spine, each piece laid along the spine and turned to face out of the surface (sofas round the body,
/// armchairs or loveseats round the neck and tail); the legs are sofas on end and armchairs, the head a bed or sofa, and the teeth,
/// claws and horns kids' chairs, the spikes on the back table lamps. The seed picks the Furniture of the body and the tail, the stance, the swing of the tail and
/// the turn of the head.
/// In its own frame u runs forward, v to the left and z up, in metres from the ground below the middle of its body.
/// </summary>
internal sealed class DinosaurBuilder
{
    private readonly LandmarkBuilder _b;
    private readonly SeededRandom _random;
    private readonly Vector2 _forward;
    private readonly Vector2 _left;
    private readonly Vector2 _at;
    private readonly DinosaurKind _kind;
    private readonly (FurnitureType Type, int Variant) _body;
    private readonly FurnitureType _tail;
    // The spines built so far, for the height of the back under the spikes.
    private readonly List<List<(Vector3 Point, float Radius)>> _spines = [];
    // The boxes of the body placed so far, cut down to the surface something rests on, for standing the lamps on.
    private readonly List<(Vector3 Centre, Vector3 Width, Vector3 Top, Vector3 Depth, Vector3 Half, float Resting)> _solids = [];

    public DinosaurBuilder(LandmarkBuilder b, Vector2 at, float heading, DinosaurKind kind)
    {
        _b = b;
        _random = b.Random;
        _at = at;
        _kind = kind;
        _forward = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        _left = new Vector2(-_forward.Y, _forward.X);
        MainTint = kind switch
        {
            DinosaurKind.TyrannosaurusRex => (int)FurniturePalettes.DinosaurTints.OrangeRed,
            DinosaurKind.Brachiosaurus => (int)FurniturePalettes.DinosaurTints.Green,
            _ => (int)FurniturePalettes.DinosaurTints.Teal,
        };
        (FurnitureType, int)[] bodies = [(FurnitureType.Sofa, 0), (FurnitureType.Sofa, 2), (FurnitureType.Bed, 0)];
        _body = bodies[_random.Next(bodies.Length)];
        FurnitureType[] tails = [FurnitureType.Armchair, FurnitureType.Sofa];
        _tail = tails[_random.Next(tails.Length)];
    }

    public int MainTint { get; }

    private const int Cream = (int)FurniturePalettes.DinosaurTints.Cream;
    private const int Dark = (int)FurniturePalettes.DinosaurTints.Dark;
    private const int Yellow = (int)FurniturePalettes.DinosaurTints.Yellow;

    private static readonly Vector3 Up = Vector3.UnitZ;
    private static readonly Vector3 Forward = Vector3.UnitX;
    private static readonly Vector3 Left = Vector3.UnitY;

    public void Build()
    {
        var swing = _random.Range(-1.6f, 1.6f);
        var headYaw = _random.Range(-0.9f, 0.9f);
        var lift = _random.Range(-0.5f, 0.5f);
        var stride = _random.Range(-0.6f, 0.6f);
        switch (_kind)
        {
            case DinosaurKind.TyrannosaurusRex: Rex(swing, headYaw, lift, stride); break;
            case DinosaurKind.Brachiosaurus: Brachiosaurus(swing, headYaw, lift, stride); break;
            default: Triceratops(swing, headYaw, lift, stride); break;
        }
    }

    private void Rex(float swing, float headYaw, float lift, float stride)
    {
        Barrel(_body, MainTint, [(new(-2.4f, 0f, 3.5f), 1.3f), (new(0f, 0f, 3.8f), 1.6f), (new(2.2f, 0f, 4.4f), 1.3f)]);
        var head = new Vector3(4.5f, headYaw, 5.3f + lift);
        Barrel((FurnitureType.Armchair, 0), MainTint, [(new(2.2f, 0f, 4.5f), 1.0f), (head - new Vector3(1.2f, 0f, 0.2f), 0.85f)]);
        Tail([(new(-2.4f, 0f, 3.5f), 1.2f), (new(-4.6f, swing * 0.5f, 3.0f), 0.85f), (new(-7.0f, swing, 2.4f), 0.4f)]);

        // The head: a bed with its foot forward, the lower jaw a mattress hanging open below it, cream teeth along both.
        var nose = Vector3.Normalize(Forward + new Vector3(0f, headYaw * 0.15f, 0f));
        var side = Vector3.Cross(Up, nose);
        PlaceAlong(FurnitureType.Bed, 0, head, nose, Up, MainTint);
        var jawDrop = Vector3.Normalize(nose - Up * 0.35f);
        PlaceAlong(FurnitureType.Mattress, 0, head + new Vector3(0.1f, 0f, -0.75f), jawDrop, Vector3.Cross(jawDrop, side), MainTint);
        for (var k = 0; k < 3; k++)
            foreach (var s in new[] { -1f, 1f })
            {
                var at = head + nose * (0.25f + k * 0.35f) + side * (s * 0.6f);
                Place(FurnitureType.KidsChair, 0, at - Up * 0.6f, side, -Up, Cream);
            }
        Eyes(head + nose * 0.2f + Up * 0.55f, side, 0.95f);

        foreach (var s in new[] { -1f, 1f })
        {
            var step = stride * s;
            // Thigh and shin of sofas on end, a loveseat for the foot, kids' chairs for its claws.
            Limb(FurnitureType.Sofa, new(0.2f + step, s * 1.15f, 3.7f), new(0.7f + step, s * 1.2f, 1.6f), s);
            Limb(FurnitureType.Sofa, new(0.7f + step, s * 1.2f, 2.0f), new(0.5f + step, s * 1.2f, 0f), s);
            var foot = new Vector3(1.1f + step, s * 1.2f, 0f);
            Place(FurnitureType.Sofa, 1, foot + Up * 0.43f, Forward, Up, MainTint);
            for (var k = -1; k <= 1; k++)
                Place(FurnitureType.KidsChair, 0, foot + new Vector3(1.0f, k * 0.3f, 0.28f), Left, Forward, Cream);
            // The small arm: one armchair against the chest, with a claw.
            var arm = new Vector3(2.5f, s * 0.95f, 3.55f);
            Place(FurnitureType.Armchair, 0, arm, Left, Vector3.Normalize(Up + Forward * 0.6f), MainTint);
            Place(FurnitureType.KidsChair, 0, arm + new Vector3(0.5f, 0f, -0.6f), Left, -Up, Cream);
        }
        Ridge(new(-5.5f, swing * 0.75f, 3.4f), new(1.6f, 0f, 5.6f), 7);
    }

    private void Brachiosaurus(float swing, float headYaw, float lift, float stride)
    {
        Barrel(_body, MainTint, [(new(-2.6f, 0f, 4.3f), 1.4f), (new(0f, 0f, 4.6f), 1.7f), (new(2.4f, 0f, 4.9f), 1.5f)]);
        var lean = lift * 1.2f;
        var head = new Vector3(5.6f + lean, headYaw * 0.4f, 13.6f);
        (Vector3 Point, float Radius)[] neck =
            [(new(2.4f, 0f, 5.2f), 1.05f), (new(3.6f + lean * 0.5f, headYaw * 0.15f, 9.0f), 0.85f), (head - new Vector3(0.9f, 0f, 0.5f), 0.8f)];
        Barrel((FurnitureType.Armchair, 0), MainTint, neck);
        NeckLamps(neck, 5);
        Tail([(new(-2.6f, 0f, 4.3f), 1.15f), (new(-6.0f, swing * 0.5f, 2.6f), 0.8f), (new(-9.0f, swing, 1.0f), 0.4f)]);

        var nose = Vector3.Normalize(Forward + new Vector3(0f, headYaw * 0.3f, -0.15f));
        var side = Vector3.Cross(Up, nose);
        PlaceAlong(FurnitureType.Sofa, 0, head, side, Vector3.Cross(nose, side), MainTint);
        Eyes(head + nose * 0.3f + Up * 0.4f, side, 0.95f);

        foreach (var along in new[] { -1f, 1f })
            foreach (var s in new[] { -1f, 1f })
            {
                var u = along * 2.0f + stride * along * s;
                // A column: a sofa on end, an armchair on it, and an armchair on end up into the body.
                Limb(FurnitureType.Sofa, new(u, s * 1.1f, 0f), new(u, s * 1.1f, 2.2f), s);
                Place(FurnitureType.Armchair, 0, new(u, s * 1.1f, 2.65f), Forward, Up, MainTint);
                Limb(FurnitureType.Armchair, new(u, s * 1.1f, 3.1f), new(u, s * 1.1f, 3.95f), s);
            }
        Ridge(new(-7.5f, swing * 0.85f, 2.1f), new(0.8f, 0f, 6.4f), 7);
    }

    private void Triceratops(float swing, float headYaw, float lift, float stride)
    {
        Barrel(_body, MainTint, [(new(-2.2f, 0f, 2.0f), 1.2f), (new(0f, 0f, 2.2f), 1.45f), (new(2.0f, 0f, 2.1f), 1.2f)]);
        Tail([(new(-2.2f, 0f, 2.0f), 1.0f), (new(-5.2f, swing * 0.6f, 1.1f), 0.4f)]);

        // The head: a bed, nose down; the frill a fan of beds standing up behind it, sweeping down over the neck on both sides;
        // horns and the beak in cream.
        var head = new Vector3(3.5f, headYaw * 0.5f, 1.7f + lift * 0.3f);
        var nose = Vector3.Normalize(Forward + new Vector3(0f, headYaw * 0.3f, -0.25f));
        var side = Vector3.Normalize(Vector3.Cross(Up, nose));
        PlaceAlong(FurnitureType.Bed, 0, head, nose, Vector3.Cross(nose, side), MainTint);
        var frillUp = Vector3.Normalize(Up - nose * 0.35f);
        var frillFace = Vector3.Cross(side, frillUp);
        var pivot = head - nose * 0.9f + Up * 0.25f;
        PlaceAlong(FurnitureType.Bed, 0, pivot + frillUp * 1.05f, frillUp, frillFace, Yellow);
        foreach (var s in new[] { -1f, 1f })
            for (var k = 1; k <= 2; k++)
            {
                // Each bed further out sits a little further back, so the overlapping beds do not fight for the same surface.
                var angle = k * 0.75f;
                var spoke = frillUp * MathF.Cos(angle) + side * (s * MathF.Sin(angle));
                PlaceAlong(FurnitureType.Bed, 0, pivot + spoke * 1.05f - frillFace * (k * 0.12f), spoke, frillFace, Yellow);
            }
        // The neck: a short ring of armchairs from the front of the body to the back of the head, under the frill.
        Barrel((FurnitureType.Armchair, 0), MainTint, [(new(1.6f, 0f, 2.2f), 1.05f), (head - nose * 0.7f + Up * 0.3f, 0.85f)]);
        foreach (var s in new[] { -1f, 1f })
        {
            var horn = Vector3.Normalize(nose + Up * 0.9f);
            Place(FurnitureType.KidsChair, 0, head + nose * 0.2f + side * (s * 0.5f) + Up * 0.8f, side, horn, Cream);
        }
        Place(FurnitureType.KidsChair, 0, head + nose * 1.1f + Up * 0.45f, side, Vector3.Normalize(nose + Up), Cream);
        Eyes(head + nose * 0.3f + Up * 0.5f, side, 1.15f);

        foreach (var along in new[] { -1f, 1f })
            foreach (var s in new[] { -1f, 1f })
            {
                var u = along * 1.5f + stride * along * s * 0.6f;
                // The foot an armchair, the leg an armchair on end on it, up into the body.
                Place(FurnitureType.Armchair, 0, new(u, s * 0.95f, 0.45f), Forward, Up, MainTint);
                Limb(FurnitureType.Armchair, new(u, s * 0.95f, 0.9f), new(u, s * 0.95f, 1.75f), s);
            }
        Ridge(new(-4.2f, swing * 0.45f, 2.1f), new(1.6f, 0f, 3.4f), 5);
    }

    // --- Building blocks ---

    /// <summary>
    /// Rings of pieces round a spine through points with radii: each piece lies along the spine, its top facing out of the surface,
    /// its outside on the radius. Where the spine is thinner than a piece, single pieces follow it; the hidden underside is left out.
    /// </summary>
    private void Barrel((FurnitureType Type, int Variant) piece, int tint, (Vector3 Point, float Radius)[] spine)
    {
        _spines.Add([.. spine]);
        var spec = FurnitureCatalog.Spec(piece.Type, piece.Variant);
        // The piece's longer side lies along the spine (a sofa's width, a bed's length).
        var lengthwise = spec.Depth > spec.Width;
        var (along, round) = lengthwise ? (spec.Depth, spec.Width) : (spec.Width, spec.Depth);
        var lengths = new float[spine.Length];
        for (var i = 1; i < spine.Length; i++)
            lengths[i] = lengths[i - 1] + Vector3.Distance(spine[i - 1].Point, spine[i].Point);
        var total = lengths[^1];
        var step = along * 0.92f;
        var station = 0;
        for (var s = step / 2f; s < total + step / 4f; s += step, station++)
        {
            var i = 1;
            while (i < spine.Length - 1 && lengths[i] < s)
                i++;
            var t = Math.Clamp((s - lengths[i - 1]) / (lengths[i] - lengths[i - 1]), 0f, 1f);
            var centre = Vector3.Lerp(spine[i - 1].Point, spine[i].Point, t);
            var radius = float.Lerp(spine[i - 1].Radius, spine[i].Radius, t);
            var tangent = Vector3.Normalize(spine[i].Point - spine[i - 1].Point);
            var up = Vector3.Normalize(Up - tangent * Vector3.Dot(tangent, Up));
            var across = Vector3.Cross(tangent, up);
            if (radius < spec.Height * 0.9f)
            {
                Lay(centre, up);
                continue;
            }
            var mid = radius - spec.Height / 2f;
            var count = Math.Max(3, (int)MathF.Ceiling(MathF.Tau * mid / round));
            var offset = station % 2 * MathF.PI / count;
            for (var k = 0; k < count; k++)
            {
                var angle = offset + k * MathF.Tau / count;
                var outward = up * MathF.Cos(angle) + across * MathF.Sin(angle);
                if (outward.Z < -0.75f)
                    continue;
                Lay(centre + outward * mid, outward);
            }

            void Lay(Vector3 at, Vector3 top)
            {
                if (lengthwise)
                    PlaceAlong(piece.Type, piece.Variant, at, tangent, top, tint);
                else
                    Place(piece.Type, piece.Variant, at, tangent, top, tint);
            }
        }
    }

    /// <summary>The tail: rings of armchairs, or of loveseats where it is thick enough, tapering to single pieces.</summary>
    private void Tail((Vector3 Point, float Radius)[] spine) =>
        Barrel(_tail == FurnitureType.Sofa ? (FurnitureType.Sofa, 1) : (FurnitureType.Armchair, 0), MainTint, spine);

    /// <summary>
    /// A leg or column on one side of the dinosaur: pieces in its main colour end to end from a to b, each standing on end with its
    /// width along the limb and its seat facing out to that side.
    /// </summary>
    private void Limb(FurnitureType type, Vector3 a, Vector3 b, float side)
    {
        var spec = FurnitureCatalog.Spec(type);
        var along = Vector3.Normalize(b - a);
        var count = Math.Max(1, (int)MathF.Round(Vector3.Distance(a, b) / spec.Width));
        var outward = Vector3.Normalize(Left * side - along * Vector3.Dot(Left * side, along));
        for (var k = 0; k < count; k++)
            Place(type, 0, Vector3.Lerp(a, b, (k + 0.5f) / count), along, outward, MainTint);
    }

    /// <summary>A row of table lamps standing upright on the back from one point to another, the dinosaur's spikes; they light up at night.</summary>
    private void Ridge(Vector3 from, Vector3 to, int count)
    {
        var along = Vector3.Normalize(to - from);
        for (var k = 0; k < count; k++)
        {
            var at = Vector3.Lerp(from, to, k / (float)(count - 1));
            StandLamp(new Vector3(at.X, at.Y, MathF.Max(at.Z, Surface(at.X))), Up, along);
        }
    }

    /// <summary>
    /// Stands a yellow table lamp on the Furniture below a point, its top along a direction: on the seat of the piece found looking
    /// back down that direction, or a little along the body where the point falls in a gap between pieces.
    /// </summary>
    private void StandLamp(Vector3 at, Vector3 top, Vector3 along)
    {
        var height = FurnitureCatalog.Spec(FurnitureType.TableLamp).Height;
        foreach (var nudge in new[] { 0f, 0.2f, -0.2f, 0.4f, -0.4f, 0.6f, -0.6f })
        {
            var origin = at + along * nudge + top * 3f;
            if (Hit(origin, -top) is not { } distance)
                continue;
            var width = Vector3.Normalize(Vector3.Cross(MathF.Abs(along.Z) < 0.99f ? along : Forward, top));
            Place(FurnitureType.TableLamp, 0, origin - top * distance + top * (height / 2f - 0.03f), width, top, Yellow);
            return;
        }
    }

    /// <summary>How far a ray goes before it meets the resting surface of a piece placed so far; null if it meets none.</summary>
    private float? Hit(Vector3 origin, Vector3 direction)
    {
        float? nearest = null;
        foreach (var solid in _solids)
        {
            var p = origin - solid.Centre;
            Vector3 o = new(Vector3.Dot(p, solid.Width), Vector3.Dot(p, solid.Top), Vector3.Dot(p, solid.Depth));
            Vector3 d = new(Vector3.Dot(direction, solid.Width), Vector3.Dot(direction, solid.Top), Vector3.Dot(direction, solid.Depth));
            Vector3 min = new(-solid.Half.X, -solid.Half.Y, -solid.Half.Z);
            Vector3 max = new(solid.Half.X, -solid.Half.Y + solid.Resting, solid.Half.Z);
            var (near, far) = (0f, float.MaxValue);
            var missed = false;
            for (var axis = 0; axis < 3 && !missed; axis++)
            {
                var (oa, da, lo, hi) = (o[axis], d[axis], min[axis], max[axis]);
                if (MathF.Abs(da) < 1e-6f)
                {
                    missed = oa < lo || oa > hi;
                    continue;
                }
                var (t1, t2) = ((lo - oa) / da, (hi - oa) / da);
                near = MathF.Max(near, MathF.Min(t1, t2));
                far = MathF.Min(far, MathF.Max(t1, t2));
                missed = near > far;
            }
            if (!missed && (nearest is null || near < nearest))
                nearest = near;
        }
        return nearest;
    }

    /// <summary>The height above its bottom a piece's resting surface is at: the seat of a sofa or armchair, a bed's mattress.</summary>
    private static float RestFraction(FurnitureType type) => type switch
    {
        FurnitureType.Sofa or FurnitureType.Armchair => 0.5f,
        FurnitureType.Bed => 0.6f,
        _ => 1f,
    };

    /// <summary>Table lamps up the back of a neck, between its ends, each standing straight out of the neck's back-facing side.</summary>
    private void NeckLamps((Vector3 Point, float Radius)[] spine, int count)
    {
        var lengths = new float[spine.Length];
        for (var i = 1; i < spine.Length; i++)
            lengths[i] = lengths[i - 1] + Vector3.Distance(spine[i - 1].Point, spine[i].Point);
        for (var k = 1; k <= count; k++)
        {
            var s = lengths[^1] * k / (count + 1f);
            var i = 1;
            while (i < spine.Length - 1 && lengths[i] < s)
                i++;
            var t = (s - lengths[i - 1]) / (lengths[i] - lengths[i - 1]);
            var centre = Vector3.Lerp(spine[i - 1].Point, spine[i].Point, t);
            var radius = float.Lerp(spine[i - 1].Radius, spine[i].Radius, t);
            var tangent = Vector3.Normalize(spine[i].Point - spine[i - 1].Point);
            var back = Up - Forward;
            var top = Vector3.Normalize(back - tangent * Vector3.Dot(tangent, back));
            StandLamp(centre + top * radius, top, tangent);
        }
    }

    private void Eyes(Vector3 at, Vector3 side, float apart)
    {
        foreach (var s in new[] { -1f, 1f })
            Place(FurnitureType.PlayCube, 0, at + side * (s * apart / 2f), side, Up, Dark);
    }

    /// <summary>The top of the body's outer surface above a point of the spine, by the spines built so far.</summary>
    private float Surface(float u)
    {
        var top = 0f;
        foreach (var spine in _spines)
            for (var i = 1; i < spine.Count; i++)
            {
                var (a, b) = (spine[i - 1], spine[i]);
                if (u < MathF.Min(a.Point.X, b.Point.X) || u > MathF.Max(a.Point.X, b.Point.X) || a.Point.X == b.Point.X)
                    continue;
                var t = (u - a.Point.X) / (b.Point.X - a.Point.X);
                top = MathF.Max(top, float.Lerp(a.Point.Z, b.Point.Z, t) + float.Lerp(a.Radius, b.Radius, t));
            }
        return top;
    }

    /// <summary>Places a piece with its depth (its length, for beds and mattresses) along a direction and its top facing up.</summary>
    private void PlaceAlong(FurnitureType type, int variant, Vector3 centre, Vector3 depth, Vector3 top, int tint) =>
        Place(type, variant, centre, Vector3.Cross(depth, top), top, tint);

    /// <summary>
    /// Places a piece, in the dinosaur's frame, with the middle of its box at a point, its width along one direction and its top
    /// facing another (perpendicular to it).
    /// </summary>
    private void Place(FurnitureType type, int variant, Vector3 centre, Vector3 width, Vector3 top, int tint)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        // The lamps stand only on the body, in the main colour: not on the frill, horns or eyes.
        if (tint == MainTint)
        {
            var w = Vector3.Normalize(width);
            var t = Vector3.Normalize(top - w * Vector3.Dot(top, w));
            _solids.Add((centre, w, t, Vector3.Cross(w, t), new Vector3(spec.Width, spec.Height, spec.Depth) / 2f, spec.Height * RestFraction(type)));
        }
        _b.PutTurned(type, ToLandmark(centre) + new Vector3(_at, 0f), ToLandmark(width), ToLandmark(top), variant, tint);
    }

    private Vector3 ToLandmark(Vector3 uvz) => new(_forward * uvz.X + _left * uvz.Y, uvz.Z);
}
