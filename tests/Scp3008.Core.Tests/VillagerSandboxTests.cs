using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class VillagerSandboxTests
{
    [Fact]
    public void Presets_provide_distinct_situations()
    {
        Assert.Equal(new[] { "closing soon", "empty", "scavenge", "staff nearby", "starving" }, ScenarioBuilder.VillagerSandboxPresets);
        using var empty = ScenarioBuilder.LoadVillagerSandbox("empty");
        Assert.Empty(empty.Snapshot.Furniture);
        Assert.Empty(empty.Snapshot.Stock);
        using var scavenge = ScenarioBuilder.LoadVillagerSandbox();
        using var starving = ScenarioBuilder.LoadVillagerSandbox("starving");
        Assert.Equal(3, scavenge.Snapshot.Stock.Single(g => g.Kind == GoodsKind.TinnedFood).Amount);
        Assert.Equal(1, starving.Snapshot.Stock.Single(g => g.Kind == GoodsKind.TinnedFood).Amount);
        Assert.Equal(new[] { GearKind.HexKey, GearKind.Screwdriver }, scavenge.Snapshot.Gear.Select(g => g.Kind));
        Assert.Equal(scavenge.Snapshot.Villagers.Select(v => v.Id), Assert.Single(scavenge.Snapshot.Bands).VillagerIds);
        using var closing = ScenarioBuilder.LoadVillagerSandbox("closing soon");
        Assert.InRange(closing.Snapshot.Clock.TimeOfDay, TimeSpan.FromHours(21.9), TimeSpan.FromHours(22));
        var events = new List<DomainEvent>();
        using var subscription = closing.Subscribe(events.Add);
        closing.Advance(TimeSpan.FromMinutes(4));
        Assert.Contains(events, e => e is ClosingTimeBegan);
    }

    [Fact]
    public void Staff_patrol_at_Closing_time_and_stand_while_open()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("staff nearby");
        var start = Assert.Single(sim.Snapshot.Staff).Position;
        sim.Advance(TimeSpan.FromMinutes(5));
        var moved = Assert.Single(sim.Snapshot.Staff);
        Assert.InRange(sim.Snapshot.Space.Distance(start, moved.Position), 4.9f, 5.1f);
        sim.Advance(TimeSpan.FromHours(2));
        Assert.Single(sim.Snapshot.Staff);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var stopped = Assert.Single(sim.Snapshot.Staff).Position;
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(stopped, Assert.Single(sim.Snapshot.Staff).Position);
    }

    [Fact]
    public void Sandbox_navigation_wraps_and_pause_allows_one_tick_steps()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var villager = sim.Scenario.PlaceVillager(new Vector2(127, 108));
        sim.Scenario.SendVillager(villager.Id, new Vector2(1, 108));
        sim.Speed = SimulationSpeed.Paused;
        var tick = sim.Snapshot.Clock.Tick;
        sim.AdvanceRealTime(TimeSpan.FromSeconds(1));
        Assert.Equal(tick, sim.Snapshot.Clock.Tick);
        sim.AdvanceTicks(1);
        Assert.Equal(tick + 1, sim.Snapshot.Clock.Tick);
        sim.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(new Vector2(1, 108), Assert.Single(sim.Snapshot.Villagers).Position);
        Assert.Null(Assert.Single(sim.Snapshot.Villagers).Destination);
        Assert.Equal(SimulationSpeed.Paused, sim.Speed);
    }

    [Theory]
    [InlineData("{\"furniture\":[{\"type\":\"Chair\",\"position\":[1,2],\"stock\":[{\"kind\":\"TinnedFood\",\"amount\":3}]}]}")]
    [InlineData("{\"staff\":[{\"position\":[1,2],\"patrol\":[]}]}")]
    [InlineData("{\"bands\":[[]]}")]
    [InlineData("{\"gear\":[{\"kind\":\"HexKey\",\"position\":[1]}]}")]
    public void Invalid_scenario_data_is_rejected(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => ScenarioBuilder.LoadVillagerSandboxJson(json));

    public static IEnumerable<object[]> Presets() => ScenarioBuilder.VillagerSandboxPresets.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Presets))]
    public void A_preset_runs_a_Store_day_at_60x(string name)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox(name);
        var before = sim.Snapshot;
        Assert.Equal(128f, before.Space.Size);
        Assert.Equal(4, before.Space.ChunkCount);
        Assert.Single(before.Pillars);
        Assert.Single(before.Sectors);
        Assert.Empty(before.Landmarks);
        Assert.Empty(before.PointsOfInterest);
        Assert.All(before.Bands, b => Assert.All(b.VillagerIds, id => Assert.Contains(before.Villagers, v => v.Id == id)));
        sim.Speed = SimulationSpeed.Fast60;
        sim.AdvanceRealTime(TimeSpan.FromSeconds(10));
        Assert.Equal(before.Clock.TotalStoreTime + TimeSpan.FromDays(1), sim.Snapshot.Clock.TotalStoreTime);
        Assert.Equal(before.Villagers.Count, sim.Snapshot.Villagers.Count + sim.Snapshot.Bodies.Count);
        Assert.All(sim.Snapshot.Villagers, v =>
        {
            Assert.InRange(v.Position.X, 0, 128);
            Assert.InRange(v.Position.Y, 0, 128);
        });
        Assert.True(sim.Snapshot.Stock.Sum(g => g.Amount) <= before.Stock.Sum(g => g.Amount));
    }
}
