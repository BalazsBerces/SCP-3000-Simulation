using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Smooths the presentation between snapshots: the core publishes one snapshot per fixed
/// real-time step, and the presentation renders slightly behind, blending the previous and
/// latest snapshot by how far the current frame is into the step.
/// </summary>
public sealed class SnapshotInterpolator(TimeSpan stepInterval)
{
    private WorldSnapshot? _previous;
    private WorldSnapshot? _latest;
    private double _latestArrivalSeconds;

    /// <summary>Feeds the core's current snapshot; call once per frame.</summary>
    public void Observe(WorldSnapshot snapshot, double nowSeconds)
    {
        if (_latest is null)
        {
            _previous = _latest = snapshot;
            _latestArrivalSeconds = nowSeconds;
            return;
        }
        if (snapshot.Clock.Tick == _latest.Clock.Tick)
        {
            // Same moment in the world (e.g. only the speed changed): nothing to blend.
            _latest = snapshot;
            return;
        }
        _previous = _latest;
        _latest = snapshot;
        _latestArrivalSeconds = nowSeconds;
    }

    /// <summary>How far, from 0 to 1, the current frame is between the previous and latest snapshot.</summary>
    public double Blend(double nowSeconds) =>
        Math.Clamp((nowSeconds - _latestArrivalSeconds) / stepInterval.TotalSeconds, 0.0, 1.0);

    /// <summary>Total Store time to display at <paramref name="nowSeconds"/>.</summary>
    public TimeSpan StoreTime(double nowSeconds)
    {
        if (_previous is null || _latest is null)
            return TimeSpan.Zero;
        var from = _previous.Clock.TotalStoreTime;
        var to = _latest.Clock.TotalStoreTime;
        return from + (to - from) * Blend(nowSeconds);
    }
}
