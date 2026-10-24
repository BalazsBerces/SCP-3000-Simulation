using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Camping ground's ground, as a small map the floor shader draws over the Store floor: red is how much lawn there is
/// (laid turf with a crisply cut, straight edge; the shader draws it with a real grass texture), green how much of it is a
/// boardwalk aisle,
/// and blue and alpha the direction of the nearest path (as cos and sin of twice its angle, so both ways along it are the
/// same), along which the boardwalk's planks are laid across. It covers the Landmark's rectangle at
/// <see cref="MetresPerPixel"/>; the lawn always reaches under the Landmark's own Furniture, so no display stands half
/// on the bare Store floor. The ground is visual only.
/// </summary>
public static class CampTurf
{
    public const float MetresPerPixel = 0.25f;

    /// <summary>The lawn left around each piece of the Landmark beyond its footprint, in metres.</summary>
    private const float PieceMargin = 1.5f;

    /// <summary>The map of this Landmark's ground and its rectangle (centre X and Z, half size) for the floor shader.</summary>
    public static (ImageTexture Map, Vector4 Rect) Build(Landmark landmark, StoreSpace space, IEnumerable<FurniturePiece> pieces)
    {
        var width = (int)MathF.Ceiling(landmark.Size.X / MetresPerPixel);
        var height = (int)MathF.Ceiling(landmark.Size.Y / MetresPerPixel);
        var half = landmark.Size / 2f;
        // Each piece's footprint, as a circle with a margin of lawn around it, in the Landmark's own frame.
        var footprints = pieces.Where(p => p.LandmarkId == landmark.Id)
            .Select(p => (Centre: space.Delta(landmark.Centre, p.Position),
                Radius: MathF.Max(FurnitureCatalog.Spec(p.Type, p.Variant).Width, FurnitureCatalog.Spec(p.Type, p.Variant).Depth) / 2f + PieceMargin))
            .ToList();
        // Paths as segments in the Landmark's own frame (x east, y south, origin at its centre).
        var segments = new List<(System.Numerics.Vector2 A, System.Numerics.Vector2 B)>();
        foreach (var path in landmark.Paths)
            for (var i = 0; i + 1 < path.Count; i++)
                segments.Add((space.Delta(landmark.Centre, path[i]), space.Delta(landmark.Centre, path[i + 1])));
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = new System.Numerics.Vector2((x + 0.5f) * MetresPerPixel - half.X, (y + 0.5f) * MetresPerPixel - half.Y);
                var toEdge = MathF.Min(half.X - MathF.Abs(p.X), half.Y - MathF.Abs(p.Y));
                // The lawn's edge is straight, cut cleanly like laid turf a metre inside the Landmark's rectangle.
                var edge = toEdge - 1f;
                foreach (var (centre, radius) in footprints)
                    edge = MathF.Max(edge, MathF.Min(radius - (p - centre).Length(), toEdge));
                var lawn = Math.Clamp(edge / (MetresPerPixel * 1.5f) + 0.5f, 0f, 1f);
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    lawn = 0f;
                var distance = float.MaxValue;
                var direction = System.Numerics.Vector2.UnitX;
                foreach (var (a, b) in segments)
                {
                    var d = DistanceToSegment(p, a, b);
                    if (d < distance && (b - a).LengthSquared() > 1e-6f)
                        (distance, direction) = (d, System.Numerics.Vector2.Normalize(b - a));
                }
                // A boardwalk of even width with straight edges.
                var path = Math.Clamp((CampingGround.PathWidth / 2f - distance) / (MetresPerPixel * 1.5f) + 0.5f, 0f, 1f);
                var angle = 2f * MathF.Atan2(direction.Y, direction.X);
                var i = (y * width + x) * 4;
                data[i] = (byte)(lawn * 255f + 0.5f);
                data[i + 1] = (byte)(path * 255f + 0.5f);
                data[i + 2] = (byte)((MathF.Cos(angle) * 0.5f + 0.5f) * 255f + 0.5f);
                data[i + 3] = (byte)((MathF.Sin(angle) * 0.5f + 0.5f) * 255f + 0.5f);
            }
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        return (ImageTexture.CreateFromImage(image), new Vector4(landmark.Centre.X, landmark.Centre.Y, half.X, half.Y));
    }

    private static float DistanceToSegment(System.Numerics.Vector2 p, System.Numerics.Vector2 a, System.Numerics.Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(System.Numerics.Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return (p - (a + ab * t)).Length();
    }
}
