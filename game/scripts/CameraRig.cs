using Godot;

namespace Scp3008.Game;

/// <summary>
/// The Observer's angled top-down camera. Input only moves target values; the camera eases
/// towards them every frame, so pan, zoom and rotate stay smooth with no swoops or shake.
/// Keys: WASD/arrows pan, Q/E rotate, wheel or R/F zoom. Mouse: middle-drag pan, right-drag rotate.
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
    [Export] public float KeyZoomPerSecond { get; set; } = 2.2f;

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
    /// Keep the focus on the Store floor by shifting the rig a whole Store length when it pans
    /// across a seam. Wrapped content is placed around the focus, so the shift is invisible.
    /// </summary>
    [Export] public bool WrapFocus { get; set; } = true;

    private Vector3 _targetFocus;
    private float _targetYaw;
    private float _targetDistance;
    private float _yaw;
    private float _distance;
    private bool _dragPanning;
    private bool _dragRotating;
    private float _storeSize;

    /// <summary>Raised with the shift whenever the rig jumps a whole Store length across a seam.</summary>
    public event Action<Vector3>? Recentred;

    /// <summary>The point on the floor the camera looks at, always on the floor while <see cref="WrapFocus"/> is on.</summary>
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
        _targetFocus = new Vector3(_storeSize / 2f, 0f, _storeSize / 2f);
        Position = _targetFocus;
        _targetDistance = _distance = Mathf.Clamp(StartDistance, MinDistance, MaxDistance);
        _targetYaw = _yaw = Mathf.DegToRad(30f);
        ApplyTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
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

        // Frame-rate independent exponential easing towards the targets.
        var blend = 1f - Mathf.Exp(-Smoothing * dt);
        Position = Position.Lerp(_targetFocus, blend);
        _yaw = Mathf.Lerp(_yaw, _targetYaw, blend);
        _distance = Mathf.Lerp(_distance, _targetDistance, blend);
        if (WrapFocus)
            WrapIntoStore();
        ApplyTransform();
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
        // Shift by whole Store lengths, applied to the eased focus and its target alike, so the
        // easing carries on undisturbed on the other side of the seam.
        var shift = new Vector3(
            -_storeSize * Mathf.Floor(Position.X / _storeSize), 0f,
            -_storeSize * Mathf.Floor(Position.Z / _storeSize));
        if (shift == Vector3.Zero)
            return;
        Position += shift;
        _targetFocus += shift;
        Recentred?.Invoke(shift);
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
        // No clamping: the Store wraps, so the focus may pan across a seam.
        _targetFocus += right * screenMetres.X + down * screenMetres.Y;
    }

    private float ClampDistance(float distance) => Mathf.Clamp(distance, MinDistance, MaxDistance);

    private float Tilt(float distance)
    {
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
