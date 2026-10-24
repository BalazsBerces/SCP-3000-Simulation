using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Sector inspection on the <see cref="StoreOverview"/>: its Inspect Sectors button turns it on, and a left click then
/// highlights the Sector under the cursor (wrapped, so a click near a Seam picks the Sector actually there) and opens a
/// panel with its grid coordinate, Surplus and Shortage, stock per Goods class, Water points, Departments by type, Pillar,
/// Landmarks and Points of interest. Clicking another Sector switches the panel; the button again turns inspection off.
/// The user argument <c>--inspect=x,y</c> turns it on in the overview and clicks the screen point over Store position (x, y).
/// Reads the snapshot only: it never computes stock.
/// </summary>
public partial class SectorInspector : CanvasLayer
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    private Button _toggle = null!;
    private PanelContainer _panel = null!;
    private Label _info = null!;
    private MeshInstance3D _highlight = null!;
    private Sector? _selected;
    private System.Numerics.Vector2? _devClick;

    public bool Inspecting => _toggle.ButtonPressed;

    public override void _Ready()
    {
        _toggle = new Button
        {
            Name = "InspectSectors", Text = "Inspect Sectors", ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
            Position = new Vector2(16f, 104f), Visible = StoreOverview.Enabled,
            TooltipText = "Click a Sector for its stock, Water and places; press again to stop",
        };
        _toggle.Toggled += _ => Select(null);
        AddChild(_toggle);
        _panel = new PanelContainer { Name = "SectorPanel", Position = new Vector2(16f, 144f), Visible = false };
        _info = new Label { Name = "SectorInfo", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440f, 0f) };
        _panel.AddChild(_info);
        AddChild(_panel);

        _highlight = new MeshInstance3D
        {
            Name = "SectorHighlight", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.2f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        Rig.GetParent().CallDeferred(Node.MethodName.AddChild, _highlight);

        StoreOverview.Changed += OnOverviewChanged;
        Host.WorldCreated += OnWorldCreated;
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--inspect=")) is { } arg)
        {
            var xy = arg["--inspect=".Length..].Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            _devClick = new System.Numerics.Vector2(xy[0], xy[1]);
        }
    }

    public override void _ExitTree()
    {
        StoreOverview.Changed -= OnOverviewChanged;
        Host.WorldCreated -= OnWorldCreated;
    }

    public override void _Process(double delta)
    {
        // A dev click waits for the overview and for the camera to settle over the Store.
        if (_devClick is not { } store || !StoreOverview.Enabled || Rig.Camera.GlobalPosition.Y < 100f)
            return;
        _devClick = null;
        _toggle.ButtonPressed = true;
        Click(Rig.Camera.UnprojectPosition(store.ToWorld()));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Inspecting && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            Click(click.Position);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Selects the Sector under a screen point: where its ray meets the floor, wrapped onto the Store.</summary>
    public void Click(Vector2 screen)
    {
        var from = Rig.Camera.ProjectRayOrigin(screen);
        var direction = Rig.Camera.ProjectRayNormal(screen);
        if (direction.Y >= 0f)
            return;
        var hit = from + direction * (-from.Y / direction.Y);
        Select(Host.Snapshot.SectorAt(hit.ToStore()));
    }

    private void Select(Sector? sector)
    {
        _selected = Inspecting ? sector : null;
        _panel.Visible = _highlight.Visible = _selected is not null;
        if (_selected is not { } s)
            return;
        _highlight.Mesh = new PlaneMesh { Size = new Vector2(s.Size, s.Size) };
        _highlight.Position = (s.Origin + new System.Numerics.Vector2(s.Size / 2f)).ToWorld(0.1f);
        _info.Text = Describe(Host.Snapshot, s);
        GD.Print($"Sector inspected:\n{_info.Text}");
    }

    private void OnOverviewChanged(bool enabled)
    {
        _toggle.Visible = enabled;
        if (!enabled)
            _toggle.ButtonPressed = false;
    }

    // Another Store may have other Sectors; the inspection stays on with the panel closed.
    private void OnWorldCreated(WorldSnapshot snapshot) => Select(null);

    /// <summary>The panel text of a Sector, all read from the snapshot.</summary>
    internal static string Describe(WorldSnapshot snapshot, Sector sector)
    {
        var stock = snapshot.SectorStocks[sector.Id];
        var lines = new List<string>
        {
            $"Sector {sector.Name}",
            $"Surplus: {string.Join(", ", stock.Surplus)}",
            $"Shortage: {string.Join(", ", stock.Shortage)}",
            "Stock: " + string.Join(", ", stock.ByClass.Select(c => $"{c.Key} {c.Value}")),
        };

        var water = snapshot.WaterPoints.Where(w => snapshot.SectorAt(w.Position).Id == sector.Id).ToList();
        lines.Add(water.Count == 0
            ? "Water: none"
            : "Water: " + string.Join(", ", water.GroupBy(w => w.Kind).Select(g =>
                g.Key == WaterPointKind.ShowroomTap ? $"showroom taps {g.Count()} ({g.Count(w => w.Running)} running)" : $"{WaterName(g.Key)} {g.Count()}")));

        // A Department counts in every Sector one of its cells lies in.
        var departments = new HashSet<Department>();
        var cells = snapshot.Departments;
        for (var y = 0; y < cells.CellsPerSide; y++)
            for (var x = 0; x < cells.CellsPerSide; x++)
            {
                var centre = new System.Numerics.Vector2(x + 0.5f, y + 0.5f) * cells.CellSize;
                if (snapshot.SectorAt(centre).Id == sector.Id)
                    departments.Add(cells.AtCell(x, y));
            }
        lines.Add("Departments: " + Counted(departments.Select(d => DepartmentNames.Of(d.Type))));

        var pillar = snapshot.Pillars.FirstOrDefault(p => p.Coordinate == sector.Name);
        lines.Add(pillar is null ? "Pillar: none" : $"Pillar {pillar.Coordinate}, {pillar.Width:0} m wide");
        var landmarks = snapshot.Landmarks.Where(l => l.SectorId == sector.Id).Select(l => LandmarkNames.Of(l.Type));
        lines.Add("Landmark: " + Counted(landmarks));
        var pois = snapshot.PointsOfInterest.Where(p => p.SectorId == sector.Id).Select(p => PointOfInterestNames.Of(p.Type));
        lines.Add("Points of interest: " + Counted(pois));
        return string.Join("\n", lines);
    }

    // "Showrooms ×10, Outdoor ×2, Workspaces", most first; "none" if empty.
    private static string Counted(IEnumerable<string> names)
    {
        var groups = names.GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key).ToList();
        return groups.Count == 0 ? "none" : string.Join(", ", groups);
    }

    private static string WaterName(WaterPointKind kind) => kind switch
    {
        WaterPointKind.RestroomWashbasin => "washbasins",
        WaterPointKind.FountainCourt => "fountain court",
        _ => "camping lake",
    };
}
