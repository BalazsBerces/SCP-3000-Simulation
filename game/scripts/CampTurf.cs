using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Camping ground's ground, as a small map the floor shader draws over the Store floor: red is how much artificial turf
/// there is (organic edge and bare patches, not a grid), green how much of it is a winding visitor path. It covers the
/// Landmark's rectangle at <see cref="MetresPerPixel"/>. The ground is visual only.
/// </summary>
public static class CampTurf
{
    public const float MetresPerPixel = 0.25f;

    /// <summary>The map of this Landmark's ground and its rectangle (centre X and Z, half size) for the floor shader.</summary>
    public static (ImageTexture Map, Vector4 Rect) Build(Landmark landmark, StoreSpace space, int seed)
    {
        var width = (int)MathF.Ceiling(landmark.Size.X / MetresPerPixel);
        var height = (int)MathF.Ceiling(landmark.Size.Y / MetresPerPixel);
        var half = landmark.Size / 2f;
        // Paths as segments in the Landmark's own frame (x east, y south, origin at its centre).
        var segments = new List<(System.Numerics.Vector2 A, System.Numerics.Vector2 B)>();
        foreach (var path in landmark.Paths)
            for (var i = 0; i + 1 < path.Count; i++)
                segments.Add((space.Delta(landmark.Centre, path[i]), space.Delta(landmark.Centre, path[i + 1])));
        var data = new byte[width * height * 2];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = new System.Numerics.Vector2((x + 0.5f) * MetresPerPixel - half.X, (y + 0.5f) * MetresPerPixel - half.Y);
                var toEdge = MathF.Min(half.X - MathF.Abs(p.X), half.Y - MathF.Abs(p.Y));
                // The lawn's edge wanders by several metres and has a few bare clearings.
                var edge = toEdge + (Noise(p, 14f, seed) - 0.5f) * 14f + (Noise(p, 4f, seed + 1) - 0.5f) * 3f - 3f;
                var lawn = Math.Clamp(edge / 1.5f, 0f, 1f);
                lawn *= 1f - Smooth(0.72f, 0.8f, Noise(p, 9f, seed + 2)) * Smooth(0.5f, 0.7f, Noise(p, 31f, seed + 3));
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    lawn = 0f;
                var distance = float.MaxValue;
                foreach (var (a, b) in segments)
                    distance = MathF.Min(distance, DistanceToSegment(p, a, b));
                // The path's edge is ragged, so it reads as trodden ground.
                var reach = CampingGround.PathWidth / 2f + (Noise(p, 2.5f, seed + 4) - 0.5f) * 0.9f;
                var path = Math.Clamp((reach - distance) / 0.4f + 0.5f, 0f, 1f);
                data[(y * width + x) * 2] = (byte)(lawn * 255f + 0.5f);
                data[(y * width + x) * 2 + 1] = (byte)(path * 255f + 0.5f);
            }
        var image = Image.CreateFromData(width, height, false, Image.Format.Rg8, data);
        return (ImageTexture.CreateFromImage(image), new Vector4(landmark.Centre.X, landmark.Centre.Y, half.X, half.Y));
    }

    private static float Smooth(float low, float high, float x)
    {
        var t = Math.Clamp((x - low) / (high - low), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float DistanceToSegment(System.Numerics.Vector2 p, System.Numerics.Vector2 a, System.Numerics.Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(System.Numerics.Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return (p - (a + ab * t)).Length();
    }

    private static float Hash(int x, int y, int salt)
    {
        var h = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)salt * 83492791u);
        h ^= h >> 16;
        h *= 0x7feb352du;
        h ^= h >> 15;
        h *= 0x846ca68bu;
        h ^= h >> 16;
        return h / 4294967295f;
    }

    /// <summary>Smooth value noise in [0, 1] on a lattice of `scale` metres.</summary>
    private static float Noise(System.Numerics.Vector2 p, float scale, int salt)
    {
        var x = p.X / scale;
        var y = p.Y / scale;
        var ix = (int)MathF.Floor(x);
        var iy = (int)MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var ux = fx * fx * (3f - 2f * fx);
        var uy = fy * fy * (3f - 2f * fy);
        var top = Hash(ix, iy, salt) + (Hash(ix + 1, iy, salt) - Hash(ix, iy, salt)) * ux;
        var bottom = Hash(ix, iy + 1, salt) + (Hash(ix + 1, iy + 1, salt) - Hash(ix, iy + 1, salt)) * ux;
        return top + (bottom - top) * uy;
    }
}
