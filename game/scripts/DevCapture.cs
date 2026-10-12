using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Development aid for unattended checks. With user arguments after "--":
///   --capture=&lt;png path&gt;   save a screenshot after <c>--capture-after</c> seconds (default 3) and quit;
///   --fps-report           print the average frame rate over the capture window;
///   --focus=x,y            start the camera focused on Store position (x, y);
///   --distance=metres      start the camera at this zoom distance;
///   --pillars=name         (read by SimulationHost) start with this Pillar layout: 3x3, 2x2, 3x3L,
///                          3x3L-corners or 3x3L-diamond (the default);
///   --pillar=coordinate    focus beside a Pillar, by its grid coordinate (e.g. B-2), if the layout has it;
///   --pause                freeze Store time for comparable captures;
///   --pan=vx,vy            pan the camera focus at (vx, vy) metres per second, then report how
///                          often it crossed a seam and its largest per-frame step, overall
///                          and on the crossing frames (a jump would show as a big step there);
///   --no-focus-wrap        never shift the camera back onto the floor (to compare images of the same place);
///   --start-time=HH:MM     (read by SimulationHost) start the Store clock at this Store time;
///   --eye-level            look from a Villager's eye height (1.7 m) along the floor instead of
///                          from the Observer camera, to check the ground-level haze;
///   --enter-seed=text      after one second, enter this seed in the seed input; the capture timer
///                          restarts once the new Store is shown;
///   --choose-pillars=name  after one second, choose this Pillar layout in the top-right panel;
///                          the capture timer restarts once the new Store is shown.
///   --furniture-tour=dir  capture all second-wave Furniture and the four new Departments, then quit.
/// Does nothing in normal runs.
/// </summary>
public partial class DevCapture : Node
{
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public SeedPanel Seeds { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;
    [Export] public AmbientAudio Audio { get; set; } = null!;
    [Export] public LampLights Lamps { get; set; } = null!;

    private string? _tourDirectory;
    private string? _capturePath;
    private double _captureAfter = 3.0;
    private bool _fpsReport;
    private Vector3 _panVelocity;
    private double _elapsed;
    private int _frames;
    private int _seamCrossings;
    private float _largestFocusStep;
    private float _largestCrossingStep;
    private bool _crossedThisFrame;
    private Vector3 _lastFocus;
    private string? _seedToEnter;
    private PillarLayout? _layoutToChoose;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--furniture-tour="))
                _tourDirectory = arg["--furniture-tour=".Length..];
            else if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--capture-after="))
                _captureAfter = double.Parse(arg["--capture-after=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--fps-report")
                _fpsReport = true;
            else if (arg.StartsWith("--focus="))
                Rig.JumpTo(ParseFloorVector(arg["--focus=".Length..]));
            else if (arg.StartsWith("--distance="))
                Rig.JumpToDistance(float.Parse(arg["--distance=".Length..], CultureInfo.InvariantCulture));
            else if (arg.StartsWith("--pillar="))
            {
                var coordinate = arg["--pillar=".Length..];
                var pillar = Host.Snapshot.Pillars.SingleOrDefault(p => p.Coordinate == coordinate);
                if (pillar is null)
                {
                    GD.PushError($"No Pillar {coordinate} in the {Host.Snapshot.PillarLayout.Name()} layout; " +
                                 $"it has {string.Join(", ", Host.Snapshot.Pillars.Select(p => p.Coordinate))}.");
                    continue;
                }
                // Just south of its walkway, so the Pillar stands in front of the camera's default heading.
                var beside = pillar.Position + new System.Numerics.Vector2(0f, pillar.ClearHalfWidth + 12f);
                Rig.JumpTo(beside.ToWorld());
                GD.Print($"Capture Pillar {pillar.Coordinate}: {pillar.Position}, {pillar.Width:0} m wide, {pillar.Height:0} m tall, " +
                         $"focus {beside} in Sector {Host.Snapshot.SectorAt(beside).Name}.");
            }
            else if (arg.StartsWith("--pan="))
                _panVelocity = ParseFloorVector(arg["--pan=".Length..]);
            else if (arg == "--no-focus-wrap")
                Rig.WrapFocus = false;
            else if (arg == "--eye-level")
                AddEyeLevelCamera();
            else if (arg.StartsWith("--enter-seed="))
                _seedToEnter = arg["--enter-seed=".Length..];
            else if (arg.StartsWith("--choose-pillars="))
                _layoutToChoose = PillarLayouts.Parse(arg["--choose-pillars=".Length..]);
        }
        if (_tourDirectory is not null)
            CaptureFurnitureTour();
        _lastFocus = Rig.Focus;
        // Keep the step measurement continuous across the rig's whole-Store shifts.
        Rig.Recentred += shift =>
        {
            _seamCrossings++;
            _crossedThisFrame = true;
            _lastFocus += shift;
        };
        Host.WorldCreated += snapshot =>
        {
            GD.Print($"New Store shown: seed {snapshot.Seed}, {snapshot.PillarLayout.Name()} Pillars, {snapshot.Departments.All.Count} Departments, " +
                     $"{snapshot.Furniture.Count} pieces of Furniture, Store clock {snapshot.Clock.TimeOfDay:hh\\:mm}, " +
                     $"focus ({Rig.Focus.X:0.0}, {Rig.Focus.Z:0.0}) in Sector {snapshot.SectorAt(Rig.Focus.ToStore()).Name}.");
            _elapsed = 0.0;
            _frames = 0;
        };
        SetProcess(_capturePath is not null || _fpsReport || _panVelocity != Vector3.Zero || _seedToEnter is not null || _layoutToChoose is not null);
    }

