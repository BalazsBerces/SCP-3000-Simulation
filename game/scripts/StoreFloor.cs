using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The flat Store floor. It follows the camera focus out to the camera's far plane, and its
/// shader gives each Department its own floor texture from a map of Department types, with
/// patterns that repeat exactly once per Store length, so the floor has no edge and no visible
/// seam however far the camera pans. The map is rebuilt when a new world is created.
/// </summary>
public partial class StoreFloor : MeshInstance3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    /// <summary>Metres per texture repeat, per Department type; each divides the 1536 m Store length.</summary>
    private static readonly float[] LayerMetres = [3.0f, 2.0f, 2.4f, 3.0f, 2.0f, 2.4f, 2.0f];

    /// <summary>Albedo balance per Department type (food market, showrooms, lighting, warehouse, bath/kitchen, plants, kids).</summary>
    private static readonly Color[] LayerTints =
    [
        new(1f, 1f, 1f), new(1f, 1f, 1f), new(0.8f, 0.8f, 0.85f), new(1.05f, 1.05f, 1.05f),
        new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f),
    ];

    public override void _Ready()
    {
        var reach = Rig.Camera.Far;
        Mesh = new PlaneMesh { Size = new Vector2(2f * reach, 2f * reach) };
        var space = Host.Snapshot.Space;
        FloorMaterial.SetShaderParameter("store_size", space.Size);
        FloorMaterial.SetShaderParameter("chunk_size", space.ChunkSize);
        FloorMaterial.SetShaderParameter("floor_albedo", GD.Load<TextureLayered>("res://assets/textures/floor_albedo.jpg"));
        FloorMaterial.SetShaderParameter("floor_normal", GD.Load<TextureLayered>("res://assets/textures/floor_normal.jpg"));
        FloorMaterial.SetShaderParameter("floor_roughness", GD.Load<TextureLayered>("res://assets/textures/floor_roughness.jpg"));
        FloorMaterial.SetShaderParameter("layer_metres", LayerMetres);
        FloorMaterial.SetShaderParameter("layer_tint", LayerTints.Select(c => new Vector3(c.R, c.G, c.B)).ToArray());
        ShowDepartments(Host.Snapshot);
        Host.WorldCreated += ShowDepartments;
        FollowFocus();
    }

    public override void _ExitTree() => Host.WorldCreated -= ShowDepartments;

    public override void _Process(double delta) => FollowFocus();

    /// <summary>The floor material, shared with the wrap debug overlay.</summary>
    public ShaderMaterial FloorMaterial => (ShaderMaterial)MaterialOverride;

    private void ShowDepartments(WorldSnapshot snapshot)
    {
        var map = snapshot.Departments;
        var types = new byte[map.CellsPerSide * map.CellsPerSide];
        for (var y = 0; y < map.CellsPerSide; y++)
            for (var x = 0; x < map.CellsPerSide; x++)
                types[y * map.CellsPerSide + x] = (byte)map.AtCell(x, y).Type;
        var image = Image.CreateFromData(map.CellsPerSide, map.CellsPerSide, false, Image.Format.R8, types);
        FloorMaterial.SetShaderParameter("department_map", ImageTexture.CreateFromImage(image));
        FloorMaterial.SetShaderParameter("department_cell_size", map.CellSize);
    }

    private void FollowFocus() => Position = Rig.Focus with { Y = 0f };
}
