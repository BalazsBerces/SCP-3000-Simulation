using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class BandTests
{
    [Fact]
    public void Starting_Bands_explore_the_full_Store_through_the_facade()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromHours(9), sim.Snapshot.Clock.TimeOfDay);
        Assert.Contains(sim.Snapshot.Villagers, v => v.KnownMap.ExploredGround.Count > 1);
        Assert.All(sim.Snapshot.Villages, v => Assert.False(sim.Snapshot.GroundAt(v.Position).HasFlag(GroundProperty.Darkness)));
    }

    [Fact]
    public void Village_food_stores_feed_members_when_the_shelf_is_gone()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        sim.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.True(Assert.Single(sim.Snapshot.Villages).Stores.GetValueOrDefault(GoodsKind.TinnedFood) > 0);
        sim.Scenario.RemoveFurniture(_ => true);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(14));
        Assert.Contains(events.OfType<GoodsConsumed>(), e => e.FurnitureId == -1 && e.Kind == GoodsKind.TinnedFood);
    }

    [Fact]
    public void A_Pillar_site_beats_an_equal_site_away_from_the_Pillar()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        // Both sites have the same food, water and Furniture; only one is near the Pillar.
        foreach (var position in new[] { new Vector2(20, 108), new Vector2(40, 40) })
        {
            var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, position);
            sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
            sim.Scenario.PlaceWaterPoint(position + new Vector2(2, 0));
        }
        sim.Scenario.PlaceBand([new(20, 104), new(40, 36)]);
        sim.Advance(TimeSpan.FromHours(3));
        var village = Assert.Single(sim.Snapshot.Villages);
        Assert.InRange(sim.Snapshot.Space.Distance(village.Position, new Vector2(64)), 0, 48);
    }

    [Fact]
    public void Even_a_desperate_Band_never_founds_in_the_dead_zone()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, LandmarkShowcase: true));
        var dead = sim.Snapshot.Landmarks.Single(l => l.Ground.HasFlag(GroundProperty.Darkness));
        sim.Scenario.RemoveFurniture(_ => true);
        var safe = dead.Centre + new Vector2(dead.Size.X / 2 + 12, 0);
        sim.Scenario.PlaceBand([dead.Centre, safe], new Needs(Rest: 0.1));
        sim.Advance(TimeSpan.FromHours(4));
        var village = Assert.Single(sim.Snapshot.Villages);
        Assert.False(sim.Snapshot.GroundAt(village.Position).HasFlag(GroundProperty.Darkness));
    }

    [Fact]
    public void Food_shortage_switches_a_Builder_to_scavenging_and_Goods_are_carried_home()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        sim.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.AdvanceTicks(1);
        var founding = sim.Snapshot;
        Assert.Contains(founding.Villagers, v => v.Role == Role.Builder);
        for (var tick = 0; tick < 500 && sim.Snapshot.Villagers.All(v => v.CarriedGoods is null); tick++)
            sim.AdvanceTicks(1);
        Assert.Contains(events.OfType<RoleChanged>(), e => e.VillagerId == founding.Villagers[1].Id && e.Role == Role.Scavenger);
        Assert.Contains(sim.Snapshot.Villagers, v => v.CarriedGoods == GoodsKind.TinnedFood);
        Assert.Empty(Assert.Single(sim.Snapshot.Villages).Stores);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.True(Assert.Single(sim.Snapshot.Villages).Stores[GoodsKind.TinnedFood] > 0);
        Assert.Contains(events.OfType<GoodsDelivered>(), e => e.Kind == GoodsKind.TinnedFood);
        Assert.Contains(sim.Snapshot.Villagers, v => v.Skills.GetValueOrDefault(Role.Scavenger) > 0);
        Assert.Empty(Assert.Single(founding.Villages).Stores);
    }

    [Fact]
    public void Starting_Bands_use_runtime_randomness_and_different_safe_Departments()
    {
        using var first = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 12));
        using var second = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 13));
        Assert.InRange(first.Snapshot.Bands.Count, 4, 5);
        Assert.Empty(first.Snapshot.Villages);
        Assert.All(first.Snapshot.Bands, b =>
        {
            Assert.InRange(b.VillagerIds.Count, 2, 6);
            Assert.False(first.Snapshot.GroundAt(b.Position).HasFlag(GroundProperty.Darkness));
        });
        Assert.Equal(first.Snapshot.Bands.Count, first.Snapshot.Bands.Select(b => first.Snapshot.Departments.At(b.Position).Id).Distinct().Count());
        Assert.NotEqual(first.Snapshot.Bands.Select(b => b.Position), second.Snapshot.Bands.Select(b => b.Position));
        Assert.Equal(first.Snapshot.Furniture, second.Snapshot.Furniture);
    }

    [Fact]
    public void A_healthy_Band_scouts_shares_knowledge_and_rests_through_Closing_time()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        sim.Scenario.PlaceBand([new(20, 104), new(24, 104)]);
        sim.Advance(TimeSpan.FromMinutes(20));
        Assert.Empty(sim.Snapshot.Villages);
        Assert.Contains(sim.Snapshot.Villagers, v => v.Action == VillagerAction.Scouting);
        Assert.True(Assert.Single(sim.Snapshot.Bands).KnownMap.ExploredGround.Count > 1);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
        sim.Advance(TimeSpan.FromMinutes(10));
        var before = sim.Snapshot.Villagers.Select(v => v.Position).ToArray();
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(before, sim.Snapshot.Villagers.Select(v => v.Position));
        Assert.All(sim.Snapshot.Villagers, v => Assert.Equal(VillagerAction.Resting, v.Action));
    }

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(1, 0.9)]
    public void Exhaustion_or_hunger_forces_settlement_at_a_weaker_site(double rest, double hunger)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        sim.Scenario.PlaceBand([new(20, 104), new(22, 104)], new Needs(Rest: rest, Hunger: hunger));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(sim.Snapshot.Villages);
        Assert.True(Assert.Single(events.OfType<VillageFounded>()).Forced);
    }

    [Fact]
    public void A_Band_founds_a_Village_at_known_food_and_water_and_keeps_its_knowledge()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        var band = sim.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromMinutes(30));

        var village = Assert.Single(sim.Snapshot.Villages);
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Equal(band.VillagerIds, village.VillagerIds);
        Assert.InRange(sim.Snapshot.Space.Distance(village.Position, new(20, 108)), 0, 48);
        Assert.Contains(village.KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
        Assert.Contains(village.KnownMap.Water, w => w.Running == true);
        Assert.All(sim.Snapshot.Villagers, v => Assert.Equal(village.Id, v.VillageId));
        Assert.Single(events.OfType<VillageFounded>());
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("founded Village"));
    }
}
