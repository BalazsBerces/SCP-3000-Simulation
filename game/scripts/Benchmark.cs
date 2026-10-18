using System.Globalization;
using Godot;

namespace Scp3008.Game;

/// <summary>
/// The fixed benchmark scene (Benchmark.tscn): the Main scene on Store seed 3008 with the clock
/// paused, and the camera flying the same path twice: once at noon in open lighting and once at
/// Closing time. The path starts close over the lamp displays of a Lighting &amp; electrical
/// Department (the shadow budget at work), pulls out to the farthest zoom, and pans and turns
/// there (the most Furniture in view). The far view is also held at the farthest zoom and, if
/// Furniture turns coarse within the zoom range, at the switch zoom with every piece full. Vsync is off. For each lighting it prints the average and
/// 1%-low frame rate (the mean rate over the slowest 1% of frames), with the average GPU and CPU
/// render time, draw calls and primitives per frame, and quits with exit code 0 if
/// every average meets <see cref="MinimumFps"/> and every 1% low meets <see cref="MinimumOnePercentLowFps"/>,
/// 1 if not; each measurement also says whether it reached the <see cref="TargetFps"/> aimed for. User arguments after "--":
///   --benchmark-seconds=s  measuring time per lighting (default 20);
///   --benchmark-shots=dir  save near, mid and far screenshots in each lighting;
///   --lamp-reproduction=dir  capture a continuous pan, rotation and zoom over the Lamp displays;
///   --benchmark-overview  measure only the <see cref="StoreOverview"/>, held over the Store centre at
///   fixed camera heights at Closing time.
/// </summary>
public partial class Benchmark : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;
    [Export] public LampLights Lamps { get; set; } = null!;

    /// <summary>The frame rate aimed for (ADR 0004); reported per measurement, but missing it does not fail the run.</summary>
    [Export] public int TargetFps { get; set; } = 120;
    /// <summary>The gate: an average below this fails the run.</summary>
    [Export] public int MinimumFps { get; set; } = 90;
    [Export] public int MinimumOnePercentLowFps { get; set; } = 60;
    /// <summary>Long enough for the one-off stall of about 0.7 s that comes some 3.7 s into the first lighting (not seen in the game itself) to fall in the warm-up.</summary>
    [Export] public double WarmUpSeconds { get; set; } = 6.0;
    [Export] public double MeasureSeconds { get; set; } = 20.0;

    /// <summary>Where the path starts: the lamp displays near the middle of Store 3008.</summary>
    [Export] public Vector2 Start { get; set; } = new(676f, 710f);

    private static readonly Vector2I Resolution = new(1920, 1080);

    private string? _shotsDirectory;
    private string? _reproductionDirectory;
    private bool _overviewOnly;
    private double? _progress;
    private float? _holdZoom;

    // HoldZoom: hold the far point of the path at this zoom, approached from closer, as zooming out.
    // OverviewHeight: hold the StoreOverview over the Store centre with the camera this high, in metres.
    private sealed record Lighting(string Name, TimeSpan TimeOfDay, bool HoldFar = false, float? HoldZoom = null, float? OverviewHeight = null);

    /// <summary>Camera heights the overview is measured at: with meshes, just below and above the map switch, at 877 m, and highest.</summary>
    private static readonly float[] OverviewHeights = [150f, 230f, 270f, 500f, 877f, 2400f];

    private sealed record Result(Lighting Lighting, int Frames, double Seconds, double AverageFps, double OnePercentLowFps, int MostLights, int MostShadowed, int MostVisibleLamps,
        double GpuMilliseconds, double CpuMilliseconds, double DrawCalls, double Primitives);

    public override void _EnterTree()
    {
        // Fly the camera before the rig and everything placed around it process the frame.
        ProcessPriority = -200;
    }

    public override void _Process(double delta)
    {
        if (_progress is { } progress)
            Fly(progress);
    }

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--benchmark-seconds="))
                MeasureSeconds = double.Parse(arg["--benchmark-seconds=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--benchmark-shots="))
                _shotsDirectory = arg["--benchmark-shots=".Length..];
            else if (arg.StartsWith("--lamp-reproduction="))
                _reproductionDirectory = arg["--lamp-reproduction=".Length..];
            else if (arg == "--benchmark-overview")
                _overviewOnly = true;
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        DisplayServer.WindowSetSize(Resolution);
        Run();
    }

    private async void Run()
    {
        try
        {
            await RunBothLightings();
        }
        catch (Exception e)
        {
            GD.PushError($"Benchmark failed: {e}");
            GetTree().Quit(2);
        }
    }

    private async Task RunBothLightings()
    {
        Host.Speed = Scp3008.Core.SimulationSpeed.Paused;
        var results = new List<Result>();
        if (_overviewOnly)
        {
            Host.AdvanceTo(TimeSpan.FromHours(23));
            Host.HoldClearWeather();
            StoreOverview.Set(true);
            foreach (var height in OverviewHeights)
                results.Add(await Measure(new Lighting($"Overview at {height:0} m", TimeSpan.FromHours(23), OverviewHeight: height)));
            StoreOverview.Set(false);
            Report(results);
            return;
        }
        foreach (var lighting in new[] { new Lighting("Open", TimeSpan.FromHours(12)), new Lighting("Closing time", TimeSpan.FromHours(23)) })
        {
            Host.AdvanceTo(lighting.TimeOfDay);
            // Fix the weather after each time skip so both lightings measure the same bank.
            // The bank covers the near view and stays in view as the camera pulls out.
            Host.HoldClearWeather();
            Host.PlaceFogBank(new System.Numerics.Vector2(Start.X + 50f, Start.Y + 20f), 160f);
            if (_reproductionDirectory is not null)
                await Reproduce(lighting);
            results.Add(await Measure(lighting));
            if (_shotsDirectory is not null)
                await Screenshots(lighting);
        }

        // A path average alone can hide an expensive far view behind faster close frames.
        results.Add(await Measure(new Lighting("Closing time far", TimeSpan.FromHours(23), HoldFar: true)));
        // The farthest zoom with full Furniture: every visible piece full (ticket 06f).
        if (FurnitureDetail.SwitchZoom < Rig.MaxDistance)
            results.Add(await Measure(new Lighting("Closing time at LOD switch", TimeSpan.FromHours(23), HoldFar: true,
                HoldZoom: FurnitureDetail.SwitchZoom)));
        Report(results);
    }

    private void Report(List<Result> results)
    {
        var size = GetViewport().GetVisibleRect().Size;
        GD.Print($"Benchmark: Store {Host.Snapshot.Seed}, {size.X}x{size.Y}, {RenderingServer.GetVideoAdapterName()}, " +
                 $"{MeasureSeconds:0} s per lighting, target {TargetFps} fps average, " +
                 $"minimum {MinimumFps} fps average and {MinimumOnePercentLowFps} fps 1% low");
        GD.Print($"  Weather: fixed Fog bank, radius 160 m; MSAA {GetViewport().Msaa3D}, TAA {GetViewport().UseTaa}; " +
                 $"Furniture full up to a zoom of {FurnitureDetail.SwitchZoom:0} m, map above an overview camera height of {FurnitureDetail.MapHeight:0} m");
        foreach (var r in results)
            GD.Print($"  {r.Lighting.Name,-26} average {r.AverageFps,6:0.0} fps, 1% low {r.OnePercentLowFps,6:0.0} fps " +
                     $"[{(r.AverageFps >= TargetFps ? "meets" : "below")} {TargetFps} target] " +
                     $"({r.Frames} frames in {r.Seconds:0.0} s; up to {r.MostLights} lamp lights, {r.MostShadowed} shadowed, {r.MostVisibleLamps} visible Lamps with halos; " +
                     $"average GPU {r.GpuMilliseconds:0.00} ms, CPU render {r.CpuMilliseconds:0.00} ms, {r.DrawCalls:0} draw calls, {r.Primitives / 1e6:0.0} M primitives)");
        var atResolution = (Vector2I)size == Resolution;
        var antialiasingEnabled = GetViewport().Msaa3D == Viewport.Msaa.Msaa4X && GetViewport().UseTaa;
        var passed = atResolution && antialiasingEnabled && results.All(r => r.AverageFps >= MinimumFps && r.OnePercentLowFps >= MinimumOnePercentLowFps);
        GD.Print(passed ? "Benchmark PASSED" :
            !atResolution ? $"Benchmark FAILED: not rendered at {Resolution.X}x{Resolution.Y}" :
            !antialiasingEnabled ? "Benchmark FAILED: MSAA 4x and TAA are required" : "Benchmark FAILED: below minimum");
        GetTree().Quit(passed ? 0 : 1);
    }

    private async Task<Result> Measure(Lighting lighting)
    {
        // Warm up on the path's first view: shaders compile, the light settles after the time skip.
        var warmUpEnds = Now + WarmUpSeconds;
        while (Now < warmUpEnds)
        {
            if (lighting.OverviewHeight is { } warmUpHeight)
            {
                HoldOverview(warmUpHeight);
                await NextFrame();
                continue;
            }
            _progress = lighting.HoldFar ? 0.8 : 0.0;
            _holdZoom = lighting.HoldZoom is not null && Now < warmUpEnds - WarmUpSeconds / 2 ? Rig.MinDistance : lighting.HoldZoom;
            await NextFrame();
        }

        var frameSeconds = new List<double>();
        var (mostLights, mostShadowed, mostVisible) = (0, 0, 0);
        var viewport = GetViewport();
        RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
        var (gpu, cpu, drawCalls, primitives) = (0.0, 0.0, 0.0, 0.0);
        var start = Now;
        var last = start;
        while (last - start < MeasureSeconds)
        {
            if (lighting.OverviewHeight is { } height)
                HoldOverview(height);
            else
                _progress = lighting.HoldFar ? 0.8 : (last - start) / MeasureSeconds;
            await NextFrame();
            var now = Now;
            frameSeconds.Add(now - last);
            last = now;
            mostLights = Math.Max(mostLights, Lamps.ActiveLights);
            mostShadowed = Math.Max(mostShadowed, Lamps.ShadowedLights);
            mostVisible = Math.Max(mostVisible, Lamps.VisibleLamps);
            gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());
            cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid());
            drawCalls += viewport.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)
                         + viewport.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.DrawCallsInFrame);
            primitives += viewport.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame)
                          + viewport.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame);
            if (Lamps.ActiveLights > Lamps.MaxLights || Lamps.ShadowedLights > Lamps.ShadowBudget)
                throw new InvalidOperationException("Lamp lights exceeded the ADR 0004 budget.");
        }

        _holdZoom = null;
        if (lighting.HoldZoom is not null && FurnitureDetail.Coarse)
            throw new InvalidOperationException("Furniture was coarse at the LOD switch zoom.");
        var seconds = last - start;
        var slowest = frameSeconds.OrderByDescending(s => s).Take(Math.Max(1, frameSeconds.Count / 100)).ToList();
        return new Result(lighting, frameSeconds.Count, seconds, frameSeconds.Count / seconds,
            slowest.Count / slowest.Sum(), mostLights, mostShadowed, mostVisible,
            gpu / frameSeconds.Count, cpu / frameSeconds.Count, drawCalls / frameSeconds.Count, primitives / frameSeconds.Count);
    }

    private async Task Screenshots(Lighting lighting)
    {
        DirAccess.MakeDirRecursiveAbsolute(_shotsDirectory);
        foreach (var (at, name) in new[] { (0.0, "close"), (0.45, "mid"), (0.8, "far") })
        {
            for (var i = 0; i < 30; i++)
            {
                _progress = at;
                await NextFrame();
            }
            var path = Path.Combine(_shotsDirectory!, $"{lighting.Name.Replace(' ', '-').ToLowerInvariant()}-{name}.png");
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
        }
    }

    // A paused, fixed bank and a continuous pan, rotation and zoom (near to farthest) across the
    // same Lamp displays. Capture after rendering, so evidence corresponds to the camera and light
    // selection logged.
    private async Task Reproduce(Lighting lighting)
    {
        _progress = null;
        var tracked = Host.Snapshot.Furniture.Where(p => Scp3008.Core.FurnitureCatalog.Spec(p).IsLamp)
            .MinBy(p => Host.Snapshot.Space.Distance(p.Position, new System.Numerics.Vector2(Start.X, Start.Y))).Id;
        DirAccess.MakeDirRecursiveAbsolute(_reproductionDirectory);
        for (var frame = -30; frame <= 240; frame++)
        {
            var t = Math.Max(0, frame) / 240f;
            Rig.JumpTo(new Vector3(Start.X - 24f + 48f * t, 0f, Start.Y));
            Rig.JumpToDistance(Rig.MinDistance * Mathf.Pow(Rig.MaxDistance / Rig.MinDistance, t));
            Rig.JumpToYaw(Mathf.DegToRad(10f + 40f * t));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (frame < 0 || frame % 10 != 0)
                continue;
            var path = Path.Combine(_reproductionDirectory!, $"{lighting.Name.Replace(' ', '-').ToLowerInvariant()}-{frame / 10:00}.png");
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok)
                throw new IOException($"Reproduction capture failed: {error}");
            GD.Print($"Reproduction {frame}: focus {Rig.Focus}, {Lamps.DescribeLamp(tracked)}, {Lamps.Describe()}, saved {path}");
        }
    }

    /// <summary>
    /// Puts the camera at a point of the fixed path, from 0 (start) to 1 (end): close over the lamp
    /// displays, then out to the farthest zoom while panning and turning.
    /// </summary>
    private void Fly(double progress)
    {
        var t = (float)Math.Clamp(progress, 0.0, 1.0);
        var pullOut = Mathf.SmoothStep(0.3f, 0.6f, t);
        Rig.JumpTo(new Vector3(Start.X + 30f * Mathf.Sin(t * Mathf.Tau), 0f, Start.Y + 20f * Mathf.Sin(t * Mathf.Tau)));
        Rig.JumpToDistance(Mathf.Lerp(Rig.MinDistance, Rig.MaxDistance, pullOut));
        Rig.JumpToYaw(Mathf.DegToRad(30f + 90f * t));
        if (_holdZoom is { } zoom)
            Rig.JumpToDistance(zoom);
    }

    /// <summary>Holds the overview camera over the Store centre at this height, in metres.</summary>
    private void HoldOverview(float height)
    {
        _progress = null;
        var size = Host.Snapshot.Space.Size;
        Rig.JumpTo(new Vector3(size / 2f, 0f, size / 2f));
        Rig.JumpToDistance(height / Mathf.Sin(Mathf.DegToRad(Rig.OverviewTilt)));
        Rig.JumpToYaw(Mathf.DegToRad(30f));
    }

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static double Now => Time.GetTicksUsec() / 1_000_000.0;
}
