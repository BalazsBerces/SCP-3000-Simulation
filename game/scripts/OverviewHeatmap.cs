using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// A heatmap over the <see cref="StoreOverview"/>: its Heatmap button turns it on, and the list beside it picks what it
/// shows: running Water points (the hidden truth: dry showroom taps count for nothing), or the units of one Goods class on
/// the Furniture. The Store is cut into <see cref="CellSize"/> cells, the amounts are smoothed over the neighbouring cells
/// (wrapped, as the Store is) and drawn as a see-through colour layer over everything, from clear (none) through blue to
/// red (the most). Rebuilt when a new world is created or the stock changes. The user argument <c>--heatmap=Mode</c>
/// (Water, Food, Drink, Comfort, Supplies) turns it on in that mode. Reads the snapshot only.
/// </summary>
public partial class OverviewHeatmap : CanvasLayer
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    /// <summary>The side of a heatmap cell, in metres.</summary>
    public const float CellSize = 16f;

    // How many cells each side the amounts are smoothed over.
    private const int Blur = 3;

    // The fountain court and the lake serve far more Villagers than one washbasin.
    private const float BigWaterWeight = 8f;

    private static readonly string[] Modes = ["Water", .. Enum.GetNames<GoodsClass>()];

    private Button _toggle = null!;
    private OptionButton _mode = null!;
    private Label _scale = null!;
    private MeshInstance3D _layer = null!;
    private IReadOnlyList<StockedGoods>? _builtStock;
    private int _builtMode = -1;

    public override void _Ready()
    {
        _toggle = new Button
        {
            Name = "Heatmap", Text = "Heatmap", ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
            Position = new Vector2(158f, 104f), Visible = StoreOverview.Enabled,
            TooltipText = "Show where Water or a Goods class is, as a heatmap over the Store",
        };
        _toggle.Toggled += _ => Refresh();
        AddChild(_toggle);
        _mode = new OptionButton { Name = "HeatmapMode", FocusMode = Control.FocusModeEnum.None, Position = new Vector2(250f, 104f), Visible = false };
        foreach (var mode in Modes)
            _mode.AddItem(mode);
        _mode.ItemSelected += _ => Refresh();
        AddChild(_mode);
        _scale = new Label { Name = "HeatmapScale", Position = new Vector2(380f, 108f), Visible = false };
        AddChild(_scale);

        _layer = new MeshInstance3D
        {
            Name = "HeatmapLayer", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            },
        };
        Rig.GetParent().CallDeferred(Node.MethodName.AddChild, _layer);

        StoreOverview.Changed += OnOverviewChanged;
        Host.WorldCreated += OnWorldCreated;
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--heatmap=")) is { } arg)
        {
            _mode.Selected = Math.Max(0, Array.IndexOf(Modes, arg["--heatmap=".Length..]));
            _toggle.ButtonPressed = true;
        }
    }

    public override void _ExitTree()
    {
        StoreOverview.Changed -= OnOverviewChanged;
        Host.WorldCreated -= OnWorldCreated;
    }

    public override void _Process(double delta)
    {
        if (_layer.Visible && !ReferenceEquals(Host.Snapshot.Stock, _builtStock))
            Build(Host.Snapshot);
    }

    private void OnOverviewChanged(bool enabled)
    {
        _toggle.Visible = enabled;
        Refresh();
    }

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        _builtStock = null;
        Refresh();
    }

    private void Refresh()
    {
        var on = StoreOverview.Enabled && _toggle.ButtonPressed;
        _mode.Visible = _scale.Visible = _layer.Visible = on;
        if (on && (_builtMode != _mode.Selected || !ReferenceEquals(Host.Snapshot.Stock, _builtStock)))
            Build(Host.Snapshot);
    }

    private void Build(WorldSnapshot snapshot)
    {
        _builtStock = snapshot.Stock;
        _builtMode = _mode.Selected;
        var space = snapshot.Space;
        var cells = Math.Max(1, (int)Math.Round(space.Size / CellSize));
        var amounts = new float[cells * cells];
        void Add(System.Numerics.Vector2 position, float amount)
        {
            var at = space.Wrap(position);
            var (x, y) = (Math.Min(cells - 1, (int)(at.X / space.Size * cells)), Math.Min(cells - 1, (int)(at.Y / space.Size * cells)));
            amounts[y * cells + x] += amount;
        }

        if (_builtMode == 0)
        {
            foreach (var water in snapshot.WaterPoints.Where(w => w.Running))
                Add(water.Position, water.Kind is WaterPointKind.FountainCourt or WaterPointKind.CampingLake ? BigWaterWeight : 1f);
        }
        else
        {
            var goodsClass = (GoodsClass)(_builtMode - 1);
            var pieces = snapshot.Furniture.ToDictionary(p => p.Id);
            foreach (var goods in snapshot.Stock.Where(g => GoodsCatalog.ClassOf(g.Kind) == goodsClass))
                Add(pieces[goods.FurnitureId].Position, goods.Amount);
        }

        var heat = Smoothed(Smoothed(amounts, cells, horizontal: true), cells, horizontal: false);
        // The 99th percentile of the warm cells is full red, so one huge pile does not wash out the rest.
        var warm = heat.Where(h => h > 0f).OrderBy(h => h).ToArray();
        var top = warm.Length == 0 ? 1f : warm[(int)((warm.Length - 1) * 0.99)];
        var image = Image.CreateEmpty(cells, cells, false, Image.Format.Rgba8);
        for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
                image.SetPixel(x, y, Colour(Math.Min(1f, heat[y * cells + x] / top)));
        ((StandardMaterial3D)_layer.MaterialOverride).AlbedoTexture = ImageTexture.CreateFromImage(image);
        _layer.Mesh = new PlaneMesh { Size = new Vector2(space.Size, space.Size) };
        _layer.Position = new System.Numerics.Vector2(space.Size / 2f).ToWorld(0.2f);

        var units = _builtMode == 0 ? "running Water points" : "units";
        _scale.Text = $"clear: none   blue → red: most ({amounts.Sum():0} {units} in all)";
        GD.Print($"Heatmap {Modes[_builtMode]}: {cells}x{cells} cells, {amounts.Sum():0} {units}.");
    }

    /// <summary>A box blur of the cells along one axis, <see cref="Blur"/> cells each side, wrapped.</summary>
    private static float[] Smoothed(float[] cells, int side, bool horizontal)
    {
        var smoothed = new float[cells.Length];
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
            {
                var sum = 0f;
                for (var d = -Blur; d <= Blur; d++)
                {
                    var (sx, sy) = horizontal ? ((x + d + side) % side, y) : (x, (y + d + side) % side);
                    sum += cells[sy * side + sx];
                }
                smoothed[y * side + x] = sum / (2 * Blur + 1);
            }
        return smoothed;
    }

    /// <summary>Clear at none, then blue, cyan, yellow and red, more opaque the hotter.</summary>
    private static Color Colour(float t)
    {
        if (t <= 0.01f)
            return new Color(0f, 0f, 0f, 0f);
        Color[] stops = [new(0.15f, 0.25f, 0.9f), new(0.1f, 0.8f, 0.9f), new(1f, 0.9f, 0.2f), new(0.95f, 0.15f, 0.1f)];
        var at = t * (stops.Length - 1);
        var i = Math.Min(stops.Length - 2, (int)at);
        var colour = stops[i].Lerp(stops[i + 1], at - i);
        colour.A = 0.35f + 0.45f * t;
        return colour;
    }
}
