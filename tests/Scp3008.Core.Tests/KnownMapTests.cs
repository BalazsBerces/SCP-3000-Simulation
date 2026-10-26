using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class KnownMapTests
{
    [Fact]
    public void Walking_through_a_Department_discovers_its_Water_points()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        sim.Scenario.ClearFogBanks();
        sim.Scenario.SetFogBankFormation(false);
        var point = sim.Snapshot.WaterPoints.First(w => w.Kind == WaterPointKind.RestroomWashbasin);
        sim.Scenario.RemoveFurniture(p => p.Id != point.FurnitureId);
        var villager = sim.Scenario.PlaceVillager(point.Position + new Vector2(0, 45));
        sim.AdvanceTicks(1);
        Assert.Empty(Assert.Single(sim.Snapshot.Villagers).KnownMap.Water);
        sim.Scenario.SendVillager(villager.Id, point.Position + new Vector2(0, 5));
        sim.Advance(TimeSpan.FromMinutes(45));
        Assert.Contains(Assert.Single(sim.Snapshot.Villagers).KnownMap.Water,
            w => w.FurnitureId == point.FurnitureId && w.Running == true);
    }

    [Fact]
    public void Scouting_records_seen_Staff_as_dangerous_places()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("staff nearby");
        var staff = Assert.Single(sim.Snapshot.Staff);
        var villager = sim.Scenario.PlaceVillager(staff.Position + new Vector2(0, 4));
        sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villagers.Single(v => v.Id == villager.Id).KnownMap.Dangers,
            d => d.StaffId == staff.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sight_discovers_sources_across_Seams_but_not_through_thick_Fog_banks(bool fogged)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox));
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new Vector2(2, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 3);
        sim.Scenario.PlaceVillager(new Vector2(120, 108));
        if (fogged)
        {
            sim.Scenario.HoldWind(Vector2.Zero);
            sim.Scenario.PlaceFogBank(new Vector2(125, 108), 20);
        }
        sim.AdvanceTicks(1);
        Assert.Equal(!fogged, Assert.Single(sim.Snapshot.Villagers).KnownMap.Goods.Any());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Unseen_emptied_or_restocked_sources_go_stale_and_refresh_only_on_return(int amount)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Restock: new RestockConfig { GoodsPerStoreHour = 0 }));
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new Vector2(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 3);
        var villager = sim.Scenario.PlaceVillager(new Vector2(20, 104));
        sim.AdvanceTicks(1);
        var before = Assert.Single(sim.Snapshot.Villagers).KnownMap;
        sim.Scenario.SendVillager(villager.Id, new Vector2(75, 30));
        sim.Advance(TimeSpan.FromHours(2));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, amount);
        sim.AdvanceTicks(51);
        var stale = Assert.Single(Assert.Single(sim.Snapshot.Villagers).KnownMap.Goods);
        Assert.True(stale.Stale);
        Assert.Equal(3, stale.Goods.Amount);
        sim.Scenario.SendVillager(villager.Id, new Vector2(20, 104));
        sim.Advance(TimeSpan.FromHours(1.5));
        var refreshed = Assert.Single(Assert.Single(sim.Snapshot.Villagers).KnownMap.Goods);
        Assert.False(refreshed.Stale);
        Assert.Equal(amount, refreshed.Goods.Amount);
        Assert.True(refreshed.LastSeen.Tick > stale.LastSeen.Tick);
        Assert.Equal(3, Assert.Single(before.Goods).Goods.Amount);
        Assert.False(Assert.Single(before.Goods).Stale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Showroom_taps_reveal_whether_they_run_only_after_being_tried(bool running)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1));
        sim.Scenario.ClearFogBanks();
        sim.Scenario.SetFogBankFormation(false);
        var point = sim.Snapshot.WaterPoints.First(w => w.Kind == WaterPointKind.ShowroomTap && w.Running == running);
        sim.Scenario.RemoveFurniture(p => p.Id != point.FurnitureId);
        sim.Scenario.PlaceVillager(point.Position, new Needs(Thirst: 0.45));
        sim.AdvanceTicks(1);
        var before = Assert.Single(sim.Snapshot.Villagers).KnownMap;
        Assert.Null(Assert.Single(before.Water, w => w.FurnitureId == point.FurnitureId).Running);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        var known = Assert.Single(Assert.Single(sim.Snapshot.Villagers).KnownMap.Water, w => w.FurnitureId == point.FurnitureId);
        Assert.Equal(running, known.Running);
        Assert.Equal(running, events.OfType<WaterDrunk>().Any(e => e.WaterPoint == point));
        Assert.Null(Assert.Single(before.Water, w => w.FurnitureId == point.FurnitureId).Running);
    }

    [Fact]
    public void Hunger_sends_a_Villager_back_to_known_food_rather_than_a_nearer_unknown_shelf()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandboxJson("""
            {"furniture":[
              {"type":"GroceryShelf","position":[20,108],"stock":[{"kind":"TinnedFood","amount":3}]},
              {"type":"GroceryShelf","position":[75,58],"stock":[{"kind":"FreshFood","amount":2}]}]}
            """);
        var villager = sim.Scenario.PlaceVillager(new Vector2(20, 104), new Needs(Hunger: 0.45));
        sim.Scenario.SendVillager(villager.Id, new Vector2(75, 104));
        sim.Advance(TimeSpan.FromHours(1.3));
        var hungry = Assert.Single(sim.Snapshot.Villagers);
        Assert.Equal(VillagerAction.SeekingFood, hungry.Action);
        Assert.True(sim.Snapshot.Space.Distance(hungry.Destination!.Value, new Vector2(20, 108)) < 3);
        Assert.DoesNotContain(hungry.KnownMap.Goods, g => g.Goods.Kind == GoodsKind.FreshFood);
    }

    [Fact]
    public void Hunger_with_no_known_source_scouts_unexplored_ground()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var villager = sim.Scenario.PlaceVillager(new Vector2(20, 108), new Needs(Hunger: 0.8));
        sim.Advance(TimeSpan.FromMinutes(20));
        var scout = Assert.Single(sim.Snapshot.Villagers);
        Assert.Equal(VillagerAction.Scouting, scout.Action);
        Assert.NotEqual(villager.Position, scout.Position);
        Assert.True(scout.KnownMap.ExploredGround.Count > 1);
    }

    [Fact]
    public void Walking_discovers_local_sources_but_not_unvisited_ground_or_another_Villagers_knowledge()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandboxJson("""
            {"furniture":[
              {"type":"GroceryShelf","position":[30,100],"stock":[{"kind":"TinnedFood","amount":3}]},
              {"type":"GroceryShelf","position":[85,45],"stock":[{"kind":"FreshFood","amount":2}]}]}
            """);
        var walker = sim.Scenario.PlaceVillager(new Vector2(10, 100));
        var other = sim.Scenario.PlaceVillager(new Vector2(85, 45));
        sim.Scenario.SendVillager(walker.Id, new Vector2(40, 100));
        sim.Advance(TimeSpan.FromMinutes(35));
        var map = sim.Snapshot.Villagers.Single(v => v.Id == walker.Id).KnownMap;
        Assert.Contains(map.Goods, g => g.Goods.Kind == GoodsKind.TinnedFood);
        Assert.DoesNotContain(map.Goods, g => g.Goods.Kind == GoodsKind.FreshFood);
        Assert.True(map.ExploredGround.Count > 1);
        Assert.DoesNotContain(map.ExploredGround, p => p == new Vector2(84, 44));
        Assert.DoesNotContain(sim.Snapshot.Villagers.Single(v => v.Id == other.Id).KnownMap.Goods,
            g => g.Goods.Kind == GoodsKind.TinnedFood);
    }
}
