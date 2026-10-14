using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The far-visible Pillars of the active Pillar layout and all their Ghost copies within reach
/// (the same Pillar may show in two directions), plus the Sector labels of the <see cref="StoreOverview"/>. Reads the snapshot only.
/// Pillars are always solid. In the overview they are cut down to their bases, so the whole Store
/// stays readable from above. When a new world puts a Pillar where the camera focus is, the focus
/// is moved just outside it.
/// </summary>
public partial class PillarRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    // Landmarks extend beyond Furniture culling. Keep placement conservative at maximum zoom.
    [Export] public float FarDistance { get; set; } = 1200f;

    /// <summary>Height of the truncated Pillar bases in the overview: enough for the lowest painted label.</summary>
    [Export] public float OverviewBaseHeight { get; set; } = 16f;

    /// <summary>Height of the shadow-casting proxy at each Pillar's foot.</summary>
    [Export] public float ShadowHeight { get; set; } = 6f;

    private WrappedInstances _instances = null!;
    private WrappedInstances _shadows = null!;
    private ShaderMaterial _material = null!;
    private Node3D _sectorLabels = null!;
    private WorldSnapshot _shown = null!;

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pillar.gdshader") };
        _instances = new WrappedInstances
        {
            Rig = Rig,
            FarDistance = FarDistance,
            ViewReach = FarDistance + Rig.MaxDistance + Host.Snapshot.Space.ChunkSize,
            Mesh = new BoxMesh { Size = Vector3.One, Material = _material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_instances);
        // The Store lights hang far below a Pillar's top, so a Pillar's shadow is a short band at its
        // foot, cast by a low proxy, not one as long as the 1200 m column would throw.
        _shadows = new WrappedInstances
        {
            Rig = Rig,
            FarDistance = FarDistance,
            ViewReach = _instances.ViewReach,
            Mesh = new BoxMesh { Size = Vector3.One },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
        };
        AddChild(_shadows);
        AddChild(_sectorLabels = new Node3D { Name = "SectorLabels" });
        Host.WorldCreated += OnWorldCreated;
        StoreOverview.Changed += OnOverviewChanged;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree()
    {
        Host.WorldCreated -= OnWorldCreated;
        StoreOverview.Changed -= OnOverviewChanged;
    }

    private void OnOverviewChanged(bool _) => Rebuild(_shown);

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        var focus = Rig.Focus.ToStore();
        var outside = snapshot.OutsidePillars(focus);
        if (outside != focus)
        {
            Rig.JumpTo(outside.ToWorld());
            GD.Print($"Camera focus moved out of Pillar {snapshot.NearestPillar(focus).Coordinate} to ({outside.X:0.0}, {outside.Y:0.0}).");
        }
        Rebuild(snapshot);
    }

    private void Rebuild(WorldSnapshot snapshot)
    {
        _shown = snapshot;
        var overview = StoreOverview.Enabled;
        var pillars = snapshot.Pillars;
        _material.SetShaderParameter("labels", LabelAtlas(pillars.Select(p => p.Coordinate).ToArray()));
        _material.SetShaderParameter("label_count", (float)pillars.Count);
        _instances.SetInstances(snapshot.Space, pillars.Select(p =>
        {
            var height = overview ? OverviewBaseHeight : p.Height;
            return (new Transform3D(Basis.Identity.Scaled(new Vector3(p.Width, height, p.Width)), p.Position.ToWorld(height / 2f)),
                new Color(pillars.Count > 1 ? p.Id / (pillars.Count - 1f) : 0f, 0f, 0f));
        }));
        _shadows.SetInstances(snapshot.Space, pillars.Select(p =>
            new Transform3D(Basis.Identity.Scaled(new Vector3(p.Width, ShadowHeight, p.Width)), p.Position.ToWorld(ShadowHeight / 2f))));
        ShowSectorLabels(snapshot, overview);
        GD.Print($"Pillars: {snapshot.PillarLayout.Name()} layout, {pillars.Count} solid concrete Landmarks {(overview ? "cut to their bases" : $"{pillars[0].Height:0} m tall")}, " +
                 $"{snapshot.Sectors.Count} Sectors, no emission or lights, reach {FarDistance:0} m.");
    }

    /// <summary>Sector names float beside each Pillar base in the overview only; the normal view has no boundary markings.</summary>
    private void ShowSectorLabels(WorldSnapshot snapshot, bool overview)
    {
        foreach (var child in _sectorLabels.GetChildren())
            child.QueueFree();
        _sectorLabels.Visible = overview;
        if (!overview)
            return;
        foreach (var sector in snapshot.Sectors)
        {
            var label = new Label3D
            {
                Text = $"Sector {sector.Name}",
                FontSize = 96,
                PixelSize = 0.5f,
                OutlineSize = 24,
                OutlineModulate = Colors.Black,
                Modulate = new Color(1f, 0.85f, 0.35f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Shaded = false,
            };
            _sectorLabels.AddChild(label);
            label.Position = (sector.Centre + new System.Numerics.Vector2(0f, sector.Size * 0.22f)).ToWorld(20f);
        }
    }

    // Stencil lettering for the grid coordinates, baked once into a shared atlas: one 128 x 64
    // cell per Pillar, three 5 x 7 glyphs each, rather than dozens of labels.
    private static ImageTexture LabelAtlas(IReadOnlyList<string> texts)
    {
        const int cellWidth = 128, cellHeight = 64, unit = 6;
        var image = Image.CreateEmpty(cellWidth * texts.Count, cellHeight, false, Image.Format.Rgba8);
        for (var cell = 0; cell < texts.Count; cell++)
        {
            var text = texts[cell];
            var width = text.Length * 6 - 1;
            var left = cell * cellWidth + (cellWidth - width * unit) / 2;
            var top = (cellHeight - 7 * unit) / 2;
            for (var c = 0; c < text.Length; c++)
            {
                var rows = Glyphs[text[c]];
                for (var y = 0; y < 7; y++)
                    for (var x = 0; x < 5; x++)
                        if (rows[y][x] == '#')
                            image.FillRect(new Rect2I(left + (c * 6 + x) * unit, top + y * unit, unit, unit), Colors.White);
            }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
        ['1'] = ["..#..", ".##..", "#.#..", "..#..", "..#..", "..#..", "#####"],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['-'] = [".....", ".....", ".....", ".###.", ".....", ".....", "....."],
    };
}
