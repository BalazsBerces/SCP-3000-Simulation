using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Observer seed input: type a seed (a number, or any text, which is hashed to one) and press
/// Generate or Enter for a newly generated Store; Random picks a seed. Below, the Pillar layout
/// row (Pillars: 3×3 | 2×2 | 3×3 large | Corners | Diamond) marks the active layout; choosing another regenerates the
/// Store with the same seed and that layout, like Generate. The Landmark showcase button switches between the
/// Store and the Landmark showcase (every Landmark in a row, no Displays) with the same seed and layout; in the
/// showcase a button per Landmark flies the camera to it. Also names the Department under the camera focus.
/// </summary>
public partial class SeedPanel : CanvasLayer
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    private LineEdit _seedInput = null!;
    private Button _generate = null!;
    private Button _random = null!;
    private Label _status = null!;
    private readonly Dictionary<PillarLayout, Button> _layoutButtons = new();
    private Button _showcase = null!;
    private HFlowContainer _landmarkButtons = null!;

    /// <summary>Zoom distance the camera flies to per metre of a Landmark's larger side, so it fills the view.</summary>
    private const float ShowcaseDistancePerMetre = 1.1f;

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f, OffsetLeft = -420f, OffsetRight = -16f, OffsetTop = 16f,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        var row = new HBoxContainer();
        column.AddChild(row);

        row.AddChild(new Label { Text = "Seed" });
        _seedInput = new LineEdit
        {
            Name = "SeedInput", Text = Host.Seed.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "number or text",
        };
        _seedInput.TextSubmitted += _ => Generate();
        row.AddChild(_seedInput);
        _generate = new Button { Name = "Generate", Text = "Generate", FocusMode = Control.FocusModeEnum.None };
        _generate.Pressed += Generate;
        row.AddChild(_generate);
        _random = new Button { Name = "Random", Text = "Random", FocusMode = Control.FocusModeEnum.None };
        _random.Pressed += () =>
        {
            _seedInput.Text = Random.Shared.Next().ToString();
            Generate();
        };
        row.AddChild(_random);

        var layoutRow = new HBoxContainer { Name = "PillarLayout" };
        column.AddChild(layoutRow);
        layoutRow.AddChild(new Label { Text = "Pillars:" });
        var layoutGroup = new ButtonGroup();
        foreach (var layout in PillarLayouts.All)
        {
            var perSide = layout.SectorsPerSide();
            var button = new Button
            {
                Name = layout.Name(), Text = LayoutLabel(layout), ToggleMode = true, ButtonGroup = layoutGroup,
                ButtonPressed = layout == Host.Layout, FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"{layout.PillarCount()} Pillars, {perSide * perSide} Sectors of {layout.SectorSize():0} m, {layout.Space().Size:0} m Store",
            };
            button.Pressed += () => ChooseLayout(layout);
            layoutRow.AddChild(button);
            _layoutButtons[layout] = button;
        }

        _showcase = new Button
        {
            Name = "LandmarkShowcase", Text = "Landmark showcase", ToggleMode = true, ButtonPressed = Host.LandmarkShowcase,
            FocusMode = Control.FocusModeEnum.None, TooltipText = "Every Landmark side by side, with no Displays; press again for the Store",
        };
        _showcase.Toggled += ChooseShowcase;
        column.AddChild(_showcase);
        _landmarkButtons = new HFlowContainer { Name = "Landmarks" };
        column.AddChild(_landmarkButtons);
        ShowLandmarkButtons(Host.Snapshot);
        // After the camera has set its start view.
        Callable.From(FlyToFirstLandmark).CallDeferred();

        _status = new Label { Name = "Department" };
        column.AddChild(_status);
        Host.WorldCreated += OnWorldCreated;
    }

    public override void _ExitTree() => Host.WorldCreated -= OnWorldCreated;

    public override void _Process(double delta)
    {
        if (Host.IsGenerating)
        {
            _status.Text = $"Generating Store {Host.Seed} ({Host.Layout.Name()} Pillars)…";
            return;
        }
        var department = Host.Snapshot.Departments.At(Rig.Focus.ToStore());
        _status.Text = $"Store {Host.Snapshot.Seed}    Department: {DepartmentNames.Of(department.Type)}";
    }

    /// <summary>Turns seed text into a seed: numbers are used as they are, other text is hashed (FNV-1a), so it is stable.</summary>
    public static int ParseSeed(string text)
    {
        text = text.Trim();
        if (int.TryParse(text, out var number))
            return number;
        var hash = 2166136261u;
        foreach (var c in text)
            hash = (hash ^ c) * 16777619u;
        return unchecked((int)hash);
    }

    /// <summary>Enters a seed as if typed and submitted (used by unattended dev checks).</summary>
    public void Submit(string text)
    {
        _seedInput.Text = text;
        Generate();
    }

    /// <summary>
    /// Regenerates the Store with the current seed and another Pillar layout, as the layout row
    /// does (also used by unattended dev checks). Choosing the active layout does nothing.
    /// </summary>
    public void ChooseLayout(PillarLayout layout)
    {
        if (Host.IsGenerating || layout == Host.Snapshot.PillarLayout)
        {
            MarkLayout(Host.IsGenerating ? Host.Layout : Host.Snapshot.PillarLayout);
            return;
        }
        Host.CreateWorld(Host.Snapshot.Seed, layout);
        MarkLayout(layout);
    }

    /// <summary>Switches between the Store and the Landmark showcase, keeping the seed and the Pillar layout.</summary>
    public void ChooseShowcase(bool showcase)
    {
        if (Host.IsGenerating || showcase == Host.Snapshot.LandmarkShowcase)
        {
            _showcase.SetPressedNoSignal(Host.IsGenerating ? Host.LandmarkShowcase : Host.Snapshot.LandmarkShowcase);
            return;
        }
        Host.CreateWorld(Host.Snapshot.Seed, landmarkShowcase: showcase);
    }

    /// <summary>In the showcase, a button per Landmark, west to east, that flies the camera to it.</summary>
    private void ShowLandmarkButtons(WorldSnapshot snapshot)
    {
        foreach (var child in _landmarkButtons.GetChildren())
            child.QueueFree();
        _landmarkButtons.Visible = snapshot.LandmarkShowcase;
        if (!snapshot.LandmarkShowcase)
            return;
        foreach (var landmark in snapshot.Landmarks.OrderBy(l => l.Centre.X))
        {
            var button = new Button { Text = LandmarkNames.Of(landmark.Type), FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Rig.FlyTo(landmark.Centre, MathF.Max(landmark.Size.X, landmark.Size.Y) * ShowcaseDistancePerMetre);
            _landmarkButtons.AddChild(button);
        }
    }

    private static string LayoutLabel(PillarLayout layout) => layout switch
    {
        PillarLayout.ThreeByThreeLarge => "3×3 large",
        PillarLayout.CornersLarge => "Corners",
        PillarLayout.DiamondLarge => "Diamond",
        _ => $"{layout.SectorsPerSide()}×{layout.SectorsPerSide()}",
    };

    private void MarkLayout(PillarLayout active)
    {
        foreach (var (layout, button) in _layoutButtons)
            button.SetPressedNoSignal(layout == active);
    }

    private void Generate()
    {
        _seedInput.ReleaseFocus();
        Host.CreateWorld(ParseSeed(_seedInput.Text));
    }

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        if (!_seedInput.HasFocus())
            _seedInput.Text = snapshot.Seed.ToString();
        MarkLayout(snapshot.PillarLayout);
        _showcase.SetPressedNoSignal(snapshot.LandmarkShowcase);
        ShowLandmarkButtons(snapshot);
        FlyToFirstLandmark();
    }

    /// <summary>The showcase starts at its first (westmost) Landmark.</summary>
    private void FlyToFirstLandmark()
    {
        var snapshot = Host.Snapshot;
        if (snapshot.LandmarkShowcase && snapshot.Landmarks.MinBy(l => l.Centre.X) is { } first)
            Rig.FlyTo(first.Centre, MathF.Max(first.Size.X, first.Size.Y) * ShowcaseDistancePerMetre);
    }
}

