using Godot;

namespace Scp3008.Game;

/// <summary>
/// The Observer's angled top-down camera. Input only moves target values; the camera eases
/// towards them every frame, so pan, zoom and rotate stay smooth with no swoops or shake.
/// Keys: WASD/arrows pan, Q/E rotate, wheel or R/F zoom, V follows the next Villager (panning lets go). Mouse: middle-drag pan, right-drag rotate.
/// </summary>
public partial class CameraRig : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public Camera3D Camera { get; set; } = null!;

    [ExportGroup("Zoom")]
    [Export] public float MinDistance { get; set; } = 8f;
    [Export] public float MaxDistance { get; set; } = 220f;
    [Export] public float StartDistance { get; set; } = 45f;
    [Export] public float WheelZoomFactor { get; set; } = 1.15f;
    /// <summary>
    /// Where the focus starts, relative to the Store centre. In the 3 × 3 Pillar layout Pillar B-2
    /// stands at the centre (also in 3 × 3 large), so the Observer starts just south of its walkway,
    /// facing it; in 2 × 2 the centre is the corner where four Sectors meet, and in the corners and
    /// diamond layouts it is the empty Sector B-2, both with no Pillar.
    /// </summary>
    [Export] public Vector2 StartFocusOffset { get; set; } = new(0f, 48f);
    [Export] public float KeyZoomPerSecond { get; set; } = 2.2f;
    /// <summary>
    /// Farthest zoom in the <see cref="StoreOverview"/> for the standard Store: far enough to see it
    /// whole. It grows with the Store, so a larger Store still fits.
    /// </summary>
    [Export] public float OverviewMaxDistance { get; set; } = 2600f;
    /// <summary>Tilt in the <see cref="StoreOverview"/>, steeper than the Observer's, for a map-like view.</summary>
    [Export] public float OverviewTilt { get; set; } = 75f;
    /// <summary>The camera's far plane in the <see cref="StoreOverview"/>.</summary>
    [Export] public float OverviewFar { get; set; } = 8000f;

    [ExportGroup("Tilt (degrees from horizontal, within 50-60)")]
    [Export(PropertyHint.Range, "50,60")] public float TiltNear { get; set; } = 50f;
    [Export(PropertyHint.Range, "50,60")] public float TiltFar { get; set; } = 60f;

    [ExportGroup("Motion")]
    /// <summary>Pan speed in screen-heights per second, so it scales with zoom.</summary>
    [Export] public float PanSpeed { get; set; } = 0.9f;
    [Export] public float RotateDegreesPerSecond { get; set; } = 70f;
    [Export] public float MouseRotateDegreesPerPixel { get; set; } = 0.25f;
    /// <summary>How quickly the camera catches up with its targets (higher is snappier).</summary>
    [Export] public float Smoothing { get; set; } = 7f;

    /// <summary>
    /// Keep the focus near the Store floor by shifting the rig whole Store lengths back onto it once
    /// it has panned <see cref="RecentreStoreLengths"/> away, so world coordinates stay small enough
    /// for float precision. Wrapped content is placed around the focus, so the shift is invisible.
    /// </summary>
    [Export] public bool WrapFocus { get; set; } = true;

    /// <summary>
    /// How many Store lengths the focus may pan off the Store floor before the rig shifts back onto
    /// it. Every shift moves all wrapped content at once (a frame hitch of about 0.1 s), so this is
    /// far rather than at the first seam: the camera passes seams like any chunk edge, and even at
    /// the farthest zoom it takes over a minute of panning one way to get there. At 8 Store lengths
    /// (15 km) float precision is still about a millimetre.
    /// </summary>
    [Export] public int RecentreStoreLengths { get; set; } = 8;

    private Vector3 _targetFocus;
    private int? _following;
    private float _targetYaw;
    private float _targetDistance;
    private float _yaw;
    private float _distance;
    private bool _dragPanning;
    private bool _dragRotating;
    private float _storeSize;
    private float _normalFar;

    /// <summary>Raised with the shift whenever the rig jumps a whole Store length across a seam.</summary>
    public event Action<Vector3>? Recentred;

    /// <summary>The current zoom: the camera's distance from the focus, in metres.</summary>
    public float Distance => _distance;

    /// <summary>
    /// The point on the floor the camera looks at, unwrapped: within <see cref="RecentreStoreLengths"/>
    /// Store lengths of the Store floor while <see cref="WrapFocus"/> is on.
    /// </summary>
    public Vector3 Focus => Position;

    public override void _EnterTree()
    {
        // Run before the nodes that place wrapped content around the focus, so a recentring
        // and the matching content placement land in the same frame.
        ProcessPriority = -100;
    }

    public override void _Ready()
    {
        _storeSize = Host.Snapshot.Space.Size;
        _targetFocus = new Vector3(_storeSize / 2f + StartFocusOffset.X, 0f, _storeSize / 2f + StartFocusOffset.Y);
        Position = _targetFocus;
        _targetDistance = _distance = Mathf.Clamp(StartDistance, MinDistance, MaxDistance);
        _targetYaw = _yaw = Mathf.DegToRad(30f);
        _normalFar = Camera.Far;
        StoreOverview.Changed += OnOverviewChanged;
        Host.WorldCreated += OnWorldCreated;
        ApplyTransform();
    }

    public override void _ExitTree()
    {
        StoreOverview.Changed -= OnOverviewChanged;
        Host.WorldCreated -= OnWorldCreated;
    }

    /// <summary>
    /// A new world may be another size (the Pillar layout chooses it). The focus stays where it is;
    /// on a smaller Store the next wrap brings it back onto the floor.
    /// </summary>
    private void OnWorldCreated(Scp3008.Core.WorldSnapshot snapshot)
    {
        if (snapshot.Space.Size == _storeSize)
            return;
        _storeSize = snapshot.Space.Size;
        if (StoreOverview.Enabled)
            OnOverviewChanged(true);
    }

    private void OnOverviewChanged(bool enabled)
    {
        Camera.Far = enabled ? OverviewFar : _normalFar;
        if (enabled)
        {
            // Look at the whole Store from above its centre.
            _targetFocus = new Vector3(_storeSize / 2f, 0f, _storeSize / 2f);
            _targetDistance = OverviewDistanceLimit * 0.65f;
        }
        else
        {
            _targetDistance = ClampDistance(_targetDistance);
        }
    }

    /// <summary>The Villager the camera follows, if any.</summary>
    public int? Following => _following;

    /// <summary>Keeps the focus on a Villager as they walk, until <see cref="StopFollowing"/> or a pan.</summary>
    public void Follow(int villagerId) => _following = villagerId;

    public void StopFollowing() => _following = null;

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.V }:
                // The next Villager by id after the one followed; past the last, let go.
                var ids = Host.Snapshot.Villagers.Select(v => v.Id).Order().ToList();
                var next = ids.FirstOrDefault(id => _following is null || id > _following, -1);
                _following = next < 0 ? null : next;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _targetDistance = ClampDistance(_targetDistance / WheelZoomFactor);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _targetDistance = ClampDistance(_targetDistance * WheelZoomFactor);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
                _dragPanning = button.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } button:
                _dragRotating = button.Pressed;
                break;
            case InputEventMouseMotion motion when _dragPanning:
                // Grab-the-floor feel: the floor follows the cursor.
                var metresPerPixel = VisibleHeight() / GetViewport().GetVisibleRect().Size.Y;
                PanTargetBy(new Vector2(-motion.Relative.X, -motion.Relative.Y) * metresPerPixel);
                break;
            case InputEventMouseMotion motion when _dragRotating:
                _targetYaw -= Mathf.DegToRad(motion.Relative.X * MouseRotateDegreesPerPixel);
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        ReadKeyboard(dt);
        if (_following is { } followed)
        {
            // A Villager just placed is in the snapshot before the interpolation sees them.
            var villager = Host.InterpolatedVillagers.FirstOrDefault(v => v.Id == followed)
                           ?? Host.Snapshot.Villagers.FirstOrDefault(v => v.Id == followed);
            if (villager is null)
                _following = null;
            else
                FlyTo(villager.Position, _targetDistance);
        }

        // Frame-rate independent exponential easing towards the targets.
        var blend = 1f - Mathf.Exp(-Smoothing * dt);
        Position = Position.Lerp(_targetFocus, blend);
        _yaw = Mathf.Lerp(_yaw, _targetYaw, blend);
        _distance = Mathf.Lerp(_distance, _targetDistance, blend);
        if (StoreOverview.Enabled)
            ClampIntoStore();
        else if (WrapFocus)
            WrapIntoStore();
        ApplyTransform();
    }

    /// <summary>Eases the focus to the nearest image of a Store position and the zoom to a distance, as if panned and zoomed there.</summary>
    public void FlyTo(System.Numerics.Vector2 target, float distance)
    {
        var delta = Host.Snapshot.Space.Delta(_targetFocus.ToStore(), target);
        _targetFocus += new Vector3(delta.X, 0f, delta.Y);
        _targetDistance = ClampDistance(distance);
    }

    /// <summary>Moves the focus and its target instantly, with no easing (dev tools only).</summary>
    public void JumpTo(Vector3 focus)
    {
        Position = _targetFocus = focus with { Y = 0f };
        ApplyTransform();
    }

    /// <summary>Sets the zoom distance instantly, with no easing (dev tools only).</summary>
    public void JumpToDistance(float distance)
    {
        _targetDistance = _distance = ClampDistance(distance);
        ApplyTransform();
    }

    /// <summary>Sets the heading instantly, with no easing (dev tools only).</summary>
    public void JumpToYaw(float radians)
    {
        _targetYaw = _yaw = radians;
        ApplyTransform();
    }

    /// <summary>Moves the focus target by a world-space offset; the camera eases after it.</summary>
    public void MoveTargetBy(Vector3 offset) => _targetFocus += offset with { Y = 0f };

    private void WrapIntoStore()
    {
        var lengths = new Vector2(Mathf.Floor(Position.X / _storeSize), Mathf.Floor(Position.Z / _storeSize));
        if (Mathf.Abs(lengths.X) <= RecentreStoreLengths && Mathf.Abs(lengths.Y) <= RecentreStoreLengths)
            return;
        // Shift by whole Store lengths, applied to the eased focus and its target alike, so the
        // easing carries on undisturbed on the other side of the seam.
        var shift = new Vector3(-_storeSize * lengths.X, 0f, -_storeSize * lengths.Y);
        Position += shift;
        _targetFocus += shift;
        Recentred?.Invoke(shift);
    }

    /// <summary>In the overview the Store is drawn once, so the focus stays on it instead of wrapping.</summary>
    private void ClampIntoStore()
    {
        _targetFocus = new Vector3(Mathf.Clamp(_targetFocus.X, 0f, _storeSize), 0f, Mathf.Clamp(_targetFocus.Z, 0f, _storeSize));
        Position = new Vector3(Mathf.Clamp(Position.X, 0f, _storeSize), 0f, Mathf.Clamp(Position.Z, 0f, _storeSize));
    }

    private void ReadKeyboard(float dt)
    {
        // Typing in a text field (e.g. the seed input) must not move the camera.
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
            return;
        var pan = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += 1f;
        if (pan != Vector2.Zero)
            PanTargetBy(pan.Normalized() * PanSpeed * VisibleHeight() * dt);

        if (Input.IsKeyPressed(Key.Q)) _targetYaw += Mathf.DegToRad(RotateDegreesPerSecond) * dt;
        if (Input.IsKeyPressed(Key.E)) _targetYaw -= Mathf.DegToRad(RotateDegreesPerSecond) * dt;

        if (Input.IsKeyPressed(Key.R)) _targetDistance = ClampDistance(_targetDistance / Mathf.Pow(KeyZoomPerSecond, dt));
        if (Input.IsKeyPressed(Key.F)) _targetDistance = ClampDistance(_targetDistance * Mathf.Pow(KeyZoomPerSecond, dt));
    }

    /// <summary>Moves the focus target in screen-aligned floor directions (x right, y down the screen).</summary>
    private void PanTargetBy(Vector2 screenMetres)
    {
        var right = new Vector3(Mathf.Cos(_targetYaw), 0f, -Mathf.Sin(_targetYaw));
        var down = new Vector3(Mathf.Sin(_targetYaw), 0f, Mathf.Cos(_targetYaw));
        _following = null;
        // No clamping: the Store wraps, so the focus may pan across a seam.
        _targetFocus += right * screenMetres.X + down * screenMetres.Y;
    }

    private float ClampDistance(float distance) =>
        Mathf.Clamp(distance, MinDistance, StoreOverview.Enabled ? OverviewDistanceLimit : MaxDistance);

    private float OverviewDistanceLimit => OverviewMaxDistance * _storeSize / Scp3008.Core.StoreSpace.Standard.Size;

    private float Tilt(float distance)
    {
        if (StoreOverview.Enabled)
            return Mathf.DegToRad(OverviewTilt);
        var zoom = Mathf.InverseLerp(MinDistance, MaxDistance, distance);
        return Mathf.DegToRad(Mathf.Lerp(TiltNear, TiltFar, Mathf.Clamp(zoom, 0f, 1f)));
    }

    /// <summary>Approximate height of floor visible on screen at the focus, in metres.</summary>
    private float VisibleHeight() =>
        2f * _targetDistance * Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2f) / Mathf.Sin(Tilt(_targetDistance));

    private void ApplyTransform()
    {
        var tilt = Tilt(_distance);
        var offset = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt)) * _distance;
        Camera.Position = offset.Rotated(Vector3.Up, _yaw);
        Camera.LookAt(GlobalPosition, Vector3.Up);
    }
}
