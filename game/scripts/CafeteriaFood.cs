using Godot;

namespace Scp3008.Game;

/// <summary>
/// Modelled, photoreal PBR food items for the Cafeteria's serving counters (ticket 08n):
/// meatballs in sauce, mashed potatoes, lingonberry jam, whole almond cake and sliced almond cake on plates.
/// Fitted into the counter wells and onto the shelves in presentation only.
/// </summary>
public static class CafeteriaFood
{
    public sealed class FoodMesh
    {
        public Vector3[] FullPositions { get; }
        public Vector3[] FullNormals { get; }
        public Vector2[] FullUvs { get; }
        public int[] FullIndices { get; }

        public Vector3[] CoarsePositions { get; }
        public Vector3[] CoarseNormals { get; }
        public Vector2[] CoarseUvs { get; }
        public int[] CoarseIndices { get; }

        public FoodMesh(
            Vector3[] fullPositions, Vector3[] fullNormals, Vector2[] fullUvs, int[] fullIndices,
            Vector3[] coarsePositions, Vector3[] coarseNormals, Vector2[] coarseUvs, int[] coarseIndices)
        {
            FullPositions = fullPositions;
            FullNormals = fullNormals;
            FullUvs = fullUvs;
            FullIndices = fullIndices;
            CoarsePositions = coarsePositions;
            CoarseNormals = coarseNormals;
            CoarseUvs = coarseUvs;
            CoarseIndices = coarseIndices;
        }
    }

    private static FoodMesh? _meatballs;
    private static FoodMesh? _sauce;
    private static FoodMesh? _mash;
    private static FoodMesh? _lingonberries;
    private static FoodMesh? _wholeCake;
    private static FoodMesh? _wholeCakePlate;
    private static FoodMesh? _cakeSlice;
    private static FoodMesh? _cakeSlicePlate;

    public static FoodMesh Meatballs { get { EnsureLoaded(); return _meatballs!; } }
    public static FoodMesh Sauce { get { EnsureLoaded(); return _sauce!; } }
    public static FoodMesh Mash { get { EnsureLoaded(); return _mash!; } }
    public static FoodMesh Lingonberries { get { EnsureLoaded(); return _lingonberries!; } }
    public static FoodMesh WholeCake { get { EnsureLoaded(); return _wholeCake!; } }
    public static FoodMesh WholeCakePlate { get { EnsureLoaded(); return _wholeCakePlate!; } }
    public static FoodMesh CakeSlice { get { EnsureLoaded(); return _cakeSlice!; } }
    public static FoodMesh CakeSlicePlate { get { EnsureLoaded(); return _cakeSlicePlate!; } }

    private static void EnsureLoaded()
    {
        if (_meatballs is not null)
            return;

        (_meatballs, _sauce) = LoadPair("meatballs", "Meatballs", "Sauce");
        _mash = LoadSingle("mash", "Mash");
        _lingonberries = LoadSingle("lingonberries", "Lingonberries");
        (_wholeCake, _wholeCakePlate) = LoadPair("whole_cake", "WholeCake", "Plate");
        (_cakeSlice, _cakeSlicePlate) = LoadPair("cake_slice", "CakeSlice", "Plate");
    }

    private static FoodMesh LoadSingle(string folder, string name)
    {
        var scene = GD.Load<PackedScene>($"res://assets/models/{folder}/{folder}.gltf").Instantiate<Node3D>();
        var mesh = Extract(scene, name);
        scene.Free();
        return mesh;
    }

    private static (FoodMesh, FoodMesh) LoadPair(string folder, string name1, string name2)
    {
        var scene = GD.Load<PackedScene>($"res://assets/models/{folder}/{folder}.gltf").Instantiate<Node3D>();
        var mesh1 = Extract(scene, name1);
        var mesh2 = Extract(scene, name2);
        scene.Free();
        return (mesh1, mesh2);
    }

    private static FoodMesh Extract(Node3D scene, string name)
    {
        var fullNode = FindMeshInstance(scene, name)
            ?? throw new System.InvalidOperationException($"Node {name} not found in scene");
        var coarseNode = FindMeshInstance(scene, $"{name}_Coarse")
            ?? throw new System.InvalidOperationException($"Node {name}_Coarse not found in scene");

        var (fPos, fNorm, fUv, fIdx) = GetArrays(fullNode.Mesh);
        var (cPos, cNorm, cUv, cIdx) = GetArrays(coarseNode.Mesh);
        return new FoodMesh(fPos, fNorm, fUv, fIdx, cPos, cNorm, cUv, cIdx);
    }

    private static MeshInstance3D? FindMeshInstance(Node node, string name)
    {
        if (node is MeshInstance3D mi && mi.Name.ToString().Equals(name, System.StringComparison.OrdinalIgnoreCase))
            return mi;
        foreach (var child in node.GetChildren())
        {
            if (FindMeshInstance(child, name) is { } found)
                return found;
        }
        return null;
    }

    private static (Vector3[], Vector3[], Vector2[], int[]) GetArrays(Mesh mesh)
    {
        var arrays = mesh.SurfaceGetArrays(0);
        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (indices.Length == 0)
            indices = [.. System.Linq.Enumerable.Range(0, positions.Length)];
        return (positions, normals, uvs, indices);
    }
}
