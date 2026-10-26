using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StoreClockTests
{
    private static Simulation NewWorld() =>
        Simulation.Create(new WorldConfig(Seed: 3008, StartingBands: false, RuntimeRngSeed: 1));

    [Fact]
    public void A_new_world_starts_on_day_one_at_store_opening()
    {
        using var sim = NewWorld();

        var clock = sim.Snapshot.Clock;

        Assert.Equal(0, clock.Tick);
        Assert.Equal(1, clock.Day);
        Assert.Equal(TimeSpan.FromHours(8), clock.TimeOfDay);
    }

    [Fact]
    public void Advancing_six_store_hours_moves_the_clock_by_six_hours()
    {
        using var sim = NewWorld();

        sim.Advance(TimeSpan.FromHours(6));

        var clock = sim.Snapshot.Clock;
        Assert.Equal(1, clock.Day);
        Assert.Equal(TimeSpan.FromHours(14), clock.TimeOfDay);
    }
}
