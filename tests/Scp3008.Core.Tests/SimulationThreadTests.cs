using Scp3008.Core;

namespace Scp3008.Core.Tests;

// These tests use real time, so they wait with generous timeouts and never assert exact tick counts.
public class SimulationThreadTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static Simulation NewWorld() =>
        Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));

    [Fact]
    public void A_started_simulation_advances_on_its_own_thread()
    {
        using var sim = NewWorld();
        var tickingThreads = new HashSet<int>();
        using var _ = sim.Subscribe(_ => tickingThreads.Add(Environment.CurrentManagedThreadId));
        sim.AdvanceTicks(StoreClock.TicksFor(TimeSpan.FromHours(16)) - 3); // 3 ticks before midnight

        sim.Speed = SimulationSpeed.Fast16;
        sim.Start();

        Assert.True(SpinWait.SpinUntil(() => sim.Snapshot.Clock.Day == 2, Timeout));
        Assert.True(sim.IsRunning);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, tickingThreads);
        Assert.NotEmpty(tickingThreads);
    }

    [Fact]
    public void Pausing_a_running_simulation_stops_the_clock()
    {
        using var sim = NewWorld();
        sim.Speed = SimulationSpeed.Fast16;
        sim.Start();
        Assert.True(SpinWait.SpinUntil(() => sim.Snapshot.Clock.Tick > 0, Timeout));

        sim.Speed = SimulationSpeed.Paused;
        Thread.Sleep(Simulation.RealTimeStep * 2); // let an in-flight step finish
        var pausedAt = sim.Snapshot.Clock.Tick;
        Thread.Sleep(Simulation.RealTimeStep * 6);

        Assert.Equal(pausedAt, sim.Snapshot.Clock.Tick);
    }

    [Fact]
    public void Disposing_stops_the_simulation_thread()
    {
        var sim = NewWorld();
        sim.Speed = SimulationSpeed.Fast16;
        sim.Start();
        Assert.True(SpinWait.SpinUntil(() => sim.Snapshot.Clock.Tick > 0, Timeout));

        sim.Dispose();
        var stoppedAt = sim.Snapshot.Clock.Tick;
        Thread.Sleep(Simulation.RealTimeStep * 6);

        Assert.False(sim.IsRunning);
        Assert.Equal(stoppedAt, sim.Snapshot.Clock.Tick);
    }

    [Fact]
    public void Normal_speed_runs_about_twenty_ticks_per_real_second()
    {
        using var sim = NewWorld();
        sim.Start();

        Thread.Sleep(TimeSpan.FromSeconds(2));
        var ticks = sim.Snapshot.Clock.Tick;

        // 40 expected; the wide band only guards against a wrong rate, not scheduler jitter.
        Assert.InRange(ticks, 25, 50);
    }
}
