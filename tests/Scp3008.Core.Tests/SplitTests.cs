using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class SplitTests
{
    private static readonly Vector2 At = new(30, 104);

    private static Simulation Sandbox(ArrivalConfig? arrivals = null) => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }, Arrivals: arrivals, Splits: true));

    private static Village Place(Simulation sim, int count, double morale = 0.7)
    {
        var members = Enumerable.Range(0, count)
            .Select(i => sim.Scenario.PlaceVillager(new(At.X + i % 5 * 0.8f - 2, At.Y - 1 + i / 5 * 0.8f), new Needs(Morale: morale))).ToArray();
        var village = sim.Scenario.PlaceVillage(At, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        return sim.Snapshot.Villages[village.Id];
    }

    private static List<DomainEvent> Run(Simulation sim, int ticks = 120)
    {
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.AdvanceTicks(ticks);
        return events;
    }

    [Fact]
    public void An_overcrowded_Village_splits_and_its_newest_people_found_a_new_Village()
    {
        using var sim = Sandbox();
        var home = Place(sim, 14);
        var split = Assert.Single(Run(sim).OfType<VillageSplit>());
        Assert.Equal((home.Id, SplitReason.Overcrowded), (split.FromVillageId, split.Reason));
        var founded = sim.Snapshot.Villages[split.Village.Id];
        Assert.Equal(home.VillagerIds.TakeLast(7).Order(), founded.VillagerIds.Order());
        Assert.Equal(7, sim.Snapshot.Villages[home.Id].VillagerIds.Count);
        Assert.True(sim.Snapshot.Space.Distance(home.Position, founded.Position) >= 48);
        Assert.Equal(200, founded.Stores[GoodsKind.TinnedFood]);
        Assert.Equal(200, sim.Snapshot.Villages[home.Id].Stores[GoodsKind.TinnedFood]);
        Assert.All(founded.VillagerIds, id => Assert.Equal(founded.Id, sim.Snapshot.Villagers.Single(v => v.Id == id).VillageId));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("too crowded") && line.Contains($"Village {founded.Id}"));
        // Once a Store day: neither side splits again at once.
        Assert.Empty(Run(sim, 500).OfType<VillageSplit>());
    }

    [Fact]
    public void A_Village_in_conflict_splits_and_the_discontented_found_a_new_Village()
    {
        using var sim = Sandbox();
        var home = Place(sim, 6);
        var unhappy = home.VillagerIds.Take(3).ToArray();
        foreach (var id in home.VillagerIds) sim.Scenario.SetNeeds(id, new Needs(Morale: unhappy.Contains(id) ? 0.05 : 0.3));
        var split = Assert.Single(Run(sim).OfType<VillageSplit>());
        Assert.Equal(SplitReason.LowMorale, split.Reason);
        Assert.Equal(unhappy.Order(), sim.Snapshot.Villages[split.Village.Id].VillagerIds.Order());
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("Quarrels split"));
    }

    [Fact]
    public void A_content_Village_with_room_does_not_split()
    {
        using var sim = Sandbox();
        Place(sim, 10);
        Assert.Empty(Run(sim, 500).OfType<VillageSplit>());
        Assert.Single(sim.Snapshot.Villages);
    }

    [Fact]
    public void A_large_Outpost_becomes_a_Village_of_its_own()
    {
        using var sim = Sandbox();
        var home = Place(sim, 8);
        var settlers = home.VillagerIds.Take(5).ToArray();
        var post = sim.Scenario.PlaceOutpost(home.Id, At + new Vector2(0, 30), settlers);
        var split = Assert.Single(Run(sim).OfType<VillageSplit>());
        Assert.Equal(SplitReason.OutpostGrew, split.Reason);
        var founded = sim.Snapshot.Villages[split.Village.Id];
        Assert.Equal(post.Position, founded.Position);
        Assert.Equal(settlers.Order(), founded.VillagerIds.Order());
        Assert.True(sim.Snapshot.Villages[home.Id].Outposts.Single().Abandoned);
        Assert.Equal(3, sim.Snapshot.Villages[home.Id].VillagerIds.Count);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("had grown large"));
    }

    [Fact]
    public void A_distant_Outpost_drifts_apart_after_a_Store_day()
    {
        using var sim = Sandbox();
        var home = Place(sim, 8);
        var far = At + new Vector2(50, -50);
        var young = sim.Scenario.PlaceOutpost(home.Id, far, home.VillagerIds.Take(2).ToArray());
        Assert.True(sim.Snapshot.Space.Distance(young.Position, home.Position) > 64);
        Assert.Empty(Run(sim).OfType<VillageSplit>());
        sim.Scenario.PlaceOutpost(home.Id, far + new Vector2(4, 0), home.VillagerIds.Skip(2).Take(3).ToArray(), TimeSpan.FromHours(24));
        var split = Assert.Single(Run(sim).OfType<VillageSplit>());
        Assert.Equal(SplitReason.OutpostDrifted, split.Reason);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("drifted apart"));
    }

    [Fact]
    public void After_the_last_Village_falls_Wanderers_keep_arriving_in_a_new_era()
    {
        using var sim = Sandbox(new ArrivalConfig { IntervalHours = 6 });
        var members = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(new(At.X + i * 0.8f - 1, 103))).ToArray();
        var village = sim.Scenario.PlaceVillage(At, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        sim.Scenario.PlanBarricade(village.Id);
        sim.Scenario.CompleteWall(village.Id, 0);
        sim.Scenario.BuildKeep(village.Id);
        var outside = At - new Vector2(18, 0);
        sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        for (var i = 0; i < 30000 && !events.OfType<VillageFell>().Any(); i++) sim.AdvanceTicks(1);
        Assert.Single(events.OfType<VillageFell>());
        var era = Assert.Single(events.OfType<EraBegan>());
        Assert.Equal((2, village.Id), (era.Era, era.LastVillageId));
        Assert.Equal(2, sim.Snapshot.Era);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("era 2 began"));
        var before = events.OfType<WanderersArrived>().Count();
        sim.Advance(TimeSpan.FromHours(13));
        Assert.True(events.OfType<WanderersArrived>().Count() > before);
        Assert.Equal(2, sim.Snapshot.Era);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(2, loaded.Snapshot.Era);
    }
}
