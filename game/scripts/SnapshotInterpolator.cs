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

    /// <summary>
    /// The Fog banks to display at <paramref name="nowSeconds"/>: each bank of the latest snapshot
    /// blended from where it was in the previous one, along the shortest wrapped path, so a bank
    /// drifting across a seam moves smoothly. Positions may lie just outside the Store floor.
    /// </summary>
    public IEnumerable<FogBank> FogBanks(double nowSeconds)
    {
        if (_previous is null || _latest is null)
            yield break;
        var blend = (float)Blend(nowSeconds);
        foreach (var bank in _latest.FogBanks)
        {
            var before = _previous.FogBanks.FirstOrDefault(b => b.Id == bank.Id);
            if (before is null)
            {
                yield return bank;
                continue;
            }
            var step = _latest.Space.Delta(before.Position, bank.Position);
            yield return bank with
            {
                Position = before.Position + step * blend,
                Radius = before.Radius + (bank.Radius - before.Radius) * blend,
                Density = before.Density + (bank.Density - before.Density) * blend,
            };
        }
    }

    /// <summary>
    /// The Villagers to display at <paramref name="nowSeconds"/>, each blended from where they stood in the previous
    /// snapshot along the shortest wrapped path, so a Villager crossing a seam walks on smoothly.
    /// </summary>
    public IEnumerable<Villager> Villagers(double nowSeconds)
    {
        if (_previous is null || _latest is null)
            yield break;
        var blend = (float)Blend(nowSeconds);
        foreach (var villager in _latest.Villagers)
        {
            var before = _previous.Villagers.FirstOrDefault(v => v.Id == villager.Id);
            if (before is null)
            {
                yield return villager;
                continue;
            }
            var turn = MathF.IEEERemainder(villager.Heading - before.Heading, MathF.Tau);
            yield return villager with
            {
                Position = before.Position + _latest.Space.Delta(before.Position, villager.Position) * blend,
                Heading = before.Heading + turn * blend,
            };
        }
    }
}
