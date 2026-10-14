using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Store's light and air. Reads the Store light level from the core at the interpolated
/// Store time and blends the scene between the bright, fluorescent open Store and dark Closing
/// time; while the lights fade they flicker like failing fluorescent tubes. The air is clear: there is
/// no base fog or haze in any light state. The only fog is inside the Fog banks (<see cref="FogBankRenderer"/>),
/// whose look this also sets: lit and greyish while the Store is open, dark and bluish at Closing time.
/// The one other depth effect is the Edge fade: a distance fade applied only in the last stretch before
/// the draw distance, so content dissolves softly where drawing stops. The volumetric fog (Fog banks only)
/// reaches as far as the camera can see at its current zoom, so distant Fog banks show, while close views keep their detail.
/// The Store light shines almost straight down (12 degrees from vertical) and casts soft, short shadows
/// out to the far edge of the view at the current zoom, fading softly beyond it; they fade with the
/// light and are gone at Closing time, and the overview has none. Screen-space ambient occlusion
/// darkens contact areas while the lights are on, so the ambient light is lower than without shadows.
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

    /// <summary>
    /// How far Store-light shadows reach per metre of zoom distance, plus a margin: past the far edge
    /// of the view (about 100 m at the default zoom), so they fade out beyond it.
    /// </summary>
    [Export] public float ShadowReachPerZoomMetre { get; set; } = 2.6f;
    [Export] public float ShadowReachMargin { get; set; } = 20f;
    [Export] public float MinShadowReach { get; set; } = 60f;
    /// <summary>The farthest shadows reach at any zoom; far zooms see small, coarse Furniture whose shadows hardly show.</summary>
    [Export] public float MaxShadowReach { get; set; } = 180f;

    /// <summary>Screen-space ambient occlusion strength while the Store is open; it fades with the Store lights.</summary>
    [Export] public float OpenSsaoIntensity { get; set; } = 2.5f;

    /// <summary>Distance from the camera where the Edge fade starts (the draw distance is about 600 m; the last chunks drop out from about 555 m).</summary>
    [Export] public float EdgeFadeBegin { get; set; } = 480f;
    /// <summary>Distance from the camera where the Edge fade is complete.</summary>
    [Export] public float EdgeFadeEnd { get; set; } = 550f;

    /// <summary>The look of the Store at one light level.</summary>
    private readonly record struct Look(
        Color Ambient, float AmbientEnergy,
        Color Light, float LightEnergy,
        Color Backdrop,
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
                closed.Backdrop.Lerp(open.Backdrop, curved),
                closed.Bank.Lerp(open.Bank, curved), Mathf.Lerp(closed.BankDensity, open.BankDensity, t),
                closed.BankGlow.Lerp(open.BankGlow, curved));
        }
    }

    private static readonly Look Open = new(
        Ambient: new Color(0.86f, 0.89f, 0.92f), AmbientEnergy: 0.4f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0.95f,
        Backdrop: new Color(0.6f, 0.62f, 0.63f),
        Bank: new Color(0.82f, 0.84f, 0.85f), BankDensity: 0.22f,
        BankGlow: new Color(0.5f, 0.51f, 0.52f));

    private static readonly Look Closed = new(
        Ambient: new Color(0.3f, 0.36f, 0.55f), AmbientEnergy: 0.045f,
        Light: new Color(0.95f, 0.97f, 1f), LightEnergy: 0f,
        Backdrop: new Color(0.008f, 0.009f, 0.013f),
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

    /// <summary>The fluorescent Store light level shown this frame, flicker included.</summary>
    public float ShownLightLevel { get; private set; } = 1f;

    /// <summary>How the Fog banks look this frame: albedo, glow, and the shader density of a bank of density 1.</summary>
    public (Color Albedo, Color Emission, float Density) FogBankLook { get; private set; }

    public void ApplyLampFog(ShaderMaterial material)
    {
        var fading = _environment.FogEnabled;
        material.SetShaderParameter("edge_begin", fading ? EdgeFadeBegin : 1e9f);
        material.SetShaderParameter("edge_end", fading ? EdgeFadeEnd : 2e9f);
        material.SetShaderParameter("fog_length", _environment.VolumetricFogLength);
    }

    public override void _Ready()
    {
        _environment = World.Environment;
        // The only distance fog is the Edge fade; the air has no base fog or haze.
        _environment.FogMode = Godot.Environment.FogModeEnum.Depth;
        _environment.FogDepthBegin = EdgeFadeBegin;
        _environment.FogDepthEnd = EdgeFadeEnd;
        _environment.FogDepthCurve = 1f;
        _environment.FogDensity = 0f;
        _environment.VolumetricFogDensity = 0f;
        Apply(Look.Blend(Closed, Open, _appliedLevel = 1f));
        _fogEnabled = _environment.FogEnabled;
        _volumetricFogEnabled = _environment.VolumetricFogEnabled;
        StoreOverview.Changed += OnOverviewChanged;
        FurnitureDetail.Changed += ShowDetail;
        ShowDetail(FurnitureDetail.Coarse);
    }

    public override void _ExitTree()
    {
        StoreOverview.Changed -= OnOverviewChanged;
        FurnitureDetail.Changed -= ShowDetail;
    }

    /// <summary>
    /// The overview shows the whole Store, so the Edge fade and the Fog banks are lifted while it is on,
    /// and the Store light casts no shadows.
    /// </summary>
    private void OnOverviewChanged(bool enabled)
    {
        _environment.FogEnabled = !enabled && _fogEnabled;
        _environment.VolumetricFogEnabled = !enabled && _volumetricFogEnabled;
        StoreLight.ShadowEnabled = !enabled;
    }

    /// <summary>The Store light lights, and shadows are cast by, the Furniture meshes the camera sees (coarse ones at far zoom).</summary>
    private void ShowDetail(bool coarse)
    {
        StoreLight.LightCullMask = FurnitureDetail.Mask(StoreLight.LightCullMask);
        StoreLight.ShadowCasterMask = FurnitureDetail.Mask(StoreLight.ShadowCasterMask);
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
        StoreLight.DirectionalShadowMaxDistance = Mathf.Clamp(
            Rig.Distance * ShadowReachPerZoomMetre + ShadowReachMargin, MinShadowReach, MaxShadowReach);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"Store light {LightLevel:0.00}, ambient {_environment.AmbientLightEnergy:0.000}, " +
        $"base fog {_environment.FogDensity:0.0000}, haze {_environment.VolumetricFogDensity:0.0000}, " +
        $"Edge fade {EdgeFadeBegin:0}-{EdgeFadeEnd:0} m (on {_environment.FogEnabled}), " +
        $"volumetric {_environment.VolumetricFogEnabled}, reach {_environment.VolumetricFogLength:0} m, " +
        $"Fog bank density {FogBankLook.Density:0.000}, " +
        $"shadows {StoreLight.ShadowEnabled && StoreLight.Visible} to {StoreLight.DirectionalShadowMaxDistance:0} m, SSAO {_environment.SsaoEnabled}";

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
        // Contact shading fades with the Store lights; at Closing time the Lamps light the Store and it is off.
        var level = look.LightEnergy / Open.LightEnergy;
        _environment.SsaoIntensity = OpenSsaoIntensity * level;
        _environment.SsaoEnabled = level > 0.05f;
        // Content dissolves into the backdrop at the Edge fade.
        _environment.BackgroundColor = look.Backdrop;
        _environment.FogLightColor = look.Backdrop;
        FogBankLook = (look.Bank, look.BankGlow, look.BankDensity);
    }
}
