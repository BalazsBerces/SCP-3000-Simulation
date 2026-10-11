using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Store's light and haze. Reads the Store light level from the core at the interpolated
/// Store time and blends the scene between the bright, fluorescent open Store and dark Closing
/// time; while the lights fade they flicker like failing fluorescent tubes. A low volumetric
/// haze layer follows the camera: thick at floor level, so ground-level view is short and the
/// shelving fades into fog, and thin above, so the angled Observer camera still sees the floor.
/// </summary>
public partial class StoreAtmosphere : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public WorldEnvironment World { get; set; } = null!;
    [Export] public DirectionalLight3D StoreLight { get; set; } = null!;
    [Export] public FogVolume GroundHaze { get; set; } = null!;

    /// <summary>The look of the Store at one light level.</summary>
    private readonly record struct Look(
        Color Ambient, float AmbientEnergy,
        Color Light, float LightEnergy,
        Color Fog, float FogDensity,
        Color Haze, float HazeDensity, Color HazeGlow)
    {
        // Colours and ambient light follow a curve, so the Store darkens quickly once the lights start failing.
        public static Look Blend(Look closed, Look open, float light)
        {
            var t = Mathf.Clamp(light, 0f, 1f);
            var curved = t * t;
            return new Look(
                closed.Ambient.Lerp(open.Ambient, t), Mathf.Lerp(closed.AmbientEnergy, open.AmbientEnergy, curved),
                open.Light, Mathf.Lerp(closed.LightEnergy, open.LightEnergy, t),
                closed.Fog.Lerp(open.Fog, curved), Mathf.Lerp(closed.FogDensity, open.FogDensity, t),
                closed.Haze.Lerp(open.Haze, curved), Mathf.Lerp(closed.HazeDensity, open.HazeDensity, t),
                closed.HazeGlow.Lerp(open.HazeGlow, curved));
        }
    }

    private static readonly Look Open = new(
        Ambient: new Color(0.86f, 0.89f, 0.92f), AmbientEnergy: 0.6f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0.9f,
        Fog: new Color(0.6f, 0.62f, 0.63f), FogDensity: 0.0045f,
        Haze: new Color(0.82f, 0.84f, 0.85f), HazeDensity: 0.16f,
        HazeGlow: new Color(0.42f, 0.43f, 0.44f));

    private static readonly Look Closed = new(
        Ambient: new Color(0.3f, 0.36f, 0.55f), AmbientEnergy: 0.045f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0f,
        Fog: new Color(0.008f, 0.009f, 0.013f), FogDensity: 0.008f,
        Haze: new Color(0.25f, 0.28f, 0.35f), HazeDensity: 0.2f,
        HazeGlow: new Color(0.004f, 0.005f, 0.008f));

    private readonly RandomNumberGenerator _random = new();
    private Godot.Environment _environment = null!;
    private FogMaterial _haze = null!;
    private double _dropoutLeft;

    /// <summary>The Store light level shown this frame (0 out, 1 fully on), before flicker.</summary>
    public float LightLevel { get; private set; } = 1f;

    public override void _Ready()
    {
        _environment = World.Environment;
        _haze = (FogMaterial)GroundHaze.Material;
        Apply(Look.Blend(Closed, Open, 1f));
    }

    public override void _Process(double delta)
    {
        var timeOfDay = TimeSpan.FromTicks(Host.InterpolatedStoreTime.Ticks % TimeSpan.TicksPerDay);
        LightLevel = (float)StoreHours.LightLevelAt(timeOfDay);
        var shown = LightLevel * Flicker(delta);
        Apply(Look.Blend(Closed, Open, shown));

        var focus = Rig.Focus;
        GroundHaze.GlobalPosition = new Vector3(focus.X, 0f, focus.Z);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"Store light {LightLevel:0.00}, ambient {_environment.AmbientLightEnergy:0.000}, " +
        $"fog {_environment.FogDensity:0.0000}, haze {_haze.Density:0.000} (volumetric {_environment.VolumetricFogEnabled})";

    // While the lights are fading, tubes drop out for a moment now and then, most often halfway.
    private float Flicker(double delta)
    {
        if (_dropoutLeft > 0)
        {
            _dropoutLeft -= delta;
            return 0.35f;
        }
        var fading = 1f - Mathf.Abs(2f * LightLevel - 1f);
        if (LightLevel is > 0.01f and < 0.99f && _random.Randf() < fading * 6f * delta)
            _dropoutLeft = _random.RandfRange(0.04f, 0.16f);
        return 1f;
    }

    private void Apply(Look look)
    {
        _environment.AmbientLightColor = look.Ambient;
        _environment.AmbientLightEnergy = look.AmbientEnergy;
        StoreLight.LightColor = look.Light;
        StoreLight.LightEnergy = look.LightEnergy;
        StoreLight.Visible = look.LightEnergy > 0.001f;
        _environment.BackgroundColor = look.Fog;
        _environment.FogLightColor = look.Fog;
        _environment.FogDensity = look.FogDensity;
        _haze.Albedo = look.Haze;
        _haze.Density = look.HazeDensity;
        _haze.Emission = look.HazeGlow;
    }
}
