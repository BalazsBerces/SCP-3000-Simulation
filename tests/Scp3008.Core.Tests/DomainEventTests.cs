using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class DomainEventTests
{
    [Fact]
    public void Crossing_midnight_starts_a_new_store_day_and_emits_one_event()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(17)); // 08:00 on day 1 -> 01:00 on day 2

        Assert.Equal(2, sim.Snapshot.Clock.Day);
        Assert.Equal(TimeSpan.FromHours(1), sim.Snapshot.Clock.TimeOfDay);
        var began = Assert.Single(events.OfType<StoreDayBegan>());
        Assert.Equal(2, began.Day);
        Assert.Equal(TimeSpan.Zero, began.At.TimeOfDay);
    }

    [Fact]
    public void A_disposed_subscription_receives_no_more_events()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        var events = new List<DomainEvent>();
        var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(24));
        subscription.Dispose();
        var received = events.Count;
        sim.Advance(TimeSpan.FromHours(24));

        Assert.NotEqual(0, received);
        Assert.Equal(received, events.Count);
    }
}
