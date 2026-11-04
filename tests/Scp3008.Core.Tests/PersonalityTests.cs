using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PersonalityTests
{
    private const int Day = 12000;

    // A Village of people in the sandbox, stocked with food, in the given Department.
    private static Simulation Home(DepartmentType department, int people = 3, Personality personality = Personality.Cautious, int food = 1000)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids, personality);
        sim.Scenario.SetDepartment(0, department);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, food);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        return sim;
    }

    [Fact]
    public void A_Village_founded_by_a_cautious_Band_starts_cautious()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        sim.Scenario.PlaceBand([new(20, 104), new(21, 104), new(22, 104)], personality: Personality.Cautious);
        sim.Advance(TimeSpan.FromHours(3));
        var village = Assert.Single(sim.Snapshot.Villages);
        Assert.Equal(Personality.Cautious, village.Personality);
        Assert.Equal(DepartmentType.RestaurantAndFoodMarket, village.Department);
    }

    [Fact]
    public void A_prolonged_famine_shifts_a_Village_toward_raiding_and_the_Chronicle_tells_it()
    {
        using var sim = Home(DepartmentType.Workspaces, food: 0);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.AdvanceTicks(Day / 2);
        Assert.Equal(Personality.Cautious, sim.Snapshot.Villages[0].Personality);
        sim.AdvanceTicks(Day);
        Assert.Equal(Personality.Raiders, sim.Snapshot.Villages[0].Personality);
        var shift = Assert.Single(events.OfType<PersonalityChanged>());
        Assert.Equal((Personality.Cautious, Personality.Raiders), (shift.From, shift.To));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("Village 0") && line.Contains("raiders"));
    }

    [Fact]
    public void A_short_hunger_that_ends_does_not_change_the_Personality()
    {
        using var sim = Home(DepartmentType.Workspaces, food: 0);
        sim.AdvanceTicks(Day / 2);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.AdvanceTicks(Day);
        Assert.Equal(Personality.Cautious, sim.Snapshot.Villages[0].Personality);
    }

    [Fact]
    public void A_Warehouse_Village_is_Material_rich()
    {
        static int Wood(DepartmentType type)
        {
            using var sim = Home(type);
            sim.AdvanceTicks(Day);
            return sim.Snapshot.Villages[0].Materials.GetValueOrDefault(MaterialKind.WoodPanel);
        }
        Assert.True(Wood(DepartmentType.SelfServeWarehouse) >= 10);
        Assert.Equal(0, Wood(DepartmentType.Workspaces));
    }

    [Fact]
    public void A_Restaurant_Village_is_a_trade_hub_that_gains_food_from_neighbours()
    {
        static int Food(DepartmentType type)
        {
            using var sim = Home(type, food: 100);
            var other = sim.Scenario.PlaceVillager(new(60, 104));
            sim.Scenario.PlaceVillage(other.Position, [other.Id]);
            sim.Scenario.SetDepartment(1, DepartmentType.Workspaces);
            sim.AdvanceTicks(Day);
            return sim.Snapshot.Villages[0].Stores.GetValueOrDefault(GoodsKind.TinnedFood);
        }
        Assert.True(Food(DepartmentType.RestaurantAndFoodMarket) > Food(DepartmentType.Workspaces));
    }

    [Fact]
    public void A_Bedroom_Village_has_high_morale()
    {
        static double Morale(DepartmentType type)
        {
            using var sim = Home(type);
            foreach (var v in sim.Snapshot.Villagers) sim.Scenario.SetNeeds(v.Id, new Needs(Morale: 0.3));
            sim.AdvanceTicks(Day / 4);
            return sim.Snapshot.Villagers.Average(v => v.Needs.Morale);
        }
        Assert.True(Morale(DepartmentType.Showrooms) > Morale(DepartmentType.Workspaces) + 0.2);
    }

    [Fact]
    public void A_Bedroom_Village_defends_weakly_its_Guard_is_struck_sooner()
    {
        static long FirstWound(DepartmentType type)
        {
            using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17,
                Restock: new RestockConfig { FurnitureHours = 0 }));
            var at = new Vector2(30, 104);
            var guard = sim.Scenario.PlaceVillager(at + new Vector2(-1, -1));
            var village = sim.Scenario.PlaceVillage(at, [guard.Id], Personality.Cautious);
            sim.Scenario.SetDepartment(village.Id, type);
            sim.Scenario.AssignRole(guard.Id, Role.Guard);
            sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
            sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
            sim.Scenario.PlanBarricade(village.Id);
            sim.Scenario.CompleteWall(village.Id, 0);
            var outside = at + new Vector2(18, 0);
            sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
            VillagerWounded? wound = null;
            using var sub = sim.Subscribe(e => { if (e is VillagerWounded w) wound ??= w; });
            for (var i = 0; i < 600 && wound is null; i++) sim.Advance(TimeSpan.FromSeconds(30));
            return Assert.IsType<VillagerWounded>(wound).At.Tick;
        }
        Assert.True(FirstWound(DepartmentType.Showrooms) < FirstWound(DepartmentType.Workspaces));
    }

    [Fact]
    public void A_Kitchen_Village_cooks_better_a_cook_feeds_more_people_per_Goods()
    {
        static int Used(DepartmentType type)
        {
            using var sim = Home(type, 4);
            sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(28, 104));
            for (var i = 1; i < 4; i++) sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(28 + i * 3, 104));
            sim.Scenario.PlaceFurniture(FurnitureType.Chair, new(28, 110));
            sim.Scenario.PlaceFurniture(FurnitureType.HallBench, new(31, 110));
            sim.Scenario.PlaceFurniture(FurnitureType.KitchenCabinet, new(35, 110));
            for (var i = 0; i < 10000 && sim.Snapshot.Villages[0].MessHall?.Complete != true; i++) sim.AdvanceTicks(1);
            foreach (var v in sim.Snapshot.Villagers)
            {
                sim.Scenario.SetNeeds(v.Id, new Needs(Morale: 0.3));
                sim.Scenario.AssignRole(v.Id, v.Id == 2 ? Role.Cook : Role.Guard);
                sim.Scenario.SendVillager(v.Id, v.Id < 3 ? new(20, 104) : new(80, 45));
            }
            sim.Scenario.PlaceWaterPoint(new(80, 45));
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(16));
            sim.Advance(TimeSpan.FromHours(2));
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(20));
            var meals = new List<SharedMealServed>();
            using var sub = sim.Subscribe(e => { if (e is SharedMealServed m) meals.Add(m); });
            sim.Advance(TimeSpan.FromMinutes(35));
            return Assert.Single(meals).GoodsUsed;
        }
        Assert.True(Used(DepartmentType.BathroomsAndKitchens) < Used(DepartmentType.Workspaces));
    }

    [Fact]
    public void A_Lighting_Village_glows_its_lights_reach_further()
    {
        static float Radius(DepartmentType type)
        {
            using var sim = Home(type, 1);
            sim.Scenario.AssignRole(0, Role.Guard);
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
            sim.Scenario.EquipGear(0, GearKind.Candle);
            sim.Advance(TimeSpan.FromMinutes(1));
            return Assert.Single(sim.Snapshot.LitAreas).Radius;
        }
        Assert.True(Radius(DepartmentType.LightingAndElectrical) > Radius(DepartmentType.Workspaces));
    }

    [Fact]
    public void A_Plant_section_Village_leans_toward_farming_Crops()
    {
        static int Farmers(DepartmentType type)
        {
            using var sim = Home(type, 6);
            foreach (var v in sim.Snapshot.Villagers) sim.Scenario.AssignRole(v.Id, Role.Scavenger);
            sim.Scenario.PlaceWaterPoint(new(26, 104));
            sim.Scenario.PlaceFurniture(FurnitureType.Crop, new(22, 108));
            sim.AdvanceTicks(2000);
            return sim.Snapshot.Villagers.Count(v => v.Role == Role.Farmer);
        }
        Assert.True(Farmers(DepartmentType.PlantSection) > Farmers(DepartmentType.Workspaces));
    }

    [Fact]
    public void Department_and_famine_survive_a_save()
    {
        using var sim = Home(DepartmentType.PlantSection, food: 0);
        sim.AdvanceTicks(Day / 2);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(DepartmentType.PlantSection, loaded.Snapshot.Villages[0].Department);
        Assert.Equal(sim.Snapshot.Villages[0].FamineTicks, loaded.Snapshot.Villages[0].FamineTicks);
    }
}
