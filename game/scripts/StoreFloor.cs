using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The flat Store floor. It follows the camera focus out to the camera's far plane, and its
/// shader gives each Department its own floor texture from a map of Department types, varied so its repeat does not show, with
/// patterns that repeat exactly once per Store length, so the floor has no edge and no visible
/// seam however far the camera pans. The map is rebuilt when a new world is created.
/// </summary>
public partial class StoreFloor : MeshInstance3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    /// <summary>Metres per texture repeat, per Department type; each divides every Store length (1344 and 1920 m).</summary>
    private static readonly float[] LayerMetres = [3.0f, 2.0f, 2.4f, 3.0f, 2.0f, 2.4f, 2.0f, 2.0f, 2.4f, 3f, 2f];

    /// <summary>Albedo balance per Department type (food market, showrooms, lighting, warehouse, bath/kitchen, plants, kids).</summary>
    private static readonly Color[] LayerTints =
    [
        new(1.25f, 1.2f, 1.1f), new(1f, 1f, 1f), new(0.8f, 0.8f, 0.85f), new(1.05f, 1.05f, 1.05f),
        new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f),
        new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f),
    ];

    /// <summary>
    /// How each Department type's floor breaks its repeat (see store_floor.gdshader): 1 randomly
    /// offset copies for the jointless warehouse concrete and office carpet, 2 per-tile variation
    /// for the large regular tiles of the food market and lighting floors, 0 large-scale shade and
    /// wear only for the rest (planks, staggered or tiny tiles), where neither fits.
    /// </summary>
    private static readonly int[] LayerModes = [1, 0, 2, 1, 0, 0, 0, 1, 0, 0, 0];

    /// <summary>The atlas layer each Department type's floor reads: the food market's chequerboard (layer 0) is replaced by the jointless concrete.</summary>
    private static readonly int[] LayerSources = [3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>How far each floor's albedo fades towards its average: the Workspaces carpet's stripes flicker and smear under FSR2.</summary>
    private static readonly float[] LayerFlatten = [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.6f, 0f, 0f, 0f];

    /// <summary>For per-tile layers: tiles per repeat and the grid's offset in repeat units, read off the textures.</summary>
    private static readonly Vector3[] LayerGrids =
    [
        new(4f, 0f, 0.18f), default, new(5f, 0f, 0f), default, default, default,
        default, default, default, default, default,
    ];

    private PlaneMesh _followingPlane = null!;

    public override void _Ready()
    {
        var reach = Rig.Camera.Far;
        Mesh = _followingPlane = new PlaneMesh { Size = new Vector2(2f * reach, 2f * reach) };
        StoreOverview.Changed += OnOverviewChanged;
        FloorMaterial.SetShaderParameter("floor_albedo", GD.Load<TextureLayered>("res://assets/textures/floor_albedo.jpg"));
        FloorMaterial.SetShaderParameter("floor_normal", GD.Load<TextureLayered>("res://assets/textures/floor_normal.jpg"));
        FloorMaterial.SetShaderParameter("floor_roughness", GD.Load<TextureLayered>("res://assets/textures/floor_roughness.jpg"));
        FloorMaterial.SetShaderParameter("layer_metres", LayerMetres);
        FloorMaterial.SetShaderParameter("layer_tint", LayerTints.Select(c => new Vector3(c.R, c.G, c.B)).ToArray());
        FloorMaterial.SetShaderParameter("layer_mode", LayerModes);
        FloorMaterial.SetShaderParameter("layer_grid", LayerGrids);
        FloorMaterial.SetShaderParameter("layer_source", LayerSources);
        FloorMaterial.SetShaderParameter("layer_flatten", LayerFlatten);
        FloorMaterial.SetShaderParameter("grass_albedo", GD.Load<Texture2D>("res://assets/textures/grass_albedo.jpg"));
        FloorMaterial.SetShaderParameter("grass_normal", GD.Load<Texture2D>("res://assets/textures/grass_normal.jpg"));
        FloorMaterial.SetShaderParameter("grass_roughness", GD.Load<Texture2D>("res://assets/textures/grass_roughness.jpg"));
        ShowDepartments(Host.Snapshot);
        Host.WorldCreated += ShowDepartments;
        FollowFocus();
    }

    public override void _ExitTree()
    {
        Host.WorldCreated -= ShowDepartments;
        StoreOverview.Changed -= OnOverviewChanged;
    }

    public override void _Process(double delta)
    {
        if (!StoreOverview.Enabled)
            FollowFocus();
    }

    /// <summary>
    /// In the overview the floor covers the Store exactly once, so its outline shows its true size,
    /// and it marks the Sector boundaries, which the normal view never shows.
    /// </summary>
    private void OnOverviewChanged(bool enabled)
    {
        FloorMaterial.SetShaderParameter("show_sectors", enabled);
        var size = Host.Snapshot.Space.Size;
        Mesh = enabled ? new PlaneMesh { Size = new Vector2(size, size) } : _followingPlane;
        if (enabled)
            Position = new Vector3(size / 2f, 0f, size / 2f);
        else
            FollowFocus();
    }

    /// <summary>The floor material, shared with the wrap debug overlay.</summary>
    public ShaderMaterial FloorMaterial => (ShaderMaterial)MaterialOverride;

    private void ShowDepartments(WorldSnapshot snapshot)
    {
        // The Store size and the overview's Sector boundaries follow the world's Pillar layout.
        FloorMaterial.SetShaderParameter("store_size", snapshot.Space.Size);
        FloorMaterial.SetShaderParameter("world_seed", snapshot.Seed);
        FloorMaterial.SetShaderParameter("chunk_size", snapshot.Space.ChunkSize);
        FloorMaterial.SetShaderParameter("sector_size", snapshot.Sectors[0].Size);
        if (StoreOverview.Enabled)
            OnOverviewChanged(true);
        var map = snapshot.Departments;
        var types = new byte[map.CellsPerSide * map.CellsPerSide];
        for (var y = 0; y < map.CellsPerSide; y++)
            for (var x = 0; x < map.CellsPerSide; x++)
                types[y * map.CellsPerSide + x] = (byte)map.AtCell(x, y).Type;
        var image = Image.CreateFromData(map.CellsPerSide, map.CellsPerSide, false, Image.Format.R8, types);
        FloorMaterial.SetShaderParameter("department_map", ImageTexture.CreateFromImage(image));
        FloorMaterial.SetShaderParameter("department_cell_size", map.CellSize);
        // The Camping ground's turf and paths; none (zero rectangle) in a world without one.
        var camp = snapshot.Landmarks.FirstOrDefault(l => l.Type == LandmarkType.CampingGround);
        if (camp is null)
        {
            FloorMaterial.SetShaderParameter("camp_rect", Vector4.Zero);
            return;
        }
        var (turf, rect) = CampTurf.Build(camp, snapshot.Space, snapshot.Furniture);
        FloorMaterial.SetShaderParameter("camp_map", turf);
        FloorMaterial.SetShaderParameter("camp_rect", rect);
    }

    private void FollowFocus() => Position = Rig.Focus with { Y = 0f };
}
