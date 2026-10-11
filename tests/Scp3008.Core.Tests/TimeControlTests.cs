using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class TimeControlTests
{
    private static Simulation NewWorld() =>
        Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));

    [Fact]
    public void A_new_world_runs_at_normal_speed()
    {
        using var sim = NewWorld();

        Assert.Equal(SimulationSpeed.Normal, sim.Snapshot.Speed);
    }

    [Fact]
    public void One_store_day_lasts_ten_real_minutes_at_normal_speed()
    {
        using var sim = NewWorld();

        sim.AdvanceRealTime(TimeSpan.FromMinutes(10));

        Assert.Equal(2, sim.Snapshot.Clock.Day);
        Assert.Equal(TimeSpan.FromHours(8), sim.Snapshot.Clock.TimeOfDay);
    }

    [Theory]
    [InlineData(SimulationSpeed.Fast2, 300)]
    [InlineData(SimulationSpeed.Fast4, 150)]
    [InlineData(SimulationSpeed.Fast8, 75)]
    [InlineData(SimulationSpeed.Fast16, 37.5)]
    public void Fast_forward_shortens_a_store_day(SimulationSpeed speed, double realSecondsPerDay)
    {
        using var sim = NewWorld();
        sim.Speed = speed;

        sim.AdvanceRealTime(TimeSpan.FromSeconds(realSecondsPerDay));

        Assert.Equal(2, sim.Snapshot.Clock.Day);
        Assert.Equal(TimeSpan.FromHours(8), sim.Snapshot.Clock.TimeOfDay);
        Assert.Equal(speed, sim.Snapshot.Speed);
    }

    [Fact]
    public void A_paused_clock_does_not_advance_with_real_time()
    {
        using var sim = NewWorld();
        sim.Speed = SimulationSpeed.Paused;

        sim.AdvanceRealTime(TimeSpan.FromMinutes(5));

        Assert.Equal(0, sim.Snapshot.Clock.Tick);
    }

    [Fact]
    public void Real_time_shorter_than_a_tick_step_is_carried_over_not_lost()
    {
        using var sim = NewWorld();

        for (var i = 0; i < 600; i++)
            sim.AdvanceRealTime(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromHours(8), sim.Snapshot.Clock.TimeOfDay);
        Assert.Equal(2, sim.Snapshot.Clock.Day);
    }
}
