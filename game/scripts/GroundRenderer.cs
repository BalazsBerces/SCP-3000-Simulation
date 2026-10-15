using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws the lasting properties of the Store's ground under Landmarks (<see cref="GroundProperty"/>) and the
/// time the clock wall's clocks run on. It hands the materials the dead zone's and the fountain court's rectangles
/// (global shader parameters in shaders/ground.gdshaderinc): Furniture and floor on dark ground get no light at all,
/// and the floor of the fountain court is wet. Over the dead zone it stands a column of dark air, black and
/// sharp-edged, at every wrapped image within Landmark reach, so from afar the dead zone reads as a black hole
/// and the darkness hides the ceiling height above it too. Its faces are drawn from outside only, so from inside
/// the dead zone the lit Store around it shows. Each frame it sets the time left until Closing time for the clock hands.
/// View only: it reads the snapshot and never touches the simulation.
/// </summary>
public partial class GroundRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreVisibility Visibility { get; set; } = null!;

    /// <summary>Height of the dead zone's column of dark air, in metres: well above its towers and the Fog banks.</summary>
    [Export] public float DarkAirHeight { get; set; } = 60f;

    private WrappedInstances _darkAir = null!;

    public override void _Ready()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dark_air.gdshader") };
        _darkAir = new WrappedInstances
        {
            Name = "DarkAir",
            Rig = Rig,
            FarDistance = Visibility.LandmarkDistance,
            ViewReach = Visibility.LandmarkPlacementReach(Rig, Host.Snapshot.Space.ChunkSize),
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_darkAir);
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    public override void _Process(double delta)
    {
        var timeOfDay = TimeSpan.FromTicks(Host.InterpolatedStoreTime.Ticks % TimeSpan.TicksPerDay);
        var toClosing = (StoreHours.ClosingTimeStarts - timeOfDay).TotalSeconds;
        RenderingServer.GlobalShaderParameterSet("time_to_closing", (float)(toClosing < 0 ? toClosing + 86400 : toClosing));
    }

    private void Rebuild(WorldSnapshot snapshot)
    {
        RenderingServer.GlobalShaderParameterSet("ground_store_size", snapshot.Space.Size);
        RenderingServer.GlobalShaderParameterSet("ground_dark_rect", Rect(snapshot, GroundProperty.Darkness));
        RenderingServer.GlobalShaderParameterSet("ground_water_rect", Rect(snapshot, GroundProperty.Water));
        _darkAir.SetInstances(snapshot.Space, snapshot.Landmarks.Where(l => l.Ground.HasFlag(GroundProperty.Darkness))
            .Select(l => new Transform3D(Basis.FromScale(new Vector3(l.Size.X, DarkAirHeight, l.Size.Y)), l.Centre.ToWorld(DarkAirHeight / 2f))));
    }

    /// <summary>The rectangle of the Landmark with this ground property (centre X and Z, half size); zero if there is none.</summary>
    private static Vector4 Rect(WorldSnapshot snapshot, GroundProperty property)
    {
        var landmark = snapshot.Landmarks.FirstOrDefault(l => l.Ground.HasFlag(property));
        return landmark is null ? Vector4.Zero : new Vector4(landmark.Centre.X, landmark.Centre.Y, landmark.Size.X / 2f, landmark.Size.Y / 2f);
    }
}
