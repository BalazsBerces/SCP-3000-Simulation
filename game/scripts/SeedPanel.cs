using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Observer seed input: type a seed (a number, or any text, which is hashed to one) and press
/// Generate or Enter for a newly generated Store; Random picks a seed. Below, the Pillar layout
/// row (Pillars: 3×3 | 2×2 | 3×3 large | Corners | Diamond) marks the active layout; choosing another regenerates the
/// Store with the same seed and that layout, like Generate. The Landmark showcase button switches between the
/// Store and the Landmark showcase (every Landmark in a row, no Displays) with the same seed and layout; in the
/// showcase a button per Landmark flies the camera to it. The Point of interest showcase button does the same for
/// the Points of interest (every kind in a row, no Displays or Landmarks). The Credits button shows CREDITS.md: the
/// source, author and licence of every external model and texture. Also names the Department under the camera focus.
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
    private Button _poiShowcase = null!;
    private HFlowContainer _poiButtons = null!;

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
        _poiShowcase = new Button
        {
            Name = "PointOfInterestShowcase", Text = "Point of interest showcase", ToggleMode = true, ButtonPressed = Host.PointOfInterestShowcase,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Every Point of interest side by side, with no Displays or Landmarks; press again for the Store",
        };
        _poiShowcase.Toggled += ChoosePointOfInterestShowcase;
        column.AddChild(_poiShowcase);
        _poiButtons = new HFlowContainer { Name = "PointsOfInterest" };
        column.AddChild(_poiButtons);
        ShowShowcaseButtons(Host.Snapshot);
        // After the camera has set its start view.
        Callable.From(FlyToFirstOfShowcase).CallDeferred();

        var credits = new Button
        {
            Name = "Credits", Text = "Credits", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Sources, authors and licences of the external models and textures",
        };
        credits.Pressed += ShowCredits;
        column.AddChild(credits);

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

    /// <summary>Switches between the Store and the Point of interest showcase, keeping the seed and the Pillar layout.</summary>
    public void ChoosePointOfInterestShowcase(bool showcase)
    {
        if (Host.IsGenerating || showcase == Host.Snapshot.PointOfInterestShowcase)
        {
            _poiShowcase.SetPressedNoSignal(Host.IsGenerating ? Host.PointOfInterestShowcase : Host.Snapshot.PointOfInterestShowcase);
            return;
        }
        Host.CreateWorld(Host.Snapshot.Seed, pointOfInterestShowcase: showcase);
    }

    /// <summary>In a showcase, a button per Landmark or Point of interest, west to east, that flies the camera to it.</summary>
    private void ShowShowcaseButtons(WorldSnapshot snapshot)
    {
        FillButtons(_landmarkButtons, snapshot.LandmarkShowcase,
            snapshot.Landmarks.Select(l => (LandmarkNames.Of(l.Type), l.Centre, Extent(l))));
        FillButtons(_poiButtons, snapshot.PointOfInterestShowcase,
            snapshot.PointsOfInterest.Select(p => (PointOfInterestNames.Of(p.Type), p.Centre, MathF.Max(p.Size.X, p.Size.Y))));
    }

    /// <summary>
    /// How big a Landmark looks: its longer side, or for a tall, narrow one (the abandoned scaffold) its height,
    /// so the camera backs off far enough to see it whole.
    /// </summary>
    private static float Extent(Landmark landmark) =>
        MathF.Max(MathF.Max(landmark.Size.X, landmark.Size.Y), landmark.Height * 1.5f);

    private void FillButtons(HFlowContainer container, bool visible, IEnumerable<(string Name, System.Numerics.Vector2 Centre, float Extent)> places)
    {
        foreach (var child in container.GetChildren())
            child.QueueFree();
        container.Visible = visible;
        if (!visible)
            return;
        foreach (var (name, centre, extent) in places.OrderBy(p => p.Centre.X))
        {
            var button = new Button { Text = name, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Rig.FlyTo(centre, extent * ShowcaseDistancePerMetre);
            container.AddChild(button);
        }
    }

    /// <summary>Shows CREDITS.md in a dialog: headings in bold, links clickable.</summary>
    internal void ShowCredits()
    {
        var text = new RichTextLabel
        {
            Name = "CreditsText", BbcodeEnabled = true, FitContent = true, SelectionEnabled = true,
            Text = CreditsBbcode(Godot.FileAccess.GetFileAsString("res://CREDITS.md")),
        };
        text.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(760f, 440f) };
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(text);
        var dialog = new AcceptDialog { Name = "CreditsDialog", Title = "Credits", Exclusive = false };
        dialog.AddChild(scroll);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>The small Markdown subset of CREDITS.md (headings, links, code spans) as BBCode.</summary>
    internal static string CreditsBbcode(string markdown)
    {
        var lines = markdown.Replace("\r", "").Split('\n').Select(line =>
        {
            line = line.Replace("[", "[lb]").Replace("`", "");
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\[lb\]([^\]]*)\]\(([^)]*)\)", "[url=$2]$1[/url]");
            return line.StartsWith('#') ? $"[b]{line.TrimStart('#', ' ')}[/b]" : line;
        });
        return string.Join("\n", lines);
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
        _poiShowcase.SetPressedNoSignal(snapshot.PointOfInterestShowcase);
        ShowShowcaseButtons(snapshot);
        FlyToFirstOfShowcase();
    }

    /// <summary>A showcase starts at its first (westmost) Landmark or Point of interest.</summary>
    private void FlyToFirstOfShowcase()
    {
        var snapshot = Host.Snapshot;
        if (snapshot.LandmarkShowcase && snapshot.Landmarks.MinBy(l => l.Centre.X) is { } landmark)
            Rig.FlyTo(landmark.Centre, Extent(landmark) * ShowcaseDistancePerMetre);
        else if (snapshot.PointOfInterestShowcase && snapshot.PointsOfInterest.MinBy(p => p.Centre.X) is { } poi)
            Rig.FlyTo(poi.Centre, MathF.Max(poi.Size.X, poi.Size.Y) * ShowcaseDistancePerMetre);
    }
}

/// <summary>Display names of the Point of interest types.</summary>
public static class PointOfInterestNames
{
    public static string Of(PointOfInterestType type) => type switch
    {
        PointOfInterestType.Restroom => "Restroom",
        PointOfInterestType.Canteen => "Canteen",
        PointOfInterestType.SetDinnerTable => "Dinner table",
        PointOfInterestType.LostAndFoundDesk => "Lost and found",
        PointOfInterestType.ShoppingCartGraveyard => "Cart graveyard",
        PointOfInterestType.BallPit => "Ball pit",
        PointOfInterestType.ArmchairHenge => "Armchair henge",
        PointOfInterestType.ExitSign => "EXIT sign",
        PointOfInterestType.MannequinCrowd => "Mannequins",
        PointOfInterestType.DoorToNowhere => "Door to nowhere",
        PointOfInterestType.DisplayAquarium => "Aquarium",
        _ => type.ToString(),
    };
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
        LandmarkType.AbandonedScaffold => "Abandoned scaffold",
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
