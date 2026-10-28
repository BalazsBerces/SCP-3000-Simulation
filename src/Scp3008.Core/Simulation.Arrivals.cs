using System.Numerics;

namespace Scp3008.Core;

/// <summary>Hidden runtime tuning. An interval of zero disables arrivals.</summary>
public sealed record ArrivalConfig
{
    public static ArrivalConfig Off { get; } = new() { IntervalHours = 0 };

    public double IntervalHours { get; init; } = 12;
    public int PopulationThreshold { get; init; } = 200;

    internal void Validate()
    {
        if (!double.IsFinite(IntervalHours) || IntervalHours < 0 || PopulationThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(ArrivalConfig));
    }
}

public sealed record WanderersArrived(StoreClock At, Band Band) : DomainEvent(At);

public sealed partial class Simulation
{
    private readonly SeededRandom _arrivalRandom;
    private long _nextArrivalTick;

    private ArrivalConfig Arrivals => _config.Arrivals ?? (_config.PillarLayout == PillarLayout.VillagerSandbox ||
        _config.LandmarkShowcase || _config.PointOfInterestShowcase ? ArrivalConfig.Off : new ArrivalConfig());

    private void ScheduleArrival()
    {
        var config = Arrivals;
        // Slowing the next interval preserves arrivals without imposing a population cap.
        var population = _villagers.Snapshot().Length + _staff.Snapshot().Length;
        var slowdown = Math.Max(1d, (double)population / config.PopulationThreshold);
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
            if (!IsSafeFloor(position))
                continue;
            var count = 1 + _arrivalRandom.Next(4);
            var positions = Enumerable.Range(0, count)
                .Select(i => _navigation.NearestWalkable(position + new Vector2(i % 2, i / 2) * 2))
                .Where(IsSafeFloor).ToArray();
            var band = PlaceBand(positions);
            ScheduleArrival();
            Emit(new WanderersArrived(clock, band));
            return;
        }
        ScheduleArrival();
    }

    private bool IsSafeFloor(Vector2 position) =>
        _navigation.IsWalkable(position) && !_snapshot.GroundAt(position).HasFlag(GroundProperty.Darkness);
}
