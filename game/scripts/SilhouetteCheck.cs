using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Compares every Furniture type and variant's full mesh with its coarse mesh (level of detail):
/// both are drawn as flat silhouettes from the front, the side and above on a 5 mm grid (2 cm for
/// the external models, <see cref="ModelCell"/>), and the
/// coarse mesh must cover nearly all of the full one, reach the floor where the full one does and
/// start at the same height (a coarse piece that lost its legs floats). Run by DevCapture's
/// <c>--silhouette-check</c>.
/// </summary>
public static class SilhouetteCheck
{
    private const float Cell = 0.005f;

    // The external models' grid: their coarse mesh is drawn only from the switch zoom out (160 m),
    // where a pixel spans about 20 cm.
    private const float ModelCell = 0.02f;

    // Share of the full silhouette the coarse one must cover in each view. Below 1 because thin
    // round parts become boxes and invisible details (handles, trim, tape) may go.
    private const float MinViewCoverage = 0.9f;

    // Share of the full mesh's floor contact (its bottom 2 cm, seen from the front and the side)
    // the coarse mesh must keep: legs, poles and plinths stay.
    private const float MinFloorCoverage = 0.9f;
    private const float FloorBand = 0.02f;

    public sealed record Result(FurnitureType Type, int Variant, float Front, float Side, float Top,
        float Floor, float FullBase, float CoarseBase, int FullTriangles, int CoarseTriangles)
    {
        public bool Passed => Front >= MinViewCoverage && Side >= MinViewCoverage && Top >= MinViewCoverage
                              && Floor >= MinFloorCoverage && CoarseBase - FullBase < 0.001f;

        public override string ToString() =>
            $"{(Passed ? "ok  " : "FAIL")} {Type}-{Variant}: coverage front {Front:P1}, side {Side:P1}, top {Top:P1}, " +
            $"floor contact {Floor:P1}, base {FullBase:0.000}/{CoarseBase:0.000} m, triangles {FullTriangles}/{CoarseTriangles}";
    }

    public static IEnumerable<Result> All()
    {
        foreach (var type in Enum.GetValues<FurnitureType>())
            for (var variant = 0; variant < FurnitureCatalog.VariantCount(type); variant++)
                yield return Compare(type, variant);
    }

    public static Result Compare(FurnitureType type, int variant) =>
        Compare(type, variant, Triangles(FurnitureMeshes.For(type, variant)), Triangles(FurnitureMeshes.CoarseFor(type, variant)),
            FurnitureModels.Find(type, variant) is null ? Cell : ModelCell);

    private static Result Compare(FurnitureType type, int variant, List<Vector3> full, List<Vector3> coarse, float cell)
    {
        var bounds = new Aabb(full[0], Vector3.Zero);
        foreach (var p in full)
            bounds = bounds.Expand(p);
        bounds = bounds.Grow(cell);
        float Coverage(int u, int v, float? maxV = null)
        {
            var a = Raster(full, bounds, u, v, maxV, cell);
            var b = Raster(coarse, bounds, u, v, maxV, cell);
            var (count, kept) = (0, 0);
            for (var i = 0; i < a.Length; i++)
                if (a[i])
                {
                    count++;
                    if (b[i])
                        kept++;
                }
            return count == 0 ? 1f : (float)kept / count;
        }
        var floorTop = full.Min(p => p.Y) + FloorBand;
        return new Result(type, variant,
            Coverage(0, 1), Coverage(2, 1), Coverage(0, 2),
            Mathf.Min(Coverage(0, 1, floorTop), Coverage(2, 1, floorTop)),
            full.Min(p => p.Y), coarse.Count == 0 ? float.PositiveInfinity : coarse.Min(p => p.Y),
            full.Count / 3, coarse.Count / 3);
    }

    private static List<Vector3> Triangles(ArrayMesh mesh)
    {
        var result = new List<Vector3>();
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            // Transparent glass is deliberately absent from coarse meshes and has no opaque silhouette.
            if (mesh.SurfaceGetMaterial(s) is ShaderMaterial { Shader.ResourcePath: "res://shaders/furniture_glass.gdshader" }) continue;
            var arrays = mesh.SurfaceGetArrays(s);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length > 0)
                result.AddRange(indices.Select(i => vertices[i]));
            else
                result.AddRange(vertices);
        }
        return result;
    }

    /// <summary>The cells whose centres lie inside a triangle, projected onto axes u and v (0 = X, 1 = Y, 2 = Z).</summary>
    private static bool[] Raster(List<Vector3> triangles, Aabb bounds, int u, int v, float? maxV, float cell)
    {
        var (u0, v0) = (bounds.Position[u], bounds.Position[v]);
        var (nu, nv) = ((int)Mathf.Ceil(bounds.Size[u] / cell), (int)Mathf.Ceil(bounds.Size[v] / cell));
        var cells = new bool[nu * nv];
        for (var t = 0; t + 2 < triangles.Count; t += 3)
        {
            var a = new Vector2(triangles[t][u] - u0, triangles[t][v] - v0) / cell;
            var b = new Vector2(triangles[t + 1][u] - u0, triangles[t + 1][v] - v0) / cell;
            var c = new Vector2(triangles[t + 2][u] - u0, triangles[t + 2][v] - v0) / cell;
            var area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 1e-6f)
                continue;
            var (minX, maxX) = (Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.X, Mathf.Min(b.X, c.X)))), Math.Min(nu - 1, (int)Mathf.Ceil(Mathf.Max(a.X, Mathf.Max(b.X, c.X)))));
            var (minY, maxY) = (Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)))), Math.Min(nv - 1, (int)Mathf.Ceil(Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)))));
            if (maxV is { } top)
                maxY = Math.Min(maxY, (int)((top - v0) / cell));
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var (w0, w1, w2) = (Cross(b - a, p - a), Cross(c - b, p - b), Cross(a - c, p - c));
                    if (area > 0 ? w0 >= 0 && w1 >= 0 && w2 >= 0 : w0 <= 0 && w1 <= 0 && w2 <= 0)
                        cells[y * nu + x] = true;
                }
        }
        if (maxV is { } limit)
            for (var y = Math.Max(0, (int)((limit - v0) / cell) + 1); y < nv; y++)
                Array.Fill(cells, false, y * nu, nu);
        return cells;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
