using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws the Fog banks as thick volumetric fog. Each bank is a box-shaped FogVolume whose fog
/// shader follows the core's density falloff, shaped as a low dome that only giant Landmarks rise
/// above. Banks drift smoothly (interpolated between snapshots) and are drawn at every wrapped
/// image near the camera, so a bank on a seam shows on both sides of it. A seamless noise rolls
/// through the fog with the wind; its tile divides the Store size, so it lines up across the seams
/// and the camera's whole-Store recentring. The look follows the light cycle through
/// <see cref="StoreAtmosphere"/>. View only: it never touches the simulation.
/// </summary>
public partial class FogBankRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;

    /// <summary>Height of a bank's dome at its centre, in metres: well above the tallest Furniture.</summary>
    [Export] public float Top { get; set; } = 18f;

    /// <summary>How far from the focus banks are drawn; at least the farthest floor the camera can see.</summary>
    [Export] public float ViewReach { get; set; } = 700f;

    // The Store (1344 m) is exactly 7 noise tiles, so the noise is seamless at the wrap.
    private const float NoiseTilesPerStore = 7f;

    private readonly List<(FogVolume Volume, ShaderMaterial Material)> _volumes = [];
    private Shader _shader = null!;
    private NoiseTexture3D _noise = null!;
    private Vector3 _noiseOffset;
    private TimeSpan? _lastStoreTime;

    /// <summary>How many Fog bank images were drawn this frame.</summary>
    public int Drawn { get; private set; }

    public override void _Ready()
    {
        _shader = GD.Load<Shader>("res://shaders/fog_bank.gdshader");
        _noise = new NoiseTexture3D
        {
            Width = 64, Height = 64, Depth = 64, Seamless = true, Normalize = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.06f, FractalOctaves = 3 },
        };
        Host.WorldCreated += _ => _lastStoreTime = null;
    }

    public override void _Process(double delta)
    {
        if (StoreOverview.Enabled)
        {
            HideFrom(0);
            return;
        }
        var snapshot = Host.Snapshot;
        var size = snapshot.Space.Size;
        RollNoise(snapshot.Wind);

        var look = Atmosphere.FogBankLook;
        var focus = Rig.Focus.ToStore();
        var drawn = 0;
        foreach (var bank in Host.InterpolatedFogBanks)
        {
            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var centre = bank.Position + new System.Numerics.Vector2(ox, oy) * size;
                    if (System.Numerics.Vector2.Distance(centre, focus) > ViewReach + bank.Radius)
                        continue;
                    var (volume, material) = VolumeAt(drawn++);
                    volume.Position = centre.ToWorld(Top / 2f);
                    volume.Size = new Vector3(2f * bank.Radius, Top, 2f * bank.Radius);
                    volume.Visible = true;
                    material.SetShaderParameter("radius", bank.Radius);
                    material.SetShaderParameter("density", bank.Density * look.Density);
                    material.SetShaderParameter("albedo", look.Albedo);
                    material.SetShaderParameter("emission", look.Emission);
                    material.SetShaderParameter("noise_offset", _noiseOffset);
                }
            }
        }
        HideFrom(drawn);
        Drawn = drawn;
    }

    /// <summary>Moves the noise with the wind by the Store time shown since the last frame.</summary>
    private void RollNoise(System.Numerics.Vector2 wind)
    {
        var now = Host.InterpolatedStoreTime;
        var hours = _lastStoreTime is { } last ? Math.Max(0.0, (now - last).TotalHours) : 0.0;
        _lastStoreTime = now;
        // Fog also churns slowly upwards, so even a calm bank does not look frozen.
        var tile = Host.Snapshot.Space.Size / NoiseTilesPerStore;
        _noiseOffset += new Vector3(wind.X, 6f, wind.Y) * (float)hours;
        _noiseOffset = new Vector3(Mathf.PosMod(_noiseOffset.X, tile), Mathf.PosMod(_noiseOffset.Y, tile), Mathf.PosMod(_noiseOffset.Z, tile));
    }

    private (FogVolume Volume, ShaderMaterial Material) VolumeAt(int index)
    {
        while (_volumes.Count <= index)
        {
            var material = new ShaderMaterial { Shader = _shader };
            material.SetShaderParameter("noise", _noise);
            material.SetShaderParameter("core_fraction", FogBank.CoreFraction);
            material.SetShaderParameter("top", Top);
            material.SetShaderParameter("noise_scale", Host.Snapshot.Space.Size / NoiseTilesPerStore);
            var volume = new FogVolume { Shape = RenderingServer.FogVolumeShape.Box, Material = material };
            AddChild(volume);
            _volumes.Add((volume, material));
        }
        return _volumes[index];
    }

    private void HideFrom(int index)
    {
        for (var i = index; i < _volumes.Count; i++)
            _volumes[i].Volume.Visible = false;
    }
}
