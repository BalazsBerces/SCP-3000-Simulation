using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Development aid for unattended checks. With user arguments after "--":
///   --capture=&lt;png path&gt;   save a screenshot after <c>--capture-after</c> seconds (default 3) and quit;
///   --fps-report           print the average frame rate over the capture window;
///   --hitch-report         print every frame over the capture window that took over twice the
///                          median (and over 12 ms), with what happened in it: a chunk or seam
///                          crossing, a garbage collection;
///   --focus=x,y            start the camera focused on Store position (x, y);
///   --poi=Type             start the camera focused on the first Point of interest of that type (e.g. MannequinCrowd);
///   --distance=metres      start the camera at this zoom distance;
///   --overview-distance=metres  once the <see cref="StoreOverview"/> is on (--overview), jump to this zoom distance;
///   --pillars=name         (read by SimulationHost) start with this Pillar layout: 3x3, 2x2, 3x3L,
///                          3x3L-corners or 3x3L-diamond (the default);
///   --pillar=coordinate    focus beside a Pillar, by its grid coordinate (e.g. B-2), if the layout has it;
///   --pause                freeze Store time for comparable captures;
///   --shelf-stock=amount   after the focus jumps, set every Goods kind on the Stock Furniture around the focus to this
///                          amount (3 full, 0 bare), to capture the shelves' fill steps (ticket 08r);
///   --clear-weather        hold clear Weather for comparable captures (the air has no base fog);
///   --pan=vx,vy            pan the camera focus at (vx, vy) metres per second, then report how
///                          often it crossed a seam and its largest per-frame step, overall
///                          and on the crossing frames (a jump would show as a big step there);
///   --rotate=degrees       turn the camera at this many degrees per second;
///   --yaw=degrees          face the camera this way: 0 north, 90 west, 180 south, 270 east (also the --eye-level camera);
///   --no-focus-wrap        never shift the camera back onto the floor (to compare images of the same place);
///   --start-time=HH:MM     (read by SimulationHost) start the Store clock at this Store time;
///   --eye-level            look from a Villager's eye height (1.7 m) along the floor instead of
///                          from the Observer camera, to check the ground-level view;
///   --enter-seed=text      after one second, enter this seed in the seed input; the capture timer
///                          restarts once the new Store is shown;
///   --choose-pillars=name  after one second, choose this Pillar layout in the top-right panel;
///                          the capture timer restarts once the new Store is shown.
///   --show-credits         after one second, open the Credits dialog as the seed panel's Credits button does.
///   --inspect=x,y          (read by SectorInspector) once the overview is on, turn on Sector inspection and click the
///                          screen point over Store position (x, y), wrapped (e.g. -20,960 picks the Sector east of the west Seam).
///   --heatmap=Mode         (read by OverviewHeatmap) turn on the overview heatmap of Water, Food, Drink, Comfort or Supplies.
///   --furniture-tour=dir  save 1080p JPG captures into dir, then quit: every Department type
///                          (Department-*), every layout archetype (Archetype-*) and every
///                          Furniture variant (Variant-*-n).
///   --silhouette-check=file  compare every Furniture type's full and coarse silhouette
///                          (<see cref="SilhouetteCheck"/>), write the report to file and quit
///                          with exit code 0 if every type passes, 3 if not.
///   --coplanar-check=file  list every pair of overlapping visible box faces in one plane (they
///                          z-fight) over every Furniture type and variant, write it to file and quit
///                          with exit code 0 if there are none, 3 if not.
///   --coarse-colour-check=dir  compare every Furniture type's full and coarse colour per part
///                          (<see cref="CoarseColourCheck"/>), save side-by-side sheets and the
///                          report into dir and quit with exit code 0 if every part matches, 3 if not.
///   --model-check=file     check the external Furniture models (<see cref="ModelCheck"/>): each fills
///                          its Furniture size, keeps to its triangle budget, has a CC0 or CC-BY
///                          licence and is in CREDITS.md; write the report to file and quit with
///                          exit code 0 if every check passes, 3 if not.
///   --lod-debug            draw coarse Furniture meshes magenta (see <see cref="FurnitureDetail"/>);
///   --zoom-sweep=seconds   zoom slowly from 20 m inside the level-of-detail switch to 20 m beyond
///                          it and back over this many seconds, wobbling by 1.5 m like an unsteady
///                          hand, then report how often the level switched (2 means no flicker).
///   --zoom-to=metres       after three seconds, ease the zoom to this distance as the mouse wheel does
///                          (capture during it to see what moving the camera does to the view);
///   --zoom-in-test         zoom in fast with the mouse wheel, from the farthest zoom in the normal
///                          view and then in the <see cref="StoreOverview"/>, and report every frame
///                          that took over twice the median, with the zoom and level of detail then.
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
    private bool _hitchReport;
    private readonly List<(double Ms, string Events)> _frameLog = [];
    private ulong _lastFrameUsec;
    private (long X, long Y) _lastChunk;
    private int[] _lastCollections = [0, 0, 0];
    private Vector3 _panVelocity;
    private float _rotateDegreesPerSecond;
    private float? _yawDegrees;
    private Camera3D? _eyeCamera;
    private double _elapsed;
    private int _frames;
    private int _seamCrossings;
    private float _largestFocusStep;
    private float _largestCrossingStep;
    private bool _crossedThisFrame;
    private Vector3 _lastFocus;
    private string? _seedToEnter;
    private PillarLayout? _layoutToChoose;
    private double _zoomSweepSeconds;
    private float? _zoomTo;
    private int _detailSwitches;
    private float? _overviewDistance;
    private Vector3? _focusToJump;
    private float? _distanceToSet;
    private string? _poiName;
    private int? _shelfStock;
    // How far around the focus --shelf-stock reaches: farther shelves show no Goods (ShelfGoods.Distance) anyway.
    private const float ShelfStockRadius = 60f;

    public override void _Ready() => Guarded(Configure);

    public override void _Process(double delta) => Guarded(() => Advance(delta));

    // A check or capture that throws would leave Godot running forever (it only quits when the run
    // finishes), so in those runs report the error and quit with exit code 2. Normal runs are unchanged.
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (OS.GetCmdlineUserArgs().Length > 0)
        {
            GD.PushError($"DevCapture failed: {error}");
            GetTree().Quit(2);
        }
    }

    private void Configure()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--sandbox-check="))
            {
                SetProcess(false);
                CheckSandbox(arg["--sandbox-check=".Length..]);
                return;
            }
            if (arg.StartsWith("--silhouette-check="))
            {
                RunSilhouetteCheck(arg["--silhouette-check=".Length..]);
                return;
            }
            if (arg.StartsWith("--coplanar-check="))
            {
                var lines = Enum.GetValues<FurnitureType>()
                    .SelectMany(t => Enumerable.Range(0, FurnitureCatalog.VariantCount(t)).SelectMany(v => FurnitureMeshes.CoplanarFaces(t, v)))
                    .ToList();
                System.IO.File.WriteAllLines(arg["--coplanar-check=".Length..], lines);
                GD.Print($"{lines.Count} coplanar face pairs.");
                GetTree().Quit(lines.Count == 0 ? 0 : 3);
                return;
            }
            if (arg.StartsWith("--model-check="))
            {
                var (passed, lines) = ModelCheck.Run();
                System.IO.File.WriteAllLines(arg["--model-check=".Length..], lines);
                GD.Print(lines[^1]);
                foreach (var line in lines.Where(l => l.StartsWith("FAIL")))
                    GD.Print(line);
                GetTree().Quit(passed ? 0 : 3);
                return;
            }
            if (arg.StartsWith("--coarse-colour-check="))
            {
                RunColourCheck(arg["--coarse-colour-check=".Length..]);
                return;
            }
            if (arg.StartsWith("--furniture-tour="))
                _tourDirectory = arg["--furniture-tour=".Length..];
            else if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--capture-after="))
                _captureAfter = double.Parse(arg["--capture-after=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--fps-report")
                _fpsReport = true;
            else if (arg == "--hitch-report")
                _hitchReport = _fpsReport = true;
            else if (arg.StartsWith("--focus="))
            {
                _focusToJump = ParseFloorVector(arg["--focus=".Length..]);
                Rig.JumpTo(_focusToJump.Value);
            }
            else if (arg.StartsWith("--poi="))
            {
                _poiName = arg["--poi=".Length..];
                var poi = Host.Snapshot.PointsOfInterest.FirstOrDefault(p => p.Type.ToString() == _poiName);
                if (poi is not null)
                    Rig.JumpTo(new Vector3(poi.Centre.X, 0f, poi.Centre.Y));
            }
            else if (arg.StartsWith("--distance="))
            {
                _distanceToSet = float.Parse(arg["--distance=".Length..], CultureInfo.InvariantCulture);
                Rig.JumpToDistance(_distanceToSet.Value);
            }
            else if (arg.StartsWith("--overview-distance="))
                _overviewDistance = float.Parse(arg["--overview-distance=".Length..], CultureInfo.InvariantCulture);
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
            else if (arg.StartsWith("--rotate="))
                _rotateDegreesPerSecond = float.Parse(arg["--rotate=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--yaw="))
                _yawDegrees = float.Parse(arg["--yaw=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--no-focus-wrap")
                Rig.WrapFocus = false;
            else if (arg == "--eye-level")
                AddEyeLevelCamera();
            else if (arg.StartsWith("--shelf-stock="))
                _shelfStock = int.Parse(arg["--shelf-stock=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--clear-weather")
                Host.HoldClearWeather();
            else if (arg.StartsWith("--enter-seed="))
                _seedToEnter = arg["--enter-seed=".Length..];
            else if (arg == "--lod-debug")
                FurnitureMeshes.ShowCoarseInMagenta(true);
            else if (arg.StartsWith("--zoom-to="))
                _zoomTo = float.Parse(arg["--zoom-to=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--zoom-sweep="))
                _zoomSweepSeconds = double.Parse(arg["--zoom-sweep=".Length..], CultureInfo.InvariantCulture);
            else if (arg == "--zoom-in-test")
                CallDeferred(MethodName.RunZoomInTest);
            else if (arg == "--show-credits")
                GetTree().CreateTimer(1.0).Timeout += Seeds.ShowCredits;
            else if (arg.StartsWith("--choose-pillars="))
                _layoutToChoose = PillarLayouts.Parse(arg["--choose-pillars=".Length..]);
        }
        if (_yawDegrees is { } yaw)
        {
            Rig.JumpToYaw(Mathf.DegToRad(yaw));
            if (_eyeCamera is not null)
                _eyeCamera.Rotation = new Vector3(_eyeCamera.Rotation.X, Mathf.DegToRad(yaw), 0f);
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
        var level = FurnitureDetail.Coarse;
        FurnitureDetail.Changed += coarse =>
        {
            // Also raised when the start-up frames that draw both levels end.
            if (coarse != level)
                _detailSwitches++;
            level = coarse;
        };
        if (_zoomSweepSeconds > 0)
            _captureAfter = Math.Max(_captureAfter, _zoomSweepSeconds);
        SetProcess(_overviewDistance is not null || _zoomSweepSeconds > 0 || _capturePath is not null || _fpsReport || _panVelocity != Vector3.Zero || _seedToEnter is not null || _layoutToChoose is not null);
    }

    private void Advance(double delta)
    {
        // Runs after the rig has moved this frame (CameraRig.ProcessPriority).
        var step = (Rig.Focus - _lastFocus).Length();
        _largestFocusStep = Mathf.Max(_largestFocusStep, step);
        if (_crossedThisFrame)
            _largestCrossingStep = Mathf.Max(_largestCrossingStep, step);
        var crossed = _crossedThisFrame;
        _crossedThisFrame = false;
        _lastFocus = Rig.Focus;
        if (_overviewDistance is { } overviewDistance && StoreOverview.Enabled)
        {
            Rig.JumpToDistance(overviewDistance);
            _overviewDistance = null;
        }
        if (_panVelocity != Vector3.Zero)
            Rig.MoveTargetBy(_panVelocity * (float)delta);
        if (_rotateDegreesPerSecond != 0f)
            Rig.JumpToYaw(Mathf.DegToRad(30f + _rotateDegreesPerSecond * (float)_elapsed));
        if (_zoomTo is { } zoomTo && _elapsed >= 3.0)
        {
            Rig.FlyTo(Rig.Focus.ToStore(), zoomTo);
            _zoomTo = null;
        }
        if (_zoomSweepSeconds > 0 && _elapsed <= _zoomSweepSeconds)
        {
            // Out and back in, a triangle wave, with a wobble faster than the sweep.
            var t = _elapsed / _zoomSweepSeconds;
            var sweep = 1.0 - Math.Abs(2.0 * t - 1.0);
            var zoom = FurnitureDetail.SwitchZoom - 20.0 + 40.0 * sweep + 1.5 * Math.Sin(_elapsed * 9.0);
            Rig.JumpToDistance((float)zoom);
        }

        _elapsed += delta;
        _frames++;
        if (_hitchReport)
            LogFrame(crossed);
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
        if (_poiName is not null && _elapsed >= 1.0)
        {
            var poi = Host.Snapshot.PointsOfInterest.FirstOrDefault(p => p.Type.ToString() == _poiName);
            if (poi is null)
                GD.PrintErr($"No Point of interest {_poiName} in this Store.");
            else
                Rig.JumpTo(new Vector3(poi.Centre.X, 0f, poi.Centre.Y));
            _poiName = null;
        }
        if (_focusToJump is { } focus && _elapsed >= 1.0)
        {
            Rig.JumpTo(focus);
            _focusToJump = null;
        }
        // After the focus jumps above, so the shelves around the final focus are set.
        if (_shelfStock is { } amount && _elapsed >= 1.5)
        {
            Host.SetShelfStock(Rig.Focus.ToStore(), ShelfStockRadius, amount);
            _shelfStock = null;
        }
        if (_distanceToSet is { } dist && _elapsed >= 1.0)
        {
            Rig.JumpToDistance(dist);
            _distanceToSet = null;
        }
        if (_seedToEnter is not null || _layoutToChoose is not null || Host.IsGenerating)
            return;
        if (_elapsed < _captureAfter || (_capturePath is null && !_fpsReport && _zoomSweepSeconds == 0))
            return;

        if (_fpsReport)
            GD.Print($"FPS average over {_elapsed:0.0}s: {_frames / _elapsed:0.0}");
        if (_hitchReport)
            ReportHitches();
        if (_zoomSweepSeconds > 0)
            GD.Print($"Zoom sweep: {_frames} frames across the level-of-detail switch at {FurnitureDetail.SwitchZoom:0} m, " +
                     $"{_detailSwitches} switches, now {(FurnitureDetail.Coarse ? "coarse" : "full")} at zoom {Rig.Distance:0.0} m");
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
        GD.Print($"DevCapture focus: {Rig.Focus}, distance: {Rig.Distance}");
        if (_capturePath is not null)
        {
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(_capturePath);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {_capturePath}" : $"Screenshot failed: {error}");
        }
        GetTree().Quit();
    }

    private void LogFrame(bool crossed)
    {
        var now = Time.GetTicksUsec();
        var events = new List<string>();
        var chunkSize = Host.Snapshot.Space.ChunkSize;
        var chunk = ((long)Math.Floor(Rig.Focus.X / chunkSize), (long)Math.Floor(Rig.Focus.Z / chunkSize));
        if (chunk != _lastChunk)
            events.Add("chunk");
        if (crossed)
            events.Add("seam");
        for (var generation = 0; generation < 3; generation++)
        {
            var count = GC.CollectionCount(generation);
            if (count != _lastCollections[generation])
                events.Add($"GC{generation}");
            _lastCollections[generation] = count;
        }
        // Skip the first seconds: start-up, shader compilation and the first placements.
        if (_lastFrameUsec != 0 && _elapsed > 3.0)
            _frameLog.Add(((now - _lastFrameUsec) / 1000.0, string.Join(" ", events)));
        _lastFrameUsec = now;
        _lastChunk = chunk;
    }

    private void ReportHitches()
    {
        if (_frameLog.Count == 0)
            return;
        var sorted = _frameLog.Select(f => f.Ms).Order().ToList();
        var median = sorted[sorted.Count / 2];
        var hitches = _frameLog.Select((f, i) => (f.Ms, f.Events, Index: i)).Where(f => f.Ms > Math.Max(2 * median, 12.0)).ToList();
        GD.Print($"Hitches: {hitches.Count} of {_frameLog.Count} frames; median {median:0.0} ms, 99th percentile {sorted[(int)(sorted.Count * 0.99)]:0.0} ms, worst {sorted[^1]:0.0} ms");
        foreach (var (ms, events, index) in hitches.Take(40))
            GD.Print($"  frame {index}: {ms:0.0} ms {events} (previous frame: {(index > 0 ? _frameLog[index - 1].Events : "")})");
    }

    private async void RunZoomInTest()
    {
        try
        {
            Host.HoldClearWeather();
            await ZoomIn("Normal view", () => Rig.JumpToDistance(Rig.MaxDistance));
            StoreOverview.Set(true);
            // Wait out the overview's own zoom-out and the Furniture map.
            await Wait(6.0);
            await ZoomIn("Overview", () => Rig.JumpToDistance(float.MaxValue));
        }
        catch (Exception e)
        {
            GD.PushError($"Zoom-in test failed: {e}");
        }
        GetTree().Quit();
    }

    private async Task ZoomIn(string name, Action start)
    {
        start();
        await Wait(3.0);
        var frames = new List<(double Ms, float Distance, float Height, string Level)>();
        var level = () => FurnitureDetail.Map ? "map" : FurnitureDetail.Coarse ? "coarse" : "full";
        var last = Time.GetTicksUsec();
        for (var frame = 0; frame < 240; frame++)
        {
            // Two wheel notches a frame for the first 60 frames: a fast scroll in.
            if (frame < 60)
                for (var notch = 0; notch < 2; notch++)
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = GetViewport().GetVisibleRect().Size / 2f });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var now = Time.GetTicksUsec();
            frames.Add(((now - last) / 1000.0, Rig.Distance, Rig.Camera.GlobalPosition.Y,
                level()));
            last = now;
        }
        var median = frames.Select(f => f.Ms).Order().ElementAt(frames.Count / 2);
        GD.Print($"Zoom in, {name}: median frame {median:0.0} ms, worst {frames.Max(f => f.Ms):0.0} ms");
        for (var i = 0; i < frames.Count; i++)
            if (frames[i].Ms > 2 * median && frames[i].Ms > 12)
                GD.Print($"  frame {i}: {frames[i].Ms:0.0} ms at zoom {frames[i].Distance:0} m, height {frames[i].Height:0} m, {frames[i].Level}");
    }

    private async Task Wait(double seconds)
    {
        var until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (Time.GetTicksMsec() < until)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // Exercises the real debug selector and time-control buttons in a rendered sandbox.
    private async void CheckSandbox(string report)
    {
        var lines = new List<string>();
        try
        {
            await Wait(0.5);
            void Check(bool passed, string message)
            {
                if (!passed)
                    throw new InvalidOperationException(message);
                lines.Add($"PASS {message}");
            }
            var root = GetTree().CurrentScene;
            var selector = root.FindChild("SandboxPreset", true, false) as OptionButton;
            Check(selector is not null, "Sandbox preset selector is present");
            foreach (var name in ScenarioBuilder.VillagerSandboxPresets)
            {
                Host.Speed = SimulationSpeed.Paused;
                var before = Host.Snapshot;
                var index = ScenarioBuilder.VillagerSandboxPresets.ToList().IndexOf(name);
                selector!.Select(index);
                selector.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                var deadline = Time.GetTicksMsec() + 10000;
                while (ReferenceEquals(before, Host.Snapshot) && Time.GetTicksMsec() < deadline)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(!ReferenceEquals(before, Host.Snapshot) && Host.Snapshot.VillagerSandbox && Host.SandboxPreset == name,
                    $"Debug selector loads {name}");
                Check(Host.Snapshot.Villagers.Count == (name == "empty" ? 0 : 3), $"{name} replaces previous Villagers");
            }
            var controls = root.FindChildren("*", "Button", true, false).Cast<Button>().ToList();
            var tick = Host.Snapshot.Clock.Tick;
            controls.Single(b => b.Text == "Step").EmitSignal(Button.SignalName.Pressed);
            Check(Host.Speed == SimulationSpeed.Paused && Host.Snapshot.Clock.Tick == tick + 1, "Step pauses and advances one tick");
            controls.Single(b => b.Text == "60x").EmitSignal(Button.SignalName.Pressed);
            Check(Host.Speed == SimulationSpeed.Fast60, "60x button sets simulation speed");
            await Wait(1);
            Check(Host.Snapshot.Clock.Tick >= tick + 1000, "60x clock advances on the simulation thread");
            Host.Speed = SimulationSpeed.Paused;
            Rig.Follow(Host.Snapshot.Villagers[0].Id);
            Host.SendVillager(Host.Snapshot.Villagers[0].Id, new System.Numerics.Vector2(65, 112));
            Host.SkipTime(TimeSpan.FromMinutes(10));
            await Wait(2);
            Check(Host.Snapshot.Space.Distance(Rig.Focus.ToStore(), Host.Snapshot.Villagers[0].Position) < 0.5f,
                "Follow camera reaches walking Villager");
            lines.Add("Sandbox UI check passed.");
            System.IO.File.WriteAllLines(report, lines);
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add($"FAIL {error}");
            System.IO.File.WriteAllLines(report, lines);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
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
            GD.Print($"Tour Store {snapshot.Seed}: {snapshot.Furniture.Count} Furniture, " +
                     $"{snapshot.Furniture.Count(p => FurnitureCatalog.Spec(p).IsLamp)} Lamps, {snapshot.Displays.Count} Displays.");
            var centre = new System.Numerics.Vector2(snapshot.Space.Size / 2f);
            var captures = new List<(string Name, System.Numerics.Vector2 Focus, float Distance, float Yaw)>();
            foreach (var type in Enum.GetValues<DepartmentType>())
            {
                var department = snapshot.Departments.All.Where(d => d.Type == type).MaxBy(d => d.Area)
                    ?? throw new InvalidOperationException($"No Department of type {type}.");
                captures.Add(($"Department-{type}", department.Anchor, 45f, Mathf.DegToRad(30f)));
            }
            var cellSize = snapshot.Departments.CellSize;
            var side = snapshot.Departments.CellsPerSide;
            var piecesPerCell = new int[side * side];
            foreach (var piece in snapshot.Furniture)
                piecesPerCell[Math.Clamp((int)(piece.Position.Y / cellSize), 0, side - 1) * side
                              + Math.Clamp((int)(piece.Position.X / cellSize), 0, side - 1)]++;
            foreach (var archetype in Enum.GetValues<LayoutArchetype>())
            {
                var layout = snapshot.CellLayouts
                    .Where(c => c.Archetype == archetype && piecesPerCell[c.Y * side + c.X] >= 10)
                    .OrderBy(c => snapshot.Space.Distance(new((c.X + 0.5f) * cellSize, (c.Y + 0.5f) * cellSize), centre))
                    .Cast<CellLayout?>().FirstOrDefault()
                    ?? throw new InvalidOperationException($"No cell with archetype {archetype} and at least 10 pieces of Furniture.");
                captures.Add(($"Archetype-{archetype}", new((layout.X + 0.5f) * cellSize, (layout.Y + 0.5f) * cellSize), 24f, Mathf.DegToRad(30f)));
                GD.Print($"Tour {archetype}: {snapshot.Departments.AtCell(layout.X, layout.Y).Type}, {layout.Partition}, " +
                         $"cell ({layout.X}, {layout.Y}), {piecesPerCell[layout.Y * side + layout.X]} pieces.");
            }
            foreach (var type in Enum.GetValues<FurnitureType>().Where(t => FurnitureCatalog.VariantCount(t) > 1))
                for (var variant = 0; variant < FurnitureCatalog.VariantCount(type); variant++)
                {
                    var piece = snapshot.Furniture.Where(p => p.Type == type && p.Variant == variant)
                        .OrderBy(p => snapshot.Space.Distance(p.Position, centre)).Cast<FurniturePiece?>().FirstOrDefault()
                        ?? throw new InvalidOperationException($"No piece of {type} variant {variant}.");
                    captures.Add(($"Variant-{type}-{variant}", piece.Position, 6f, -piece.Rotation + Mathf.DegToRad(30f)));
                    var full = FurnitureMeshes.For(type, variant);
                    var coarse = FurnitureMeshes.CoarseFor(type, variant);
                    if (full.GetSurfaceCount() == 0 || coarse.GetSurfaceCount() == 0)
                        throw new InvalidOperationException($"Missing full/coarse mesh for {type} variant {variant}.");
                    GD.Print($"Tour {type} variant {variant}: {snapshot.Departments.At(piece.Position).Type}, {piece.Position}, " +
                             $"full/coarse surfaces {full.GetSurfaceCount()}/{coarse.GetSurfaceCount()}, " +
                             $"occluder vertices {FurnitureMeshes.OccluderQuads(type, variant).Length}.");
                }
            foreach (var capture in captures)
            {
                Rig.JumpTo(capture.Focus.ToWorld());
                Rig.JumpToDistance(capture.Distance);
                Rig.JumpToYaw(capture.Yaw);
                for (var frame = 0; frame < 45; frame++)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var path = System.IO.Path.Combine(_tourDirectory!, capture.Name + ".jpg");
                var error = GetViewport().GetTexture().GetImage().SaveJpg(path, 0.9f);
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

    private void RunSilhouetteCheck(string reportPath)
    {
        var results = SilhouetteCheck.All().ToList();
        var failed = results.Count(r => !r.Passed);
        var pieces = Host.Snapshot.Furniture.GroupBy(p => (p.Type, p.Variant)).ToDictionary(g => g.Key, g => g.Count());
        int Count(SilhouetteCheck.Result r) => pieces.GetValueOrDefault((r.Type, r.Variant));
        var lines = results.Select(r => $"{r}, {Count(r)} pieces in Store {Host.Snapshot.Seed}")
            .Append($"Store {Host.Snapshot.Seed}: {results.Sum(r => (long)r.FullTriangles * Count(r))} full and " +
                    $"{results.Sum(r => (long)r.CoarseTriangles * Count(r))} coarse triangles over every piece.")
            .Append($"{results.Count} Furniture types and variants, {failed} failed.").ToList();
        System.IO.File.WriteAllLines(reportPath, lines);
        GD.Print(lines[^1]);
        foreach (var line in lines.Where(l => l.StartsWith("FAIL")))
            GD.Print(line);
        GetTree().Quit(failed == 0 ? 0 : 3);
    }

    private async void RunColourCheck(string directory)
    {
        try
        {
            // Only the check's own world is drawn.
            GetViewport().Disable3D = true;
            var check = new CoarseColourCheck();
            AddChild(check);
            GetTree().Quit(await check.Run(directory) ? 0 : 3);
        }
        catch (Exception error)
        {
            GD.PushError($"Coarse colour check failed: {error}");
            GetTree().Quit(2);
        }
    }

    private void AddEyeLevelCamera()
    {
        // At eye level Furniture is always full.
        var camera = _eyeCamera = new Camera3D { Fov = 70f, Far = 2000f, CullMask = ~FurnitureDetail.CoarseLayer };
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