    public override void _Process(double delta)
    {
        // Runs after the rig has moved this frame (CameraRig.ProcessPriority).
        var step = (Rig.Focus - _lastFocus).Length();
        _largestFocusStep = Mathf.Max(_largestFocusStep, step);
        if (_crossedThisFrame)
            _largestCrossingStep = Mathf.Max(_largestCrossingStep, step);
        _crossedThisFrame = false;
        _lastFocus = Rig.Focus;
        if (_panVelocity != Vector3.Zero)
            Rig.MoveTargetBy(_panVelocity * (float)delta);

        _elapsed += delta;
        _frames++;
        if (_seedToEnter is not null && _elapsed >= 1.0)
        {
            Seeds.Submit(_seedToEnter);
            _seedToEnter = null;
        }
        // Waits for a seed entered in the same run, which must be shown first.
        if (_layoutToChoose is { } chosen && _elapsed >= 1.0 && _seedToEnter is null && !Host.IsGenerating)
        {
            Seeds.ChooseLayout(chosen);
            _layoutToChoose = null;
        }
        if (_seedToEnter is not null || _layoutToChoose is not null || Host.IsGenerating)
            return;
        if (_elapsed < _captureAfter || (_capturePath is null && !_fpsReport))
            return;

        if (_fpsReport)
            GD.Print($"FPS average over {_elapsed:0.0}s: {_frames / _elapsed:0.0}");
        if (_panVelocity != Vector3.Zero)
            GD.Print($"Camera: {_seamCrossings} seam crossings, largest focus step {_largestFocusStep:0.000} m/frame " +
                     $"(on crossing frames {_largestCrossingStep:0.000} m), " +
                     $"focus now ({Rig.Focus.X:0.0}, {Rig.Focus.Z:0.0})");
        var clock = Host.Snapshot.Clock;
        GD.Print($"Store time: day {clock.Day} {clock.TimeOfDay:hh\\:mm}, {clock.Phase}. {Atmosphere.Describe()}");
        GD.Print($"Ambient audio: {Audio.Describe()}");
        GD.Print($"Lamps: {Lamps.Describe()}");
        var wind = Host.Snapshot.Wind;
        GD.Print($"Weather: {Host.Snapshot.FogBanks.Count} Fog banks, wind {wind.Length():0} m per Store hour, " +
                 $"fog density at the focus {Host.Snapshot.FogDensityAt(Rig.Focus.ToStore()):0.00}");
        if (_capturePath is not null)
        {
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(_capturePath);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {_capturePath}" : $"Screenshot failed: {error}");
        }
        GetTree().Quit();
    }

    private async void CaptureFurnitureTour()
    {
        try
        {
            Host.Speed = SimulationSpeed.Paused;
            Host.AdvanceTo(TimeSpan.FromHours(12));
            Host.HoldClearWeather();
            DirAccess.MakeDirRecursiveAbsolute(_tourDirectory);
            var snapshot = Host.Snapshot;
            var centre = new System.Numerics.Vector2(snapshot.Space.Size / 2f);
            var captures = new List<(string Name, System.Numerics.Vector2 Focus, float Distance, float Yaw)>();
            foreach (var type in Enum.GetValues<FurnitureType>().Where(t => t >= FurnitureType.Desk))
            {
                var piece = snapshot.Furniture.Where(p => p.Type == type)
                    .MinBy(p => snapshot.Space.Distance(p.Position, centre));
                captures.Add((type.ToString(), piece.Position, 8f, -piece.Rotation + Mathf.DegToRad(30f)));
                var full = FurnitureMeshes.For(type);
                var coarse = FurnitureMeshes.CoarseFor(type);
                if (full.GetSurfaceCount() == 0 || coarse.GetSurfaceCount() == 0)
                    throw new InvalidOperationException($"Missing full/coarse mesh for {type}.");
                GD.Print($"Tour {type}: {snapshot.Departments.At(piece.Position).Type}, {piece.Position}, " +
                         $"full/coarse surfaces {full.GetSurfaceCount()}/{coarse.GetSurfaceCount()}, " +
                         $"occluder vertices {FurnitureMeshes.OccluderQuads(type).Length}.");
            }
            foreach (var type in new[] { DepartmentType.Workspaces, DepartmentType.TextilesAndDecor,
                         DepartmentType.Outdoor, DepartmentType.DiningAndHallway })
            {
                var department = snapshot.Departments.All.Where(d => d.Type == type).MaxBy(d => d.Area)!;
                captures.Add(($"Department-{type}", department.Anchor, 45f, Mathf.DegToRad(30f)));
            }
            foreach (var capture in captures)
            {
                Rig.JumpTo(capture.Focus.ToWorld());
                Rig.JumpToDistance(capture.Distance);
                Rig.JumpToYaw(capture.Yaw);
                for (var frame = 0; frame < 45; frame++)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var path = System.IO.Path.Combine(_tourDirectory!, capture.Name + ".png");
                var error = GetViewport().GetTexture().GetImage().SavePng(path);
                if (error != Error.Ok)
                    throw new System.IO.IOException($"Capture {capture.Name}: {error}");
                GD.Print($"Screenshot saved to {path}");
            }
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"Furniture tour failed: {error}");
            GetTree().Quit(2);
        }
    }

    private void AddEyeLevelCamera()
    {
        var camera = new Camera3D { Fov = 70f, Far = 2000f };
        Rig.AddChild(camera);
        camera.Position = new Vector3(0f, 1.7f, 0f);
        camera.Rotation = new Vector3(Mathf.DegToRad(-3f), 0f, 0f);
        camera.MakeCurrent();
    }

    private static Vector3 ParseFloorVector(string text)
    {
        var parts = text.Split(',');
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture), 0f,
            float.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
