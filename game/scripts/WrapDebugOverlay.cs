using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Dev overlay for checking the wrap. F3 (or the user argument <c>--wrap-debug</c>) toggles it.
/// It draws chunk boundaries (blue) and the seams (orange) on the floor, marker posts standing on
/// both seams with a tall red post at the corner, crates just inside each side of every seam
/// (teal on the east and south edges, violet on the west and north edges), and the focus
/// position and chunk. The markers are drawn through <see cref="WrappedInstances"/>, so they
/// show the ghost copies across the seams. F4 (or the user argument <c>--overview</c>) toggles the <see cref="StoreOverview"/>.
/// View only: it never touches the simulation.
/// </summary>
public partial class WrapDebugOverlay : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreFloor Floor { get; set; } = null!;

    private const float MarkerSpacing = 32f;

    private Label _label = null!;
    private Label _overviewLabel = null!;
    private StoreSpace _space = StoreSpace.Standard;
    private readonly List<WrappedInstances> _markers = [];

    public bool Enabled { get; private set; }

    public override void _Ready()
    {
        var hud = new CanvasLayer();
        AddChild(hud);
        _label = new Label
        {
            Name = "WrapInfo",
            AnchorTop = 1f, AnchorBottom = 1f, OffsetLeft = 16f, OffsetTop = -44f,
        };
        _label.AddThemeFontSizeOverride("font_size", 18);
        hud.AddChild(_label);
        _overviewLabel = new Label
        {
            Name = "OverviewInfo",
            AnchorTop = 1f, AnchorBottom = 1f, OffsetLeft = 16f, OffsetTop = -76f,
            Visible = false,
        };
        _overviewLabel.AddThemeFontSizeOverride("font_size", 18);
        hud.AddChild(_overviewLabel);

        BuildMarkers(Host.Snapshot);
        Host.WorldCreated += BuildMarkers;
        SetEnabled(OS.GetCmdlineUserArgs().Contains("--wrap-debug"));
        if (OS.GetCmdlineUserArgs().Contains("--overview"))
            CallDeferred(MethodName.ToggleOverview);
    }

    public override void _ExitTree() => Host.WorldCreated -= BuildMarkers;

    /// <summary>Places the seam markers for the world's Store; a new world may be another size.</summary>
    private void BuildMarkers(WorldSnapshot snapshot)
    {
        if (_markers.Count > 0 && snapshot.Space == _space)
            return;
        _space = snapshot.Space;
        foreach (var markers in _markers)
            markers.QueueFree();
        _markers.Clear();

        var size = _space.Size;
        var alongSeam = Enumerable.Range(0, (int)(size / MarkerSpacing)).Select(i => (i + 0.5f) * MarkerSpacing).ToArray();
        AddMarkers(new BoxMesh { Size = new Vector3(0.5f, 4f, 0.5f) }, new Color(1f, 0.45f, 0.1f),
            alongSeam.SelectMany(t => new[] { new Vector3(0f, 2f, t), new Vector3(t, 2f, 0f) }));
        AddMarkers(new BoxMesh { Size = new Vector3(1.2f, 12f, 1.2f) }, new Color(0.9f, 0.1f, 0.1f),
            [new Vector3(0f, 6f, 0f)]);
        AddMarkers(new BoxMesh { Size = new Vector3(1.5f, 1.5f, 1.5f) }, new Color(0.1f, 0.7f, 0.7f),
            alongSeam.SelectMany(t => new[] { new Vector3(size - 3f, 0.75f, t), new Vector3(t, 0.75f, size - 3f) }));
        AddMarkers(new BoxMesh { Size = new Vector3(1.5f, 1.5f, 1.5f) }, new Color(0.55f, 0.25f, 0.85f),
            alongSeam.SelectMany(t => new[] { new Vector3(3f, 0.75f, t), new Vector3(t, 0.75f, 3f) }));
        _overviewLabel.Text = $"Store overview: {size:0} m x {size:0} m, drawn once with no wrap, haze or far cut-off    F4 returns";
    }

    private void ToggleOverview()
    {
        StoreOverview.Set(!StoreOverview.Enabled);
        _overviewLabel.Visible = StoreOverview.Enabled;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            SetEnabled(!Enabled);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F4 })
        {
            ToggleOverview();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var focus = Rig.Focus.ToStore();
        var chunk = _space.ChunkOf(focus);
        _label.Text = $"Focus  x {focus.X:0.0}  y {focus.Y:0.0}    chunk ({chunk.X}, {chunk.Y})    F3 hides";
    }

    private void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Visible = enabled;
        _label.Visible = enabled;
        SetProcess(enabled);
        Floor.FloorMaterial.SetShaderParameter("show_chunks", enabled);
    }

    private void AddMarkers(PrimitiveMesh mesh, Color color, IEnumerable<Vector3> positions)
    {
        mesh.Material = new StandardMaterial3D { AlbedoColor = color };
        var markers = new WrappedInstances { Rig = Rig, Mesh = mesh };
        AddChild(markers);
        _markers.Add(markers);
        markers.SetInstances(_space, positions.Select(p => new Transform3D(Basis.Identity, p)));
    }
}
