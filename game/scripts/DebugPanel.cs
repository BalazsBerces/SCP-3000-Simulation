using System.Globalization;
using Godot;

namespace Scp3008.Game;

/// <summary>
/// The dev debug panel: sets up situations through the core's scenario builder, in debug builds
/// only. So far: F6 places a Fog bank on the camera focus. User arguments after "--":
///   --clear-weather          remove every Fog bank and stop new ones forming (fixed views, benchmarks);
///   --fog-bank=x,y[,radius]  place a Fog bank at Store position (x, y), 160 m across by default.
/// </summary>
public partial class DebugPanel : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    [Export] public float FogBankRadius { get; set; } = 160f;

    public override void _Ready()
    {
        if (!OS.IsDebugBuild())
        {
            SetProcessUnhandledKeyInput(false);
            return;
        }
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--clear-weather")
                Host.HoldClearWeather();
            else if (arg.StartsWith("--fog-bank="))
            {
                var parts = arg["--fog-bank=".Length..].Split(',')
                    .Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
                PlaceFogBank(new System.Numerics.Vector2(parts[0], parts[1]), parts.Length > 2 ? parts[2] : FogBankRadius);
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
        {
            PlaceFogBank(Rig.Focus.ToStore(), FogBankRadius);
            GetViewport().SetInputAsHandled();
        }
    }

    private void PlaceFogBank(System.Numerics.Vector2 position, float radius)
    {
        var bank = Host.PlaceFogBank(position, radius);
        GD.Print($"Debug: placed Fog bank {bank.Id} at ({bank.Position.X:0}, {bank.Position.Y:0}), radius {bank.Radius:0} m.");
    }
}
