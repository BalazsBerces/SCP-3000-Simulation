using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class MessHallTests
{
    private static Simulation Village(int people = 3)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104), new Needs(Morale: 0.3)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        // The existing Workshop queue takes these working surfaces first.
        for (var i = 0; i < 4; i++) sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(28 + i * 3, 104));
        sim.Scenario.PlaceFurniture(FurnitureType.Chair, new(28, 110));
        sim.Scenario.PlaceFurniture(FurnitureType.HallBench, new(31, 110));
        sim.Scenario.PlaceFurniture(FurnitureType.KitchenCabinet, new(35, 110));
        return sim;
    }

    [Fact]
    public void Builders_haul_a_Mess_hall_Blueprint_and_Villagers_gather_before_Closing_time()
    {
        using var sim = Village();
        for (var i = 0; i < 10000 && sim.Snapshot.Villages[0].MessHall?.Complete != true; i++) sim.AdvanceTicks(1);
        var hall = sim.Snapshot.Villages[0].MessHall;
        Assert.NotNull(hall);
        Assert.True(hall.Complete);
        Assert.Equal(4, hall.Slots.Count(s => s.FurnitureId is not null));
        foreach (var v in sim.Snapshot.Villagers) sim.Scenario.SendVillager(v.Id, new(20, 104));
        sim.Advance(TimeSpan.FromHours(1));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(20));
        sim.Advance(TimeSpan.FromMinutes(35));
        Assert.All(sim.Snapshot.Villagers, v => Assert.Equal(VillagerAction.SharedMeal, v.Action));
        Assert.All(sim.Snapshot.Villagers, v => Assert.InRange(sim.Snapshot.Space.Distance(v.Position, hall.Position), 0, 5));
    }

    private static Simulation Ready(int diners = 3, bool cook = false)
    {
        var sim = Village(4);
        for (var i = 0; i < 10000 && sim.Snapshot.Villages[0].MessHall?.Complete != true; i++) sim.AdvanceTicks(1);
        Assert.True(sim.Snapshot.Villages[0].MessHall!.Complete);
        foreach (var v in sim.Snapshot.Villagers)
        {
            sim.Scenario.SetNeeds(v.Id, new Needs(Morale: 0.3));
            sim.Scenario.AssignRole(v.Id, cook && v.Id == 2 ? Role.Cook : Role.Guard);
            sim.Scenario.SendVillager(v.Id, v.Id < diners ? new(20, 104) : new(80, 45));
        }
        sim.Scenario.PlaceWaterPoint(new(80, 45));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(16));
        sim.Advance(TimeSpan.FromHours(2));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(20));
        return sim;
    }

    [Fact]
    public void Eating_with_more_people_raises_morale_more_than_a_solitary_meal()
    {
        static double Gain(int diners)
        {
            using var sim = Ready(diners);
            var before = sim.Snapshot.Villagers[0].Needs.Morale;
            sim.Advance(TimeSpan.FromMinutes(35));
            Assert.Equal(VillagerAction.SharedMeal, sim.Snapshot.Villagers[0].Action);
            return sim.Snapshot.Villagers[0].Needs.Morale - before;
        }
        var alone = Gain(1);
        var pair = Gain(2);
        var group = Gain(4);
        Assert.True(pair > alone);
        Assert.True(group > pair);
    }

    [Fact]
    public void A_cook_feeds_the_same_people_with_fewer_Goods()
    {
        static SharedMealServed Meal(bool cook)
        {
            using var sim = Ready(3, cook);
            var meals = new List<SharedMealServed>();
            using var sub = sim.Subscribe(e => { if (e is SharedMealServed m) meals.Add(m); });
            sim.Advance(TimeSpan.FromMinutes(35));
            return Assert.Single(meals);
        }
        var plain = Meal(false);
        var cooked = Meal(true);
        Assert.Equal(3, plain.VillagerIds.Count);
        Assert.Equal(plain.VillagerIds, cooked.VillagerIds);
        Assert.Equal(3, plain.GoodsUsed);
        Assert.Equal(2, cooked.GoodsUsed);
    }

    [Fact]
    public void Only_people_at_the_table_learn_a_returning_Villagers_news_and_the_traveller_eats_alone()
    {
        using var sim = Ready();
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(80, 48));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        // The fourth member is away; only Mara visits the far shelf and brings its news back.
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        sim.Scenario.SendVillager(0, new(80, 45));
        sim.Advance(TimeSpan.FromHours(2));
        Assert.Contains(sim.Snapshot.Villagers[0].KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
        sim.Scenario.SendVillager(0, new(20, 104));
        sim.Advance(TimeSpan.FromHours(2));
        Assert.DoesNotContain(sim.Snapshot.Villagers[1].KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
        // Elias' route is also news, which the distant fourth member must miss.
        sim.Scenario.SendVillager(1, new(12, 80));
        sim.Advance(TimeSpan.FromMinutes(40));
        sim.Scenario.SendVillager(1, new(20, 104));
        sim.Advance(TimeSpan.FromMinutes(40));
        var news = sim.Snapshot.Villagers[1].KnownMap.ExploredGround.Except(sim.Snapshot.Villagers[3].KnownMap.ExploredGround).ToArray();
        Assert.NotEmpty(news);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(20));
        sim.Scenario.SetNeeds(3, new Needs(Hunger: 0.6, Morale: 0.3));
        var consumed = new List<GoodsConsumed>();
        using var sub = sim.Subscribe(e => { if (e is GoodsConsumed g) consumed.Add(g); });
        sim.Advance(TimeSpan.FromMinutes(35));
        Assert.Contains(sim.Snapshot.Villagers[1].KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
        Assert.DoesNotContain(sim.Snapshot.Villagers[3].KnownMap.ExploredGround, p => news.Contains(p));
        Assert.NotEqual(VillagerAction.SharedMeal, sim.Snapshot.Villagers[3].Action);
        Assert.Contains(consumed, g => g.VillagerId == 3 && g.FurnitureId == shelf.Id);
    }

    [Fact]
    public void Gathering_and_served_meals_survive_loading_without_a_second_reward()
    {
        using var sim = Ready(3, true);
        sim.Advance(TimeSpan.FromMinutes(10));
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Villages[0].MessHall!.Slots, loaded.Snapshot.Villages[0].MessHall!.Slots);
        sim.Advance(TimeSpan.FromMinutes(25));
        loaded.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(sim.Snapshot.Villages[0].Stores, loaded.Snapshot.Villages[0].Stores);
        Assert.Equal(sim.Snapshot.Villages[0].MessHall!.LastMealTick, loaded.Snapshot.Villages[0].MessHall!.LastMealTick);
        Assert.Equal(sim.Snapshot.Villagers.Select(v => v.Needs), loaded.Snapshot.Villagers.Select(v => v.Needs));
        using var served = new MemoryStream(); loaded.Save(served); served.Position = 0;
        using var resumed = Simulation.Load(served);
        var stores = resumed.Snapshot.Villages[0].Stores[GoodsKind.TinnedFood];
        resumed.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(stores, resumed.Snapshot.Villages[0].Stores[GoodsKind.TinnedFood]);
        Assert.All(resumed.Snapshot.Villagers.Take(3), v => Assert.Equal(VillagerAction.SharedMeal, v.Action));
        foreach (var field in new[] { "FurnitureId", "ReservedFurnitureId" })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(served.ToArray())!;
            data["State"]!["Snapshot"]!["Villages"]![0]!["MessHall"]!["Slots"]![0]![field] = 999999;
            using var bad = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            Assert.Throws<InvalidDataException>(() => Simulation.Load(bad));
        }
        var table = resumed.Snapshot.Villages[0].MessHall!.Slots[0].FurnitureId;
        resumed.Scenario.RemoveFurniture(p => p.Id == table);
        Assert.False(resumed.Snapshot.Villages[0].MessHall!.Complete);
    }

    [Fact]
    public void A_reserved_Mess_hall_piece_survives_saving_during_the_haul()
    {
        using var sim = Village();
        for (var i = 0; i < 10000 && !(sim.Snapshot.Villages[0].MessHall?.Slots.Any(s => s.ReservedFurnitureId is not null) == true &&
            sim.Snapshot.FurnitureTasks.Any(t => t.Started && sim.Snapshot.Furniture.Any(p => p.Id == t.FurnitureId && p.Elevation > 0))); i++) sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villages[0].MessHall!.Slots, s => s.ReservedFurnitureId is not null);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        for (var i = 0; i < 10000 && loaded.Snapshot.Villages[0].MessHall?.Complete != true; i++) loaded.AdvanceTicks(1);
        Assert.True(loaded.Snapshot.Villages[0].MessHall!.Complete);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(14)]
    [InlineData(20)]
    public void Meals_follow_the_daily_schedule_and_end_after_the_table_time(int hour)
    {
        using var sim = Ready(4);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(hour));
        var meals = new List<SharedMealServed>();
        using var sub = sim.Subscribe(e => { if (e is SharedMealServed m) meals.Add(m); });
        sim.Advance(TimeSpan.FromMinutes(35));
        Assert.Equal(4, Assert.Single(meals).VillagerIds.Count);
        sim.Advance(TimeSpan.FromMinutes(15));
        Assert.DoesNotContain(sim.Snapshot.Villagers, v => v.Action == VillagerAction.SharedMeal);
        Assert.Single(meals);
    }

    [Fact]
    public void Work_keeps_a_Villager_from_the_meal_and_empty_stores_give_no_free_meal()
    {
        using var sim = Ready(4);
        sim.Advance(TimeSpan.FromMinutes(25));
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(18, 104));
        sim.Scenario.DisassembleFurniture(chair.Id, 0);
        sim.Advance(TimeSpan.FromMinutes(10));
        Assert.NotEqual(VillagerAction.SharedMeal, sim.Snapshot.Villagers[0].Action);
        Assert.All(sim.Snapshot.Villagers.Skip(1), v => Assert.Equal(VillagerAction.SharedMeal, v.Action));
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 0);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(9));
        var meals = new List<SharedMealServed>();
        using var sub = sim.Subscribe(e => { if (e is SharedMealServed m) meals.Add(m); });
        sim.Advance(TimeSpan.FromMinutes(35));
        Assert.Empty(meals);
    }

    [Fact]
    public void Joining_Wanderers_do_not_broadcast_news_to_members_away_from_the_Village()
    {
        using var sim = Ready(3);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(18));
        var news = new System.Numerics.Vector2(77, 77);
        sim.Scenario.PlaceBand([new(20, 104)], knownMap: new KnownMap
        {
            ExploredGround = [news],
            Villages = [new KnownVillage(0, new(20, 104), sim.Snapshot.Clock)],
        });
        sim.AdvanceTicks(51);
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Contains(news, sim.Snapshot.Villages[0].KnownMap.ExploredGround);
        Assert.DoesNotContain(news, sim.Snapshot.Villagers.Single(v => v.Id == 3).KnownMap.ExploredGround);
    }
}
