using System.Numerics;

namespace Scp3008.Core;

/// <summary>Hidden runtime tuning. An interval of zero disables arrivals.</summary>
public sealed record ArrivalConfig
{
    public double IntervalHours { get; init; } = 12;
    public int AgentThreshold { get; init; } = 200;

    internal void Validate()
    {
        if (!double.IsFinite(IntervalHours) || IntervalHours < 0 || AgentThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(ArrivalConfig));
    }
}

public sealed record WanderersArrived(StoreClock At, Band Band) : DomainEvent(At);

public sealed partial class Simulation
{
    private readonly SeededRandom _arrivalRandom;
    private long _nextArrivalTick;

    private ArrivalConfig Arrivals => _config.Arrivals ?? new ArrivalConfig
    {
        IntervalHours = _config.PillarLayout == PillarLayout.VillagerSandbox ||
            _config.LandmarkShowcase || _config.PointOfInterestShowcase ? 0 : 12,
    };

    private void ScheduleArrival()
    {
        var config = Arrivals;
        // Slowing the next interval preserves arrivals without imposing a population cap.
        var agents = _villagers.Snapshot().Length + _staff.Snapshot().Length;
        var slowdown = Math.Max(1d, (double)agents / config.AgentThreshold);
        var ticks = config.IntervalHours * (StoreClock.TicksPerStoreDay / 24d) * slowdown;
        _nextArrivalTick = config.IntervalHours == 0 || ticks >= long.MaxValue - _tick ? long.MaxValue :
            _tick + Math.Max(1, (long)Math.Round(ticks, MidpointRounding.AwayFromZero));
    }

    private void AdvanceArrivals(StoreClock clock)
    {
        if (_tick < _nextArrivalTick)
            return;
        // Bounded search also works in scenarios whose entire walkable floor is unsafe.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var position = _navigation.NearestWalkable(new Vector2(
                _arrivalRandom.Range(0, _space.Size), _arrivalRandom.Range(0, _space.Size)));
            if (!_navigation.IsWalkable(position) || _snapshot.GroundAt(position).HasFlag(GroundProperty.Darkness))
                continue;
            var count = 1 + _arrivalRandom.Next(4);
            var positions = Enumerable.Range(0, count)
                .Select(i => _navigation.NearestWalkable(position + new Vector2(i % 2, i / 2) * 2))
                .Where(p => _navigation.IsWalkable(p) && !_snapshot.GroundAt(p).HasFlag(GroundProperty.Darkness)).ToArray();
            var band = PlaceBand(positions);
            ScheduleArrival();
            Emit(new WanderersArrived(clock, band));
            return;
        }
        ScheduleArrival();
    }
}
