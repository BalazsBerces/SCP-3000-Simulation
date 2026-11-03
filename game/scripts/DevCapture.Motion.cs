using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --load=path loads a saved world at start; --speed=N then runs it at that speed (1, 2, 4, 8, 16 or 60);
    // --motion-report prints, over the capture window, how evenly the shown Store time moved from frame to frame
    // (Villagers walk with it) and how unevenly the core's snapshots came, e.g.
    // --load=late.scp3008.json --speed=16 --motion-report --capture-after=30.
    private string? _worldToLoad;
    private SimulationSpeed? _speedToSet;
    private bool _motionReport;
    private readonly List<double> _shownRates = [];
    private readonly List<double> _arrivalGaps = [];
    private double _lastShownSeconds = double.NaN;
    private long _lastSnapshotTick = -1;
    private double _sinceSnapshot;
    private long _firstTick = -1;

    private bool ConfigureMotion(string arg)
    {
        if (arg.StartsWith("--load="))
            _worldToLoad = arg["--load=".Length..];
        else if (arg.StartsWith("--speed="))
            _speedToSet = (SimulationSpeed)int.Parse(arg["--speed=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (arg == "--motion-report")
            _motionReport = true;
        else
            return false;
        return true;
    }

    private void StartMotion()
    {
        if (_worldToLoad is { } path)
            Host.LoadWorld(System.IO.Path.GetFullPath(path));
        else if (_speedToSet is { } speed)
            Host.Speed = speed;
        Host.WorldCreated += _ =>
        {
            if (_speedToSet is { } speed)
                Host.Speed = speed;
            ResetMotion();
        };
    }

    private void ResetMotion()
    {
        _shownRates.Clear();
        _arrivalGaps.Clear();
        _lastShownSeconds = double.NaN;
        _lastSnapshotTick = -1;
        _firstTick = -1;
    }

    private void TrackMotion(double delta)
    {
        if (!_motionReport || Host.IsGenerating || _elapsed < 1.0)
            return;
        // Shown Store time per real second, frame by frame, against what the speed asks for.
        var shown = Host.InterpolatedStoreTime.TotalSeconds;
        if (!double.IsNaN(_lastShownSeconds) && delta > 0)
            _shownRates.Add((shown - _lastShownSeconds) / delta);
        _lastShownSeconds = shown;
        var tick = Host.Snapshot.Clock.Tick;
        if (_firstTick < 0)
            _firstTick = tick;
        _sinceSnapshot += delta;
        if (tick != _lastSnapshotTick)
        {
            if (_lastSnapshotTick >= 0)
                _arrivalGaps.Add(_sinceSnapshot);
            _sinceSnapshot = 0;
            _lastSnapshotTick = tick;
        }
    }

    private void ReportMotion()
    {
        if (!_motionReport || _shownRates.Count < 2)
            return;
        var asked = (int)Host.Speed * StoreClock.TickDuration.TotalSeconds / Simulation.RealTimeStep.TotalSeconds;
        var mean = _shownRates.Average();
        var deviation = Math.Sqrt(_shownRates.Average(r => (r - mean) * (r - mean)));
        var frozen = _shownRates.Count(r => r <= 0);
        var gaps = _arrivalGaps.OrderBy(g => g).ToArray();
        var ran = (Host.Snapshot.Clock.Tick - _firstTick) * StoreClock.TickDuration.TotalSeconds;
        GD.Print($"Motion over {_shownRates.Count} frames at {Host.Speed}: shown Store time {mean / asked:P0} of the asked rate on average, " +
                 $"frame-to-frame spread {deviation / Math.Max(mean, 1e-9):P0}, {frozen} frozen frames; " +
                 $"the core ran {ran / Math.Max(_elapsed - 1.0, 1e-9) / asked:P0} of the asked rate.");
        if (gaps.Length > 0)
            GD.Print($"Snapshots: {gaps.Length} arrivals, gap median {gaps[gaps.Length / 2] * 1000:0} ms, " +
                     $"p95 {gaps[(int)(gaps.Length * 0.95)] * 1000:0} ms, max {gaps[^1] * 1000:0} ms, " +
                     $"{gaps.Count(g => g > 0.1)} over 100 ms.");
    }
}
