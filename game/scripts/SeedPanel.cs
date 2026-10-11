using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Observer seed input: type a seed (a number, or any text, which is hashed to one) and press
/// Generate or Enter for a newly generated Store; Random picks a seed. Also names the
/// Department under the camera focus.
/// </summary>
public partial class SeedPanel : CanvasLayer
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    private LineEdit _seedInput = null!;
    private Button _generate = null!;
    private Button _random = null!;
    private Label _status = null!;

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

        _status = new Label { Name = "Department" };
        column.AddChild(_status);
        Host.WorldCreated += OnWorldCreated;
    }

    public override void _ExitTree() => Host.WorldCreated -= OnWorldCreated;

    public override void _Process(double delta)
    {
        if (Host.IsGenerating)
        {
            _status.Text = $"Generating Store {Host.Seed}…";
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

    private void Generate()
    {
        _seedInput.ReleaseFocus();
        Host.CreateWorld(ParseSeed(_seedInput.Text));
    }

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        if (!_seedInput.HasFocus())
            _seedInput.Text = snapshot.Seed.ToString();
    }
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
        DepartmentType.KidsArea => "Kids' area",
        _ => type.ToString(),
    };
}
