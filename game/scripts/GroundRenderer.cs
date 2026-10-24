using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws the lasting properties of the Store's ground under Landmarks (<see cref="GroundProperty"/>) and the
/// time the clock wall's clocks run on. It hands the materials the dead zone's and the fountain court's rectangles
/// (global shader parameters in shaders/ground.gdshaderinc): Furniture and floor on dark ground get no light at all,
/// and the floor of the fountain court is wet. The dead zone is just that unlit square of ground: there is no
/// solid black volume over it. Each frame it sets the time left until Closing time for the clock hands.
/// View only: it reads the snapshot and never touches the simulation.
/// </summary>
public partial class GroundRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;

    public override void _Ready()
    {
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
    }

    /// <summary>The rectangle of the Landmark with this ground property (centre X and Z, half size); zero if there is none.</summary>
    private static Vector4 Rect(WorldSnapshot snapshot, GroundProperty property)
    {
        var landmark = snapshot.Landmarks.FirstOrDefault(l => l.Ground.HasFlag(property));
        return landmark is null ? Vector4.Zero : new Vector4(landmark.Centre.X, landmark.Centre.Y, landmark.Size.X / 2f, landmark.Size.Y / 2f);
    }
}
