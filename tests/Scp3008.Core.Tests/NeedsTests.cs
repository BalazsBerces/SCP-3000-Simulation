using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class NeedsTests
{
    [Fact]
    public void Hungry_and_thirsty_Villagers_walk_to_Goods_and_deplete_the_shelves()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("scavenge");
        var before = sim.Snapshot;
        sim.Advance(TimeSpan.FromHours(5));
        Assert.True(sim.Snapshot.Stock.Sum(g => g.Amount) < before.Stock.Sum(g => g.Amount));
        Assert.Contains(sim.Snapshot.Villagers, v => v.Position != before.Villagers.Single(b => b.Id == v.Id).Position);
        Assert.All(sim.Snapshot.Villagers, v => Assert.True(v.Needs.Hunger < 0.5 && v.Needs.Thirst < 0.5));
        Assert.Equal(6, before.Stock.Sum(g => g.Amount));
    }

    [Theory]
    [InlineData(PointOfInterestType.Cafeteria)]
    [InlineData(PointOfInterestType.SetDinnerTable)]
    [InlineData(PointOfInterestType.AquariumAisle)]
    public void Villagers_eat_the_food_at_Points_of_interest(PointOfInterestType type)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        var point = sim.Snapshot.PointsOfInterest.First(p => p.Type == type);
        var ids = sim.Snapshot.Furniture.Where(p => p.PointOfInterestId == point.Id).Select(p => p.Id).ToHashSet();
        var food = sim.Snapshot.Stock.First(g => ids.Contains(g.FurnitureId) && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food);
        var holder = sim.Snapshot.Furniture.Single(p => p.Id == food.FurnitureId);
        sim.Scenario.PlaceVillager(holder.Position, new Needs(Hunger: 0.8));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(events, e => e is GoodsConsumed g && ids.Contains(g.FurnitureId));
        Assert.True(Assert.Single(sim.Snapshot.Villagers).Needs.Hunger < 0.5);
        Assert.Equal(food.Amount - 1, sim.Snapshot.Stock.Where(g => g.FurnitureId == holder.Id && g.Kind == food.Kind).Sum(g => g.Amount));
    }

    [Theory]
    [InlineData("Sweets")]
    [InlineData("Candles")]
    public void Comfort_goods_raise_morale_but_supply_no_food_or_water(string kind)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandboxJson("""
            {"furniture":[{"type":"GroceryShelf","position":[30,100],"stock":[{"kind":"KIND","amount":1}]}]}
            """.Replace("KIND", kind));
        var before = sim.Scenario.PlaceVillager(new Vector2(30, 100), new Needs(Hunger: 0.3, Thirst: 0.3, Morale: 0.1));
        sim.Advance(TimeSpan.FromMinutes(20));
        var after = Assert.Single(sim.Snapshot.Villagers);
        Assert.Empty(sim.Snapshot.Stock);
        Assert.True(after.Needs.Morale > before.Needs.Morale);
        Assert.True(after.Needs.Hunger >= before.Needs.Hunger);
        Assert.True(after.Needs.Thirst >= before.Needs.Thirst);
    }

    [Theory]
    [InlineData(WaterPointKind.RestroomWashbasin)]
    [InlineData(WaterPointKind.FountainCourt)]
    [InlineData(WaterPointKind.CampingLake)]
    [InlineData(WaterPointKind.ShowroomTap)]
    public void Villagers_drink_at_every_kind_of_running_Water_point(WaterPointKind kind)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1,
            LandmarkShowcase: kind is WaterPointKind.FountainCourt or WaterPointKind.CampingLake));
        var water = sim.Snapshot.WaterPoints.First(w => w.Kind == kind && w.Running);
        sim.Scenario.PlaceVillager(water.Position, new Needs(Thirst: 0.8));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(events, e => e is WaterDrunk d && d.WaterPoint == water);
        Assert.True(Assert.Single(sim.Snapshot.Villagers).Needs.Thirst < 0.2);
    }

    [Fact]
    public void Dry_taps_supply_no_water()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, Water: new WaterConfig { RunningTapShare = 0 }));
        var water = sim.Snapshot.WaterPoints.First(w => w.Kind == WaterPointKind.ShowroomTap &&
            sim.Snapshot.WaterPoints.Where(p => p.Running).All(p => sim.Snapshot.Space.Distance(w.Position, p.Position) > 100));
        sim.Scenario.RemoveFurniture(p => p.Id != water.FurnitureId);
        sim.Scenario.PlaceVillager(water.Position, new Needs(Thirst: 0.8));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.DoesNotContain(events, e => e is WaterDrunk);
        Assert.True(Assert.Single(sim.Snapshot.Villagers).Needs.Thirst > 0.8);
    }

    [Theory]
    [InlineData(DeathCause.Starvation)]
    [InlineData(DeathCause.Thirst)]
    public void Prolonged_deprivation_kills_once_and_leaves_a_body_after_the_configured_period(DeathCause cause)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Needs: new NeedsConfig(StarvationHours: 2, DehydrationHours: 3)));
        var villager = sim.Scenario.PlaceVillager(new Vector2(30, 100),
            cause == DeathCause.Starvation ? new Needs(Hunger: 1) : new Needs(Thirst: 1));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        var period = TimeSpan.FromHours(cause == DeathCause.Starvation ? 2 : 3);
        sim.Advance(period - StoreClock.TickDuration);
        Assert.Single(sim.Snapshot.Villagers);
        sim.Advance(StoreClock.TickDuration);
        Assert.Empty(sim.Snapshot.Villagers);
        var body = Assert.Single(sim.Snapshot.Bodies);
        Assert.Equal(villager.Id, body.VillagerId);
        Assert.Equal(villager.Position, body.Position);
        Assert.Equal(cause, body.Cause);
        Assert.Equal(body, Assert.Single(events.OfType<VillagerDied>()).Body);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Single(events.OfType<VillagerDied>());
        Assert.NotEqual(villager.Id, sim.Scenario.PlaceVillager(new Vector2(30, 100)).Id);
    }

    [Fact]
    public void Exhausted_Villagers_rest_faster_inside_the_armchair_henge()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        var henge = sim.Snapshot.PointsOfInterest.First(p => p.Type == PointOfInterestType.ArmchairHenge);
        var inside = sim.Scenario.PlaceVillager(henge.Centre, new Needs(Rest: 0.1));
        var outside = sim.Scenario.PlaceVillager(new Vector2(100, 100), new Needs(Rest: 0.1));
        sim.Advance(TimeSpan.FromHours(2));
        var faster = sim.Snapshot.Villagers.Single(v => v.Id == inside.Id);
        var slower = sim.Snapshot.Villagers.Single(v => v.Id == outside.Id);
        Assert.Equal(VillagerAction.Resting, faster.Action);
        Assert.Equal(VillagerAction.Resting, slower.Action);
        Assert.True(faster.Needs.Rest > slower.Needs.Rest + 0.2);
    }

    [Fact]
    public void Only_one_Villager_can_eat_the_last_portion()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandboxJson("""
            {"furniture":[{"type":"GroceryShelf","position":[30,100],"stock":[{"kind":"TinnedFood","amount":1}]}]}
            """);
        for (var i = 0; i < 3; i++)
            sim.Scenario.PlaceVillager(new Vector2(30, 100), new Needs(Hunger: 0.8));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Single(events.OfType<GoodsConsumed>());
        Assert.Empty(sim.Snapshot.Stock);
        Assert.Single(sim.Snapshot.Villagers.Where(v => v.Needs.Hunger < 0.5));
    }

    [Fact]
    public void A_hungry_Villager_crosses_a_Seam_to_eat_and_resets_starvation()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandboxJson("""
            {"needsConfig":{"starvationHours":1},
             "furniture":[{"type":"GroceryShelf","position":[1,100],"stock":[{"kind":"TinnedFood","amount":1}]}]}
            """);
        sim.Scenario.PlaceVillager(new Vector2(127, 100), new Needs(Hunger: 1));
        sim.Advance(TimeSpan.FromHours(2));
        var villager = Assert.Single(sim.Snapshot.Villagers);
        Assert.Empty(sim.Snapshot.Bodies);
        Assert.Empty(sim.Snapshot.Stock);
        Assert.True(villager.Needs.Hunger < 0.5);
        Assert.InRange(villager.Position.X, 0, 3);
    }

    [Fact]
    public void Display_Fridges_are_empty_except_for_actual_left_behind_finds()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, Stock: new StockConfig { LeftBehindShare = 1 }));
        var pieces = sim.Snapshot.Furniture.ToDictionary(p => p.Id);
        var find = sim.Snapshot.Stock.First(g => g.LeftBehind && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food &&
            pieces[g.FurnitureId].Type == FurnitureType.Fridge);
        sim.Scenario.RemoveFurniture(p => p.Id != find.FurnitureId);
        sim.Scenario.PlaceVillager(pieces[find.FurnitureId].Position, new Needs(Hunger: 0.8));
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Empty(sim.Snapshot.Stock);
        Assert.True(Assert.Single(sim.Snapshot.Villagers).Needs.Hunger < 0.5);
        sim.Advance(TimeSpan.FromHours(20));
        Assert.Empty(sim.Snapshot.Stock);
        Assert.True(Assert.Single(sim.Snapshot.Villagers).Needs.Hunger > 0.8);
    }

    [Fact]
    public void Safety_and_morale_fall_at_Closing_time_and_Villagers_move_away_from_nearby_Staff()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("staff nearby");
        var staff = Assert.Single(sim.Snapshot.Staff);
        var start = staff.Position + new Vector2(0, 4);
        var before = sim.Scenario.PlaceVillager(start);
        sim.Advance(TimeSpan.FromMinutes(5));
        var after = sim.Snapshot.Villagers.Single(v => v.Id == before.Id);
        Assert.True(after.Needs.Safety < before.Needs.Safety);
        Assert.True(after.Needs.Morale < before.Needs.Morale);
        Assert.Equal(VillagerAction.SeekingSafety, after.Action);
        Assert.True(sim.Snapshot.Space.Distance(start, after.Position) > 1);
    }

    [Fact]
    public void Starving_preset_shows_consumption_and_death_while_scavenge_supplies_everyone()
    {
        using var starving = ScenarioBuilder.LoadVillagerSandbox("starving");
        using var scavenge = ScenarioBuilder.LoadVillagerSandbox("scavenge");
        var events = new List<DomainEvent>();
        using var subscription = starving.Subscribe(events.Add);
        starving.Advance(TimeSpan.FromHours(3));
        scavenge.Advance(TimeSpan.FromHours(3));
        Assert.Equal(2, starving.Snapshot.Bodies.Count);
        Assert.Contains(events, e => e is GoodsConsumed g && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food);
        Assert.Contains(events, e => e is GoodsConsumed g && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Drink);
        Assert.Contains(events, e => e is VillagerDied);
        Assert.Equal(3, scavenge.Snapshot.Villagers.Count);
        Assert.Empty(scavenge.Snapshot.Bodies);
        Assert.All(scavenge.Snapshot.Villagers, v => Assert.True(v.Needs.Hunger < 0.5 && v.Needs.Thirst < 0.5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_needs_and_death_periods_are_rejected(double value)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.PlaceVillager(new Vector2(30, 100), new Needs(Hunger: value)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new WorldConfig(3008, Needs: new NeedsConfig(value))));
        Assert.Empty(sim.Snapshot.Villagers);
    }

    [Fact]
    public void Needs_change_with_Store_time()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var before = sim.Scenario.PlaceVillager(new Vector2(30, 100));
        sim.Advance(TimeSpan.FromHours(2));
        var after = Assert.Single(sim.Snapshot.Villagers);
        Assert.True(after.Needs.Hunger > before.Needs.Hunger);
        Assert.True(after.Needs.Thirst > before.Needs.Thirst);
        Assert.True(after.Needs.Rest < before.Needs.Rest);
        Assert.True(after.Needs.Morale < before.Needs.Morale);
    }
}
