using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// In the <see cref="StoreOverview"/>, zoomed far out, a letter over each Landmark (V Ruined Village, C Rack canyon,
/// B Box fort, M Mattress mountain, D Dead zone, K Clock wall, F Fountain court, G Camping ground), kept the
/// same size on screen however far the camera is, and a smaller one over each Water point (W blue: always there, T green: a running
/// showroom tap, t grey: a dry one; dev only, the presentation itself never tells them apart). The markers are rebuilt when a new world is created.
/// </summary>
public partial class LandmarkMarkers : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    /// <summary>The camera distance from which the letters show; closer, the Landmarks can be read off the Furniture.</summary>
    [Export] public float ShowFromDistance { get; set; } = 600f;
    /// <summary>The letter's height as a share of the screen height.</summary>
    [Export] public float ScreenShare { get; set; } = 0.05f;

    private const int FontSize = 128;
    private const float WaterScale = 0.3f;

    private readonly List<Label3D> _labels = [];

    public override void _Ready()
    {
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
        Visible = false;
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    public override void _Process(double delta)
    {
        Visible = StoreOverview.Enabled && Rig.Distance >= ShowFromDistance;
        if (!Visible)
            return;
        // The view is 2 tan(fov / 2) distances high at a distance.
        var pixelSize = Rig.Distance * 2f * Mathf.Tan(Mathf.DegToRad(Rig.Camera.Fov) / 2f) * ScreenShare / FontSize;
        foreach (var label in _labels)
            label.PixelSize = pixelSize * (label.FontSize == FontSize ? 1f : WaterScale);
    }

    private void Rebuild(WorldSnapshot snapshot)
    {
        foreach (var label in _labels)
            label.QueueFree();
        _labels.Clear();
        foreach (var landmark in snapshot.Landmarks)
        {
            var label = new Label3D
            {
                Text = Letter(landmark.Type),
                FontSize = FontSize,
                OutlineSize = 24,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Shaded = false,
                FixedSize = false,
                Modulate = new Color(1f, 0.9f, 0.3f),
                OutlineModulate = new Color(0f, 0f, 0f),
                Position = new Vector3(landmark.Centre.X, landmark.Height, landmark.Centre.Y),
            };
            AddChild(label);
            _labels.Add(label);
        }
        foreach (var water in snapshot.WaterPoints)
        {
            var tap = water.Kind == WaterPointKind.ShowroomTap;
            var label = new Label3D
            {
                Text = !tap ? "W" : water.Running ? "T" : "t",
                FontSize = FontSize - 1, // told apart from the Landmark letters by size
                OutlineSize = 24,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Shaded = false,
                Modulate = !tap ? new Color(0.3f, 0.6f, 1f) : water.Running ? new Color(0.3f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f),
                OutlineModulate = new Color(0f, 0f, 0f),
                Position = new Vector3(water.Position.X, 3f, water.Position.Y),
            };
            AddChild(label);
            _labels.Add(label);
        }
    }

    private static string Letter(LandmarkType type) => type switch
    {
        LandmarkType.RuinedVillage => "V",
        LandmarkType.RackCanyon => "C",
        LandmarkType.BoxFort => "B",
        LandmarkType.MattressMountain => "M",
        LandmarkType.DeadZone => "D",
        LandmarkType.ClockWall => "K",
        LandmarkType.FountainCourt => "F",
        LandmarkType.CampingGround => "G",
        _ => "?",
    };
}
