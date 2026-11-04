using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class RaidTests
{
    // Isolationists rarely raid of their own accord and send no Caravans, so manual Raids run undisturbed.
    private static Village Place(Simulation sim, Vector2 at, int people, Personality personality = Personality.Isolationists)
    {
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(at + new Vector2(i - people / 2f, 1)).Id).ToArray();
        var village = sim.Scenario.PlaceVillage(at, ids, personality);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 30);
        return village;
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int ticks = 8000)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done());
    }

    private static RaidStruck Raid(Simulation sim, List<DomainEvent> events, int from, int to, RaidGoal goal, Holding? site = null)
    {
        var raid = sim.Scenario.SendRaid(from, to, goal, site);
        RunUntil(sim, () => events.OfType<RaidReturned>().Any(r => r.RaidId == raid.Id));
        return events.OfType<RaidStruck>().Single(s => s.RaidId == raid.Id);
    }

    [Fact]
    public void A_successful_Raid_moves_stores_and_is_chronicled()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var target = Place(sim, new(20, 104), 2);
        var raiders = Place(sim, new(50, 104), 5);
        sim.Scenario.SetStores(target.Id, GoodsKind.TinnedFood, 0);
        sim.Scenario.SetStores(target.Id, GoodsKind.DryFood, 40);
        var struck = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Stores);
        Assert.True(struck.Success);
        Assert.Equal(20, struck.FromPile); // four Raiders carry five units each
        Assert.True(sim.Snapshot.Villages[raiders.Id].Stores.GetValueOrDefault(GoodsKind.DryFood) > 0);
        // Meals and Department flavour move a unit or two meanwhile.
        Assert.InRange(sim.Snapshot.Villages[target.Id].Stores.GetValueOrDefault(GoodsKind.DryFood), 0, 40 - 20 + 2);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"Raiders of Village {raiders.Id} fell on Village {target.Id}") && line.Contains("stores"));
    }

    [Fact]
    public void Raids_wound_and_then_kill_the_defenders()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var target = Place(sim, new(20, 104), 2);
        var raiders = Place(sim, new(50, 104), 5);
        Assert.Equal(2, Raid(sim, events, raiders.Id, target.Id, RaidGoal.Stores).Hurt);
        Assert.All(target.VillagerIds, id => Assert.Contains(events.OfType<VillagerWounded>(), w => w.VillagerId == id));
        // The second Raid finds the wounded at home with no one able to fight, and the blows kill them.
        var second = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Stores);
        Assert.True(second.Hurt > 0);
        Assert.Contains(events.OfType<VillagerTaken>(), t => target.VillagerIds.Contains(t.VillagerId));
    }

    [Fact]
    public void A_Raid_can_take_a_Pillar_site()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var pillar = sim.Snapshot.Pillars[0];
        var at = sim.Snapshot.Space.Wrap(pillar.Position + new Vector2(0, pillar.Width / 2 + 8));
        var target = Place(sim, at, 2);
        var raiders = Place(sim, sim.Snapshot.Space.Wrap(at + new Vector2(30, 0)), 5);
        RunUntil(sim, () => sim.Snapshot.Villages[target.Id].Holdings.Any(h => h.Kind == HoldingKind.Pillar), 1000);
        var site = sim.Snapshot.Villages[target.Id].Holdings.Single(h => h.Kind == HoldingKind.Pillar);
        var struck = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Site, site);
        Assert.True(struck.Success);
        Assert.Equal(site, struck.Site);
        Assert.Contains(site, sim.Snapshot.Villages[raiders.Id].Holdings);
        Assert.DoesNotContain(site, sim.Snapshot.Villages[target.Id].Holdings);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"seized Pillar {pillar.Coordinate} from Village {target.Id}"));
    }

    [Fact]
    public void A_Raid_can_take_Captives_who_are_absorbed_into_the_captors_Village()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var target = Place(sim, new(20, 104), 3);
        var raiders = Place(sim, new(50, 104), 5);
        var struck = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Captives);
        Assert.True(struck.Success);
        Assert.Equal(2, struck.CaptiveIds.Count);
        var captors = sim.Snapshot.Villages[raiders.Id];
        Assert.All(struck.CaptiveIds, id =>
        {
            Assert.Contains(id, captors.VillagerIds);
            Assert.Contains(captors.Captives, c => c.VillagerId == id && c.FromVillageId == target.Id);
            Assert.DoesNotContain(id, sim.Snapshot.Villages[target.Id].VillagerIds);
            Assert.Equal(raiders.Id, sim.Snapshot.Villagers.Single(v => v.Id == id).VillageId);
        });
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("captive"));

        RunUntil(sim, () => events.OfType<CaptiveAbsorbed>().Count() == 2, Simulation.CaptiveAbsorbTicks + 200);
        Assert.Empty(sim.Snapshot.Villages[raiders.Id].Captives);
        Assert.All(struck.CaptiveIds, id => Assert.Contains(id, sim.Snapshot.Villages[raiders.Id].VillagerIds));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"became one of Village {raiders.Id}'s own"));
    }

    [Fact]
    public void Raiders_take_from_an_outside_Furniture_yard_and_the_open_pile_but_not_from_a_defended_Storehouse()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var target = Place(sim, new(20, 104), 4);
        for (var i = 0; i < 3; i++) sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(14 + i * 3, 110));
        RunUntil(sim, () => sim.Snapshot.Villages[target.Id].Storehouse?.Complete == true, 12000);
        var raiders = Place(sim, new(60, 104), 5);
        var yard = sim.Scenario.PlanFurnitureYard(target.Id);
        Assert.False(yard.Inside);
        for (var i = 0; i < 3; i++) sim.Scenario.PlaceFurniture(FurnitureType.Chair, yard.Position + new Vector2(i - 1, 0));
        // 36 units fit the Storehouse; 4 of the water lie in the open pile.
        sim.Scenario.SetStores(target.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SetStores(target.Id, GoodsKind.BottledWater, 10);
        var struck = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Stores);
        Assert.True(struck.FromYard > 0);
        Assert.True(struck.FromPile > 0);
        Assert.Equal(0, struck.FromStorehouse);
        Assert.True(sim.Snapshot.Villages[raiders.Id].Materials.Values.Sum() > 0);
    }

    [Theory]
    [InlineData(HoldingKind.FountainCourt)]
    [InlineData(HoldingKind.Cafeteria)]
    public void A_Raid_can_seize_the_fountain_court_or_a_Cafeteria(HoldingKind kind)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1,
            LandmarkShowcase: kind == HoldingKind.FountainCourt, PointOfInterestShowcase: kind == HoldingKind.Cafeteria));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(9));
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var at = kind == HoldingKind.FountainCourt
            ? sim.Snapshot.WaterPoints.First(w => w.Kind == WaterPointKind.FountainCourt).Position
            : sim.Snapshot.PointsOfInterest.First(p => p.Type == PointOfInterestType.Cafeteria) is var cafeteria
                ? sim.Snapshot.Space.Wrap(cafeteria.Centre + new Vector2(0, cafeteria.Size.Y / 2 + 2)) : default;
        var target = Place(sim, at, 2);
        var raiders = Place(sim, sim.Snapshot.Space.Wrap(at + new Vector2(10, 0)), 5);
        RunUntil(sim, () => sim.Snapshot.Villages[target.Id].Holdings.Any(h => h.Kind == kind), 1000);
        var site = sim.Snapshot.Villages[target.Id].Holdings.Single(h => h.Kind == kind);
        var struck = Raid(sim, events, raiders.Id, target.Id, RaidGoal.Site, site);
        Assert.True(struck.Success);
        Assert.Contains(site, sim.Snapshot.Villages[raiders.Id].Holdings);
        Assert.DoesNotContain(site, sim.Snapshot.Villages[target.Id].Holdings);
    }

    [Fact]
    public void Raiding_likelihood_follows_Personality()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var raiders = Place(sim, new(20, 104), 5, Personality.Raiders);
        var traders = Place(sim, new(40, 104), 5, Personality.Traders);
        RunUntil(sim, () => sim.Snapshot.Villages.All(v => v.KnownMap.Villages.Count >= 1));
        sim.AdvanceTicks(10000);
        Assert.True(events.OfType<RaidLaunched>().Count(r => r.FromVillageId == raiders.Id) >= 2);
        Assert.DoesNotContain(events.OfType<RaidLaunched>(), r => r.FromVillageId == traders.Id);
        Assert.True(Simulation.RaidChance(Personality.Raiders) > Simulation.RaidChance(Personality.Bold));
        Assert.True(Simulation.RaidChance(Personality.Bold) > Simulation.RaidChance(Personality.Cautious));
        Assert.Equal(0, Simulation.RaidChance(Personality.Traders));
    }

    [Fact]
    public void Raids_and_Captives_survive_a_save()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var target = Place(sim, new(20, 104), 3);
        var raiders = Place(sim, new(50, 104), 5);
        var raid = sim.Scenario.SendRaid(raiders.Id, target.Id, RaidGoal.Captives);
        RunUntil(sim, () => events.OfType<RaidStruck>().Any());
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(sim.Snapshot.Raids.Single().CaptiveIds, loaded.Snapshot.Raids.Single().CaptiveIds);
        var back = new List<DomainEvent>();
        using var sub2 = loaded.Subscribe(back.Add);
        RunUntil(loaded, () => back.OfType<RaidReturned>().Any(r => r.RaidId == raid.Id));
        Assert.Equal(2, loaded.Snapshot.Villages[raiders.Id].Captives.Count);
    }
}