/// <summary>Display names of the Landmark types.</summary>
public static class LandmarkNames
{
    public static string Of(LandmarkType type) => type switch
    {
        LandmarkType.RuinedVillage => "Ruined Village",
        LandmarkType.RackCanyon => "Rack canyon",
        LandmarkType.BoxFort => "Box fort",
        LandmarkType.MattressMountain => "Mattress mountain",
        LandmarkType.DeadZone => "Dead zone",
        LandmarkType.ClockWall => "Clock wall",
        LandmarkType.FountainCourt => "Fountain court",
        _ => type.ToString(),
    };
}

/// <summary>Display names of the Department types.</summary>
public static class DepartmentNames
{
    public static string Of(DepartmentType type) => type switch
    {
        DepartmentType.RestaurantAndFoodMarket => "Restaurant & food market",
        DepartmentType.Showrooms => "Showrooms",
        DepartmentType.LightingAndElectrical => "Lighting & electrical",
        DepartmentType.SelfServeWarehouse => "Self-serve warehouse",
        DepartmentType.BathroomsAndKitchens => "Bathrooms & kitchens",
        DepartmentType.PlantSection => "Plant section",
        DepartmentType.Workspaces => "Workspaces",
        DepartmentType.TextilesAndDecor => "Textiles & Decor",
        DepartmentType.Outdoor => "Outdoor",
        DepartmentType.DiningAndHallway => "Dining & Hallway",
        DepartmentType.KidsArea => "Kids' area",
        _ => type.ToString(),
    };
}
