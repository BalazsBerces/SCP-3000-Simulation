using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Store's light and haze. Reads the Store light level from the core at the interpolated
/// Store time and blends the scene between the bright, fluorescent open Store and dark Closing
/// time; while the lights fade they flicker like failing fluorescent tubes. A thin haze lies
/// everywhere for depth: distance fog that hides the far cut-off, plus a faint volumetric haze that
/// catches the lamp light. Thick fog only lies inside the Fog banks (<see cref="FogBankRenderer"/>),
/// whose look this also sets: lit and greyish while the Store is open, dark and bluish at Closing time.
/// The volumetric fog reaches as far as the camera can see at its current zoom, so distant Fog banks
/// show, while close views keep their detail.
/// </summary>
public partial class StoreAtmosphere : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public WorldEnvironment World { get; set; } = null!;
    [Export] public DirectionalLight3D StoreLight { get; set; } = null!;

    /// <summary>Farthest floor the camera sees per metre of zoom distance, plus a margin: how far the volumetric fog must reach.</summary>
    [Export] public float VolumetricReachPerZoomMetre { get; set; } = 2.6f;
    [Export] public float VolumetricReachMargin { get; set; } = 60f;
    [Export] public float MinVolumetricReach { get; set; } = 150f;
    [Export] public float MaxVolumetricReach { get; set; } = 640f;

    /// <summary>The look of the Store at one light level.</summary>
    private readonly record struct Look(
        Color Ambient, float AmbientEnergy,
        Color Light, float LightEnergy,
        Color Fog, float FogDensity,
        Color Haze, float HazeDensity, Color HazeGlow,
        Color Bank, float BankDensity, Color BankGlow)
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
                closed.HazeGlow.Lerp(open.HazeGlow, curved),
                closed.Bank.Lerp(open.Bank, curved), Mathf.Lerp(closed.BankDensity, open.BankDensity, t),
                closed.BankGlow.Lerp(open.BankGlow, curved));
        }
    }

    private static readonly Look Open = new(
        Ambient: new Color(0.86f, 0.89f, 0.92f), AmbientEnergy: 0.6f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0.9f,
        Fog: new Color(0.6f, 0.62f, 0.63f), FogDensity: 0.0045f,
        Haze: new Color(0.82f, 0.84f, 0.85f), HazeDensity: 0.004f,
        HazeGlow: new Color(0.02f, 0.02f, 0.02f),
        Bank: new Color(0.82f, 0.84f, 0.85f), BankDensity: 0.22f,
        BankGlow: new Color(0.5f, 0.51f, 0.52f));

    private static readonly Look Closed = new(
        Ambient: new Color(0.3f, 0.36f, 0.55f), AmbientEnergy: 0.045f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0f,
        Fog: new Color(0.008f, 0.009f, 0.013f), FogDensity: 0.008f,
        Haze: new Color(0.25f, 0.28f, 0.35f), HazeDensity: 0.006f,
        HazeGlow: new Color(0.0005f, 0.0006f, 0.001f),
        Bank: new Color(0.25f, 0.29f, 0.4f), BankDensity: 0.25f,
        BankGlow: new Color(0.16f, 0.2f, 0.32f));

    private readonly RandomNumberGenerator _random = new();
    private Godot.Environment _environment = null!;
    private double _dropoutLeft;
    private float _appliedLevel;
    // Fog settings from the scene, restored when the overview is switched off.
    private bool _fogEnabled;
    private bool _volumetricFogEnabled;

    /// <summary>The Store light level shown this frame (0 out, 1 fully on), before flicker.</summary>
    public float LightLevel { get; private set; } = 1f;

    /// <summary>The Store light level shown this frame, flicker included; Lamps on display follow it.</summary>
    public float ShownLightLevel { get; private set; } = 1f;

    /// <summary>How the Fog banks look this frame: albedo, glow, and the shader density of a bank of density 1.</summary>
    public (Color Albedo, Color Emission, float Density) FogBankLook { get; private set; }

    public override void _Ready()
    {
        _environment = World.Environment;
        Apply(Look.Blend(Closed, Open, _appliedLevel = 1f));
        _fogEnabled = _environment.FogEnabled;
        _volumetricFogEnabled = _environment.VolumetricFogEnabled;
        StoreOverview.Changed += OnOverviewChanged;
    }

    public override void _ExitTree() => StoreOverview.Changed -= OnOverviewChanged;

    /// <summary>The overview shows the whole Store, so the haze and the Fog banks are lifted while it is on.</summary>
    private void OnOverviewChanged(bool enabled)
    {
        _environment.FogEnabled = !enabled && _fogEnabled;
        _environment.VolumetricFogEnabled = !enabled && _volumetricFogEnabled;
    }

    public override void _Process(double delta)
    {
        var timeOfDay = TimeSpan.FromTicks(Host.InterpolatedStoreTime.Ticks % TimeSpan.TicksPerDay);
        LightLevel = (float)StoreHours.LightLevelAt(timeOfDay);
        ShownLightLevel = LightLevel * Flicker(delta);
        // The light is steady most of the day; only push a new look when it changes.
        if (ShownLightLevel != _appliedLevel)
            Apply(Look.Blend(Closed, Open, _appliedLevel = ShownLightLevel));

        _environment.VolumetricFogLength = Mathf.Clamp(
            Rig.Distance * VolumetricReachPerZoomMetre + VolumetricReachMargin, MinVolumetricReach, MaxVolumetricReach);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"Store light {LightLevel:0.00}, ambient {_environment.AmbientLightEnergy:0.000}, " +
        $"fog {_environment.FogDensity:0.0000}, haze {_environment.VolumetricFogDensity:0.0000} " +
        $"(volumetric {_environment.VolumetricFogEnabled}, reach {_environment.VolumetricFogLength:0} m), " +
        $"Fog bank density {FogBankLook.Density:0.000}";

    // While the lights are fading, tubes drop out for a moment now and then, most often halfway;
    // a paused Store stands still, so no new dropouts start then.
    private float Flicker(double delta)
    {
        if (_dropoutLeft > 0)
        {
            _dropoutLeft -= delta;
            return 0.35f;
        }
        var fading = 1f - Mathf.Abs(2f * LightLevel - 1f);
        if (Host.Speed != SimulationSpeed.Paused && LightLevel is > 0.01f and < 0.99f && _random.Randf() < fading * 6f * delta)
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
        _environment.VolumetricFogAlbedo = look.Haze;
        _environment.VolumetricFogDensity = look.HazeDensity;
        _environment.VolumetricFogEmission = look.HazeGlow;
        FogBankLook = (look.Bank, look.BankGlow, look.BankDensity);
    }
}
