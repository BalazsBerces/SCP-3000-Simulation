using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class WandererTests
{
    [Fact]
    public void The_first_arrival_interval_counts_the_starting_Bands()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12,
            Arrivals: new ArrivalConfig { IntervalHours = 0.002, PopulationThreshold = 1 }));
        var arrivals = new List<WanderersArrived>();
        using var subscription = sim.Subscribe(e => { if (e is WanderersArrived arrived) arrivals.Add(arrived); });
        Assert.True(sim.Snapshot.Villagers.Count > 1);
        sim.AdvanceTicks(1);
        Assert.Empty(arrivals);
    }

    [Fact]
    public void Arrivals_use_safe_walkable_positions_in_a_generated_Store()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12, StartingBands: false,
            Arrivals: new ArrivalConfig { IntervalHours = 0.002 }));
        sim.AdvanceTicks(1);
        var band = Assert.Single(sim.Snapshot.Bands);
        Assert.All(sim.Snapshot.Villagers, person =>
        {
            Assert.False(sim.Snapshot.GroundAt(person.Position).HasFlag(GroundProperty.Darkness));
            Assert.Equal(person.Position, sim.Snapshot.OutsidePillars(person.Position));
            Assert.Contains(person.Id, band.VillagerIds);
        });
    }

    [Fact]
    public void A_thirsty_Wanderer_drinks_on_the_way_to_a_known_Village()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var resident = sim.Scenario.PlaceVillager(new(40, 108));
        sim.Scenario.PlaceVillage(new(40, 108), [resident.Id]);
        var water = sim.Scenario.PlaceWaterPoint(new(20, 96));
        var band = sim.Scenario.PlaceBand([new(20, 108)], new Needs(Thirst: 0.8), new KnownMap
        {
            Villages = Array.AsReadOnly(new[] { new KnownVillage(0, new(40, 108), sim.Snapshot.Clock) }),
        });
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(events.OfType<WaterDrunk>(), e => e.VillagerId == band.VillagerIds[0] && e.WaterPoint.Position == water.Position);
        Assert.Contains(sim.Snapshot.Villages[0].VillagerIds, id => id == band.VillagerIds[0]);
        Assert.True(events.FindIndex(e => e is WaterDrunk drink && drink.VillagerId == band.VillagerIds[0]) <
            events.FindIndex(e => e is WanderersJoined));
    }

    [Fact]
    public void Arriving_Wanderers_can_found_a_new_Village()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12,
            PillarLayout: PillarLayout.VillagerSandbox, Arrivals: new ArrivalConfig { IntervalHours = 0.5 }));
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 100);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(6));
        Assert.NotEmpty(sim.Snapshot.Villages);
        Assert.Contains(events, e => e is VillageFounded);
        Assert.Contains(sim.Snapshot.Villages.SelectMany(v => v.VillagerIds),
            id => events.OfType<WanderersArrived>().Any(e => e.Band.VillagerIds.Contains(id)));
    }

    [Fact]
    public void Bands_do_not_join_undiscovered_Villages()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var person = sim.Scenario.PlaceVillager(new(80, 108));
        sim.Scenario.PlaceVillage(new(80, 108), [person.Id]);
        var band = sim.Scenario.PlaceBand([new(20, 108)]);
        sim.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(sim.Snapshot.Bands);
        Assert.Empty(sim.Snapshot.Bands[0].KnownMap.Villages);
        Assert.Null(sim.Snapshot.Villagers.Single(v => v.Id == band.VillagerIds[0]).VillageId);
    }

    [Theory]
    [InlineData(-1, 200)]
    [InlineData(double.NaN, 200)]
    [InlineData(double.PositiveInfinity, 200)]
    [InlineData(1, 0)]
    public void Invalid_arrival_configuration_is_rejected(double interval, int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new WorldConfig(3008,
            Arrivals: new ArrivalConfig { IntervalHours = interval, PopulationThreshold = threshold })));
    }

    [Fact]
    public void Refugees_keep_their_fallen_Villages_knowledge_and_Recipes_when_founding()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var distantGround = new System.Numerics.Vector2(4, 4);
        sim.Scenario.PlaceBand([new(20, 108)], new Needs(Rest: 0.1),
            new KnownMap { ExploredGround = Array.AsReadOnly(new[] { distantGround }) }, [GearKind.Bandage]);
        sim.Advance(TimeSpan.FromHours(1));
        var village = Assert.Single(sim.Snapshot.Villages);
        Assert.Equal(new[] { GearKind.Bandage }, village.Recipes);
        Assert.Contains(distantGround, village.KnownMap.ExploredGround);
        Assert.Contains(sim.Snapshot.Chronicle, s => s.Contains("founded"));
    }

    [Fact]
    public void Refugee_Recipes_merge_without_resetting_existing_members_Roles_or_skills()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var founder = sim.Scenario.PlaceBand([new(4, 108), new(5, 108), new(6, 108), new(7, 108)],
            new Needs(Rest: 0.1), recipes: []);
        sim.AdvanceTicks(1);
        sim.Scenario.SetSkill(founder.VillagerIds[3], Role.Guard, 3);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 20);
        var refugee = sim.Scenario.PlaceBand([new(5, 108)], recipes: [GearKind.Bandage]);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Equal(new[] { GearKind.Bandage }, sim.Snapshot.Villages[0].Recipes);
        var veteran = sim.Snapshot.Villagers.Single(v => v.Id == founder.VillagerIds[3]);
        Assert.Equal(Role.Guard, veteran.Role);
        Assert.Equal(3, veteran.Skills[Role.Guard]);
        Assert.Equal(0, sim.Snapshot.Villagers.Single(v => v.Id == refugee.VillagerIds[0]).VillageId);
    }

    [Fact]
    public void High_total_population_slows_arrivals_and_includes_Staff()
    {
        static int CountArrivals(int threshold, bool staff)
        {
            var patrols = staff ? string.Join(",", Enumerable.Repeat("""{"position":[20,108],"patrol":[[20,108]]}""", 20)) : "";
            using var sim = ScenarioBuilder.LoadVillagerSandboxJson($$"""
                {"runtimeRngSeed":12,"arrivals":{"intervalHours":0.25,"populationThreshold":{{threshold}}},"staff":[{{patrols}}]}
                """);
            var count = 0;
            using var subscription = sim.Subscribe(e => { if (e is WanderersArrived) count++; });
            sim.Advance(TimeSpan.FromHours(2));
            return count;
        }
        Assert.Equal(8, CountArrivals(200, false));
        Assert.InRange(CountArrivals(1, false), 1, 4);
        Assert.Equal(1, CountArrivals(1, true));
    }

    [Fact]
    public void Arrival_schedule_randomness_and_joining_continue_identically_after_loading()
    {
        using var original = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12,
            PillarLayout: PillarLayout.VillagerSandbox, Arrivals: new ArrivalConfig { IntervalHours = 0.25 }));
        var resident = original.Scenario.PlaceVillager(new(20, 108));
        original.Scenario.PlaceVillage(new(20, 108), [resident.Id]);
        original.Scenario.PlaceBand([new(36, 108)]);
        original.Advance(TimeSpan.FromMinutes(3));
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        static string Snapshot(Simulation sim) => System.Text.Json.JsonSerializer.Serialize(sim.Snapshot,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        Assert.Equal(Snapshot(original), Snapshot(loaded));
        original.Advance(TimeSpan.FromHours(2));
        loaded.Advance(TimeSpan.FromHours(2));
        Assert.Equal(Snapshot(original), Snapshot(loaded));
        Assert.Contains(loaded.Snapshot.Chronicle, s => s.Contains("joined"));
        Assert.Contains(loaded.Snapshot.Chronicle, s => s.Contains("arrived"));
    }

    [Fact]
    public void Wanderers_find_a_Village_walk_to_it_and_add_their_Known_map()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var founder = sim.Scenario.PlaceVillager(new(4, 108));
        var village = sim.Scenario.PlaceVillage(new(4, 108), [founder.Id]);
        var band = sim.Scenario.PlaceBand([new(118, 108)]);
        var remoteWater = sim.Scenario.PlaceWaterPoint(new(112, 108));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.AdvanceTicks(1);
        Assert.Null(sim.Snapshot.Villagers.Single(v => v.Id == band.VillagerIds[0]).VillageId);
        Assert.Contains(sim.Snapshot.Bands[0].KnownMap.Villages, v => v.VillageId == village.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Equal(2, sim.Snapshot.Villages[0].VillagerIds.Count);
        Assert.Contains(sim.Snapshot.Villages[0].KnownMap.Water, w => w.Position == remoteWater.Position);
        Assert.Single(events.OfType<WanderersJoined>());
        Assert.Contains(sim.Snapshot.Chronicle, s => s.Contains("joined Village 0"));
    }

    [Fact]
    public void Joining_stories_name_the_place()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var founder = sim.Scenario.PlaceVillager(new(40, 108));
        sim.Scenario.PlaceVillage(new(40, 108), [founder.Id]);
        sim.Scenario.PlaceBand([new(36, 108)]);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Contains(sim.Snapshot.Chronicle, s => s.Contains("joined Village 0 in Sector "));
    }

    [Fact]
    public void Only_arrivals_of_note_enter_the_Chronicle()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12,
            PillarLayout: PillarLayout.VillagerSandbox, StartingBands: false,
            Arrivals: new ArrivalConfig { IntervalHours = 0.25 }));
        var arrivals = new List<WanderersArrived>();
        using var subscription = sim.Subscribe(e => { if (e is WanderersArrived arrived) arrivals.Add(arrived); });
        sim.Advance(TimeSpan.FromHours(3));
        Assert.Contains(arrivals, e => e.Band.VillagerIds.Count < 3);
        Assert.Equal(arrivals.Count(e => e.Band.VillagerIds.Count >= 3),
            sim.Snapshot.Chronicle.Count(s => s.Contains("arrived")));
    }

    [Fact]
    public void A_large_Band_founds_its_own_Village_at_a_good_site_nearer_than_a_known_one()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var resident = sim.Scenario.PlaceVillager(new(100, 108));
        sim.Scenario.PlaceVillage(new(100, 108), [resident.Id]);
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(22, 110));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 100);
        sim.Scenario.PlaceWaterPoint(new(22, 106));
        var known = new KnownMap { Villages = Array.AsReadOnly(new[] { new KnownVillage(0, new(100, 108), sim.Snapshot.Clock) }) };
        sim.Scenario.PlaceBand([new(20, 108), new(21, 108), new(22, 108)], knownMap: known);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(events, e => e is VillageFounded);
        Assert.DoesNotContain(events, e => e is WanderersJoined);
    }

    [Fact]
    public void A_Band_that_finds_a_remembered_Village_abandoned_does_not_join_it()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Needs: new NeedsConfig(DehydrationHours: 0.5)));
        var resident = sim.Scenario.PlaceVillager(new(40, 108), new Needs(Thirst: 1));
        sim.Scenario.PlaceVillage(new(40, 108), [resident.Id]);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Villages[0].VillagerIds);
        var known = new KnownMap { Villages = Array.AsReadOnly(new[] { new KnownVillage(0, new(40, 108), sim.Snapshot.Clock) }) };
        var band = sim.Scenario.PlaceBand([new(20, 108)], knownMap: known);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Null(sim.Snapshot.Villagers.Single(v => v.Id == band.VillagerIds[0]).VillageId);
        Assert.All(sim.Snapshot.Bands.SelectMany(b => b.KnownMap.Villages), v => Assert.True(v.Abandoned));
    }

    [Fact]
    public void Lost_shoppers_arrive_over_time_alone_and_in_small_Bands()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12,
            PillarLayout: PillarLayout.VillagerSandbox, StartingBands: false,
            Arrivals: new ArrivalConfig { IntervalHours = 0.25 }));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        Assert.Empty(sim.Snapshot.Villagers);
        sim.Advance(TimeSpan.FromHours(3));
        var arrivals = events.OfType<WanderersArrived>().ToArray();
        Assert.Equal(12, arrivals.Length);
        Assert.Contains(arrivals, e => e.Band.VillagerIds.Count == 1);
        Assert.Contains(arrivals, e => e.Band.VillagerIds.Count > 1);
        Assert.All(arrivals, e => Assert.InRange(e.Band.VillagerIds.Count, 1, 4));
        Assert.Contains(sim.Snapshot.Chronicle, s => s.Contains("arrived"));
        Assert.Contains(sim.Snapshot.Villagers, v => v.KnownMap.ExploredGround.Count > 1);
    }
}
