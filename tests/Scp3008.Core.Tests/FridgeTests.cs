using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class FridgeTests
{
    private static Simulation Home()
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var person = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(person.Position, [person.Id]);
        sim.Scenario.AssignRole(person.Id, Role.Guard);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 100);
        return sim;
    }

    [Fact]
    public void Taken_food_in_a_display_Fridge_does_not_spoil_even_at_Closing_time()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(20, 108));
        sim.Scenario.PutBack(fridge.Id, GoodsKind.FreshFood, 12);
        sim.Advance(TimeSpan.FromDays(2));
        Assert.Equal(12, sim.Snapshot.Stock.Single(g => g.FurnitureId == fridge.Id).Amount);
    }

    [Fact]
    public void Moved_Fridge_protects_Village_food_only_until_its_battery_runs_out_in_open_hours()
    {
        using var sim = Home();
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        sim.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
        sim.Scenario.SetStores(0, GoodsKind.FreshFood, 12);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 1);
        var spoiled = 0;
        using var events = sim.Subscribe(e => { if (e is FoodSpoiled { Kind: GoodsKind.FreshFood } f) spoiled += f.Amount; });
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        sim.Advance(TimeSpan.FromHours(3));
        Assert.False(sim.Snapshot.Villages[0].Stores.ContainsKey(GoodsKind.Batteries));
        Assert.Equal(0, spoiled);
        sim.Advance(TimeSpan.FromHours(4));
        Assert.True(spoiled > 0);
    }

    [Fact]
    public void A_generator_keeps_a_Fridge_cold_in_open_hours_only_while_a_Villager_cranks()
    {
        using var sim = Home();
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        sim.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
        sim.Scenario.EquipGear(0, GearKind.HandCrankedGenerator);
        sim.Scenario.SetStores(0, GoodsKind.FreshFood, 12);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(VillagerAction.Cranking, sim.Snapshot.Villagers[0].Action);
        Assert.InRange(sim.Snapshot.Villagers[0].Needs.Rest, 0.8, 0.87);
        Assert.True(sim.Snapshot.FridgePower.Single().GeneratorPowered);
        sim.Scenario.SendVillager(0, new(40, 104));
        sim.AdvanceTicks(1);
        Assert.False(sim.Snapshot.FridgePower.Single().GeneratorPowered);
        sim.Advance(TimeSpan.FromHours(6));
        Assert.True(sim.Snapshot.Villages[0].SpoilProgress.GetValueOrDefault(GoodsKind.FreshFood) > 0);
    }

    [Fact]
    public void An_unpowered_Fridge_does_not_slow_the_open_piles_spoilage()
    {
        using var fridgeHome = Home();
        using var bareHome = Home();
        var fridge = fridgeHome.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        fridgeHome.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
        foreach (var sim in new[] { fridgeHome, bareHome })
        {
            sim.Scenario.SetStores(0, GoodsKind.FreshFood, 12);
            sim.Advance(TimeSpan.FromHours(6));
        }
        Assert.Equal(bareHome.Snapshot.Villages[0].SpoilProgress, fridgeHome.Snapshot.Villages[0].SpoilProgress);
        Assert.Equal(bareHome.Snapshot.Villages[0].Stores, fridgeHome.Snapshot.Villages[0].Stores);
    }

    [Fact]
    public void Fridge_charge_and_food_continue_identically_after_save_load()
    {
        using var sim = Home();
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        sim.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
        sim.Scenario.SetStores(0, GoodsKind.FreshFood, 12);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 1);
        sim.Advance(TimeSpan.FromHours(1));
        using var save = new MemoryStream();
        sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.FridgePower, loaded.Snapshot.FridgePower);
        sim.Advance(TimeSpan.FromHours(6)); loaded.Advance(TimeSpan.FromHours(6));
        Assert.Equal(sim.Snapshot.FridgePower, loaded.Snapshot.FridgePower);
        Assert.Equal(sim.Snapshot.Villages[0].Stores, loaded.Snapshot.Villages[0].Stores);
        Assert.Equal(sim.Snapshot.Villages[0].SpoilProgress, loaded.Snapshot.Villages[0].SpoilProgress);
    }

    [Fact]
    public void Builders_bring_a_Fridge_home_with_spare_power_and_record_it_in_the_chronicle()
    {
        using var sim = Home();
        sim.Scenario.AssignRole(0, Role.Builder);
        sim.Scenario.EquipGear(0, GearKind.ShoppingCart);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 3);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(34, 104));
        sim.Advance(TimeSpan.FromHours(3));
        Assert.True(sim.Snapshot.Space.Distance(sim.Snapshot.Furniture.Single(p => p.Id == fridge.Id).Position, new(20, 104)) <= 8);
        Assert.Equal(0, sim.Snapshot.FridgePower.Single().Home);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("Fridge"));
    }

    [Fact]
    public void A_generator_holder_does_not_crank_for_the_Fridge_while_Batteries_last_or_without_food()
    {
        using var batteries = Home();
        using var empty = Home();
        foreach (var sim in new[] { batteries, empty })
        {
            var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
            sim.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
            sim.Scenario.EquipGear(0, GearKind.HandCrankedGenerator);
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        }
        batteries.Scenario.SetStores(0, GoodsKind.FreshFood, 12);
        batteries.Scenario.SetStores(0, GoodsKind.Batteries, 3);
        empty.Scenario.SetStores(0, GoodsKind.TinnedFood, 0);
        foreach (var sim in new[] { batteries, empty })
        {
            sim.Advance(TimeSpan.FromMinutes(30));
            Assert.NotEqual(VillagerAction.Cranking, sim.Snapshot.Villagers[0].Action);
        }
        Assert.Equal(0, empty.Snapshot.FridgePower.Single().BatteryHours);
    }

    [Fact]
    public void A_home_Fridge_can_still_be_ordered_apart_and_is_only_announced_once()
    {
        using var sim = Home();
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        sim.Scenario.MoveFurniture(fridge.Id, new(22, 106), 0);
        sim.AdvanceTicks(2);
        sim.Scenario.EquipGear(0, GearKind.ShoppingCart);
        sim.Scenario.CarryFurniture(fridge.Id, [0], new(21, 107));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.FurnitureTasks);
        Assert.Single(sim.Snapshot.Chronicle, line => line.Contains("Fridge"));
        using var bare = Home();
        var other = bare.Scenario.PlaceFurniture(FurnitureType.Fridge, new(30, 108));
        bare.Scenario.MoveFurniture(other.Id, new(22, 106), 0);
        bare.AdvanceTicks(2);
        bare.Scenario.DisassembleFurniture(other.Id, 0);
        Assert.Single(bare.Snapshot.FurnitureTasks);
    }

    [Fact]
    public void Any_available_Builder_with_a_cart_fetches_the_Fridge()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var bare = sim.Scenario.PlaceVillager(new(20, 104));
        var carter = sim.Scenario.PlaceVillager(new(21, 104));
        sim.Scenario.PlaceVillage(bare.Position, [bare.Id, carter.Id]);
        foreach (var id in new[] { bare.Id, carter.Id }) sim.Scenario.AssignRole(id, Role.Builder);
        sim.Scenario.EquipGear(carter.Id, GearKind.ShoppingCart);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 100);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 3);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(34, 104));
        sim.Advance(TimeSpan.FromHours(3));
        Assert.True(sim.Snapshot.Space.Distance(sim.Snapshot.Furniture.Single(p => p.Id == fridge.Id).Position, new(20, 104)) <= 8);
        Assert.Equal(0, sim.Snapshot.FridgePower.Single().Home);
    }
}
