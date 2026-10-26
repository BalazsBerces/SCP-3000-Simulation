using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class LightCycleTests
{
    private static Simulation NewWorld() =>
        Simulation.Create(new WorldConfig(Seed: 3008, StartingBands: false, RuntimeRngSeed: 1));

    [Fact]
    public void A_new_world_starts_with_the_store_open_and_fully_lit()
    {
        using var sim = NewWorld();

        Assert.Equal(DayPhase.Open, sim.Snapshot.Phase);
        Assert.Equal(1.0, sim.Snapshot.StoreLightLevel);
    }

    [Fact]
    public void Closing_time_begins_once_at_22_00_and_the_store_goes_dark()
    {
        using var sim = NewWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(15)); // 08:00 -> 23:00 on day 1

        var began = Assert.Single(events.OfType<ClosingTimeBegan>());
        Assert.Equal(1, began.Day);
        Assert.Equal(TimeSpan.FromHours(22), began.At.TimeOfDay);
        Assert.Empty(events.OfType<StoreReopened>());
        Assert.Equal(DayPhase.ClosingTime, sim.Snapshot.Phase);
        Assert.Equal(0.0, sim.Snapshot.StoreLightLevel);
    }

    [Fact]
    public void The_store_reopens_at_08_00_the_next_day_fully_lit()
    {
        using var sim = NewWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(25)); // 08:00 on day 1 -> 09:00 on day 2

        var reopened = Assert.Single(events.OfType<StoreReopened>());
        Assert.Equal(2, reopened.Day);
        Assert.Equal(TimeSpan.FromHours(8), reopened.At.TimeOfDay);
        Assert.Equal(DayPhase.Open, sim.Snapshot.Phase);
        Assert.Equal(1.0, sim.Snapshot.StoreLightLevel);
    }

    [Fact]
    public void Each_store_day_closes_and_reopens_once_in_order()
    {
        using var sim = NewWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(72));

        var cycle = events.Where(e => e is ClosingTimeBegan or StoreReopened).ToList();
        Assert.Equal(6, cycle.Count);
        for (var i = 0; i < cycle.Count; i++)
            Assert.IsType(i % 2 == 0 ? typeof(ClosingTimeBegan) : typeof(StoreReopened), cycle[i]);
    }

    [Fact]
    public void About_forty_percent_of_a_store_day_is_dark()
    {
        using var sim = NewWorld();
        long darkTicks = 0;

        for (long i = 0; i < StoreClock.TicksPerStoreDay; i++)
        {
            sim.AdvanceTicks(1);
            if (sim.Snapshot.StoreLightLevel < 0.5)
                darkTicks++;
        }

        var darkShare = (double)darkTicks / StoreClock.TicksPerStoreDay;
        Assert.InRange(darkShare, 0.38, 0.44);
    }

    [Fact]
    public void Store_lights_change_gradually_between_open_and_closing_time()
    {
        using var sim = NewWorld();
        var previous = sim.Snapshot.StoreLightLevel;
        var largestStep = 0.0;
        var sawInBetween = false;

        for (long i = 0; i < StoreClock.TicksPerStoreDay; i++)
        {
            sim.AdvanceTicks(1);
            var level = sim.Snapshot.StoreLightLevel;
            largestStep = Math.Max(largestStep, Math.Abs(level - previous));
            sawInBetween |= level is > 0.1 and < 0.9;
            previous = level;
        }

        Assert.True(sawInBetween, "The lights should pass through in-between levels.");
        Assert.True(largestStep < 0.05, $"Largest per-tick change was {largestStep:0.000}.");
    }
}
