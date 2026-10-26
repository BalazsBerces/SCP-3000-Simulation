using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The dev debug panel: sets up situations through the core's scenario builder, in debug builds
/// only. Keys: F6 places a Fog bank on the camera focus, F7 places a Villager there, F8 sends the
/// Villager placed last walking to it, F9 skips one Store hour. User arguments after "--":
///   --clear-weather          remove every Fog bank and stop new ones forming (fixed views, benchmarks);
///   --fog-bank=x,y[,radius]  place a Fog bank at Store position (x, y), 160 m across by default;
///   --villager=x,y           place a Villager at Store position (x, y);
///   --villager-to=x,y        send the Villager placed last walking to (x, y);
///   --follow-villager        follow the Villager placed last with the camera.
/// </summary>
public partial class DebugPanel : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    [Export] public float FogBankRadius { get; set; } = 160f;

    private int? _lastVillager;
    private Label? _sandboxStatus;
    private Label? _knownMap;

    public override void _Ready()
    {
        if (!OS.IsDebugBuild())
        {
            SetProcessUnhandledKeyInput(false);
            return;
        }
        if (Host.Snapshot.VillagerSandbox)
            AddSandboxPanel();
        AddKnownMapPanel();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--clear-weather")
                Host.HoldClearWeather();
            else if (arg.StartsWith("--fog-bank="))
            {
                var parts = Numbers(arg["--fog-bank=".Length..]);
                PlaceFogBank(new System.Numerics.Vector2(parts[0], parts[1]), parts.Length > 2 ? parts[2] : FogBankRadius);
            }
            else if (arg.StartsWith("--villager="))
            {
                var parts = Numbers(arg["--villager=".Length..]);
                PlaceVillager(new System.Numerics.Vector2(parts[0], parts[1]));
            }
            else if (arg.StartsWith("--villager-to="))
            {
                var parts = Numbers(arg["--villager-to=".Length..]);
                SendVillager(new System.Numerics.Vector2(parts[0], parts[1]));
            }
            else if (arg == "--follow-villager" && (_lastVillager ?? Host.Snapshot.Villagers.FirstOrDefault()?.Id) is { } id)
                Rig.Follow(id);
        }
    }

    private void AddKnownMapPanel()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(400, 148) };
        layer.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        var toggle = new CheckButton { Text = "Known map (V selects Villager)", ButtonPressed = true };
        column.AddChild(toggle);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(355, 200), Visible = true };
        column.AddChild(scroll);
        _knownMap = new Label { Name = "KnownMapDetails" };
        _knownMap.AddThemeFontSizeOverride("font_size", 13);
        scroll.AddChild(_knownMap);
        toggle.Toggled += visible => scroll.Visible = visible;
    }

    private void AddSandboxPanel()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(16, 148) };
        layer.AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "Villager sandbox" });
        var presets = new OptionButton { Name = "SandboxPreset" };
        foreach (var name in ScenarioBuilder.VillagerSandboxPresets)
            presets.AddItem(name);
        presets.Select(ScenarioBuilder.VillagerSandboxPresets.ToList().IndexOf(Host.SandboxPreset!));
        presets.ItemSelected += index => Host.LoadSandboxPreset(presets.GetItemText((int)index));
        column.AddChild(presets);
        var follow = new Button { Text = "Follow Villager (V)", FocusMode = Control.FocusModeEnum.None };
        follow.Pressed += () =>
        {
            if (Host.Snapshot.Villagers.FirstOrDefault() is { } villager)
                Rig.Follow(villager.Id);
        };
        column.AddChild(follow);
        column.AddChild(new Label { Text = "F7: place | F8: walk to focus\nF9: +1 Store hour | .: step | 6: 60x" });
        var status = new Label();
        _sandboxStatus = status;
        column.AddChild(status);
        void Refresh(WorldSnapshot snapshot)
        {
            _lastVillager = snapshot.Villagers.LastOrDefault()?.Id;
            status.Text = $"{snapshot.Bands.Count} Band | {snapshot.Villagers.Count} Villagers | {snapshot.Staff.Count} Staff\n" +
                $"{snapshot.Stock.Sum(g => g.Amount)} Goods | Gear: {string.Join(", ", snapshot.Gear.Select(g => g.Kind))}";
        }
        Refresh(Host.Snapshot);
        Host.WorldCreated += Refresh;
    }

    public override void _Process(double delta)
    {
        if (_knownMap is not null)
        {
            var selected = Host.Snapshot.Villagers.FirstOrDefault(v => v.Id == Rig.Following);
            _knownMap.Text = selected is null ? "Follow a Villager with V to inspect their Known map." : DescribeKnownMap(selected);
        }
        if (_sandboxStatus is null)
            return;
        var snapshot = Host.Snapshot;
        _sandboxStatus.Text = $"{snapshot.Bands.Count} Band | {snapshot.Villagers.Count} Villagers | {snapshot.Bodies.Count} bodies\n" +
            $"{snapshot.Stock.Sum(g => g.Amount)} Goods | {snapshot.Staff.Count} Staff";
    }

    private static string DescribeKnownMap(Villager villager)
    {
        var map = villager.KnownMap;
        static string Position(System.Numerics.Vector2 p) => $"({p.X:0}, {p.Y:0})";
        static string Seen(StoreClock clock, bool stale) => $"D{clock.Day} {clock.TimeOfDay:hh\\:mm}{(stale ? " STALE" : "")}";
        return $"Villager {villager.Id} | {villager.Action}\nExplored ground: {map.ExploredGround.Count} patches\n" +
            string.Join("\n", map.Goods.Select(g => $"{g.Goods.Kind}: {g.Goods.Amount} at {Position(g.Position)} {Seen(g.LastSeen, g.Stale)}")) + "\n" +
            string.Join("\n", map.Water.Select(w => $"{w.Kind}: {(w.Running is null ? "untested" : w.Running.Value ? "running" : "dry")}\n  {Position(w.Position)} {Seen(w.LastSeen, w.Stale)}")) + "\nGround: " +
            string.Join(" ", map.ExploredGround.Select(Position)) + "\n" +
            string.Join("\n", map.Dangers.Select(d => $"Staff {d.StaffId} at {Position(d.Position)} {Seen(d.LastSeen, d.Stale)}"));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.F6:
                PlaceFogBank(Rig.Focus.ToStore(), FogBankRadius);
                break;
            case Key.F7:
                PlaceVillager(Rig.Focus.ToStore());
                break;
            case Key.F8:
                SendVillager(Rig.Focus.ToStore());
                break;
            case Key.F9:
                Host.SkipTime(TimeSpan.FromHours(1));
                var clock = Host.Snapshot.Clock;
                GD.Print($"Debug: skipped one Store hour, now day {clock.Day} {clock.TimeOfDay.Hours:00}:{clock.TimeOfDay.Minutes:00}.");
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private static float[] Numbers(string list) =>
        list.Split(',').Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();

    private void PlaceFogBank(System.Numerics.Vector2 position, float radius)
    {
        var bank = Host.PlaceFogBank(position, radius);
        GD.Print($"Debug: placed Fog bank {bank.Id} at ({bank.Position.X:0}, {bank.Position.Y:0}), radius {bank.Radius:0} m.");
    }

    private void PlaceVillager(System.Numerics.Vector2 position)
    {
        var villager = Host.PlaceVillager(position);
        _lastVillager = villager.Id;
        GD.Print($"Debug: placed Villager {villager.Id} at ({villager.Position.X:0.0}, {villager.Position.Y:0.0}).");
    }

    private void SendVillager(System.Numerics.Vector2 destination)
    {
        if (_lastVillager is not { } id)
            return;
        Host.SendVillager(id, destination);
        var villager = Host.Snapshot.Villagers.First(v => v.Id == id);
        GD.Print(villager.Destination is { } to
            ? $"Debug: Villager {id} walks to ({to.X:0.0}, {to.Y:0.0})."
            : $"Debug: Villager {id} finds no way to ({destination.X:0.0}, {destination.Y:0.0}).");
    }
}
