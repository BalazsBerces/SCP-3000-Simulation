using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class GearTests
{
    [Fact]
    public void Invalid_Gear_and_corrupt_carried_Gear_are_rejected()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var v = sim.Scenario.PlaceVillager(new(20, 104));
        Assert.Throws<ArgumentException>(() => sim.Scenario.PlaceGear((GearKind)999, new(20, 104)));
        Assert.Throws<ArgumentException>(() => sim.Scenario.PlaceGear(GearKind.HexKey, new(float.NaN, 0)));
        Assert.Throws<ArgumentException>(() => sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag, double.NaN));
        Assert.Throws<ArgumentException>(() => sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetCarriedLoad(v.Id, GoodsKind.Batteries, -1));
        sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag);
        using var save = new MemoryStream();
        sim.Save(save);
        var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        data["State"]!["Villagers"]!["Walkers"]![0]!["Villager"]!["Bag"]!["Durability"] = -1;
        using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(corrupt));
    }

    [Fact]
    public void A_bag_wears_out_on_delivery_and_disappears_at_zero()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        var band = sim.Scenario.PlaceBand([new(20, 104)]);
        sim.Scenario.EquipGear(band.VillagerIds[0], GearKind.CanvasBag, durability: 1);
        for (var i = 0; i < 2000 && sim.Snapshot.Villagers[0].Bag is not null; i++) sim.AdvanceTicks(1);
        Assert.Null(Assert.Single(sim.Snapshot.Villagers).Bag);
        Assert.Equal(8, Assert.Single(sim.Snapshot.Villages).Stores[GoodsKind.TinnedFood]);
        Assert.Equal(2, sim.Snapshot.Villagers[0].CarryingCapacity);
    }

    [Fact]
    public void Gear_slots_load_and_battery_state_survive_save_and_continue()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var v = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.EquipGear(v.Id, GearKind.Flashlight, durability: 0.2);
        sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag, durability: 12);
        sim.Scenario.EquipGear(v.Id, GearKind.QuiltedArmour);
        sim.Scenario.SetCarriedLoad(v.Id, GoodsKind.Batteries, 1, 2);
        sim.Scenario.PlaceGear(GearKind.Scissors, new(40, 104));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
        sim.Advance(TimeSpan.FromMinutes(6));
        var frozen = sim.Snapshot;
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Gear, loaded.Snapshot.Gear);
        Assert.Equal(sim.Snapshot.Villagers[0].Hand, loaded.Snapshot.Villagers[0].Hand);
        Assert.Equal(sim.Snapshot.Villagers[0].Bag, loaded.Snapshot.Villagers[0].Bag);
        Assert.Equal(sim.Snapshot.Villagers[0].Armour, loaded.Snapshot.Villagers[0].Armour);
        Assert.Equal(2, loaded.Snapshot.Villagers[0].CarriedMaterials);
        sim.Advance(TimeSpan.FromMinutes(12));
        loaded.Advance(TimeSpan.FromMinutes(12));
        Assert.Null(loaded.Snapshot.Villagers[0].Hand);
        Assert.Null(sim.Snapshot.Villagers[0].Hand);
        Assert.NotNull(frozen.Villagers[0].Hand);
    }

    [Fact]
    public void Found_Gear_has_suitable_sources_and_does_not_come_back_after_pickup()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false,
            Restock: new RestockConfig { GoodsPerStoreHour = 60, DrySpellIntervalHours = 0 }));
        var world = sim.Snapshot;
        Assert.All(new[] { GearKind.HexKey, GearKind.KitchenKnife, GearKind.Scissors, GearKind.CanvasBag,
            GearKind.ShoppingCart, GearKind.Flashlight }, kind => Assert.Contains(world.Gear, g => g.Kind == kind));
        foreach (var gear in world.Gear)
        {
            var piece = world.Furniture.Single(p => p.Id == gear.FurnitureId);
            var department = world.Departments.At(piece.Position).Type;
            var poi = piece.PointOfInterestId >= 0 ? world.PointsOfInterest[piece.PointOfInterestId].Type : (PointOfInterestType?)null;
            if (poi == PointOfInterestType.LostAndFound) continue;
            Assert.True(gear.Kind switch
            {
                GearKind.HexKey => department == DepartmentType.SelfServeWarehouse && piece.Type == FurnitureType.FlatPackBox,
                GearKind.KitchenKnife => department == DepartmentType.BathroomsAndKitchens,
                GearKind.Scissors => department is DepartmentType.Workspaces or DepartmentType.TextilesAndDecor,
                GearKind.CanvasBag => department is DepartmentType.RestaurantAndFoodMarket or DepartmentType.TextilesAndDecor,
                GearKind.Flashlight => department == DepartmentType.LightingAndElectrical,
                GearKind.ShoppingCart => poi == PointOfInterestType.ShoppingCartGraveyard,
                _ => false,
            });
        }
        Assert.Contains(world.Gear, g => world.PointsOfInterest.ElementAtOrDefault(world.Furniture.Single(p => p.Id == g.FurnitureId).PointOfInterestId)?.Type == PointOfInterestType.LostAndFound);
        var source = world.Gear.First(g => g.Kind == GearKind.CanvasBag);
        sim.Scenario.PlaceVillager(source.Position);
        for (var i = 0; i < 500 && sim.Snapshot.Villagers[0].Bag is null; i++) sim.AdvanceTicks(1);
        Assert.NotNull(sim.Snapshot.Villagers[0].Bag);
        sim.Advance(TimeSpan.FromMinutes(2));
        Assert.DoesNotContain(sim.Snapshot.Gear, g => g.Kind == source.Kind && g.FurnitureId == source.FurnitureId);
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        sim.Advance(TimeSpan.FromMinutes(2));
        loaded.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(sim.Snapshot.Gear, loaded.Snapshot.Gear);
    }

    [Fact]
    public void A_found_cart_moves_out_of_the_graveyard_and_does_not_come_back()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false,
            Restock: new RestockConfig { GoodsPerStoreHour = 1, DrySpellIntervalHours = 0 }));
        var source = sim.Snapshot.Gear.First(g => g.Kind == GearKind.ShoppingCart);
        var graveyard = sim.Snapshot.PointsOfInterest[sim.Snapshot.Furniture.Single(p => p.Id == source.FurnitureId).PointOfInterestId];
        var v = sim.Scenario.PlaceVillager(source.Position);
        for (var i = 0; i < 500 && sim.Snapshot.Villagers[0].Hand is null; i++) sim.AdvanceTicks(1);
        Assert.Equal(GearKind.ShoppingCart, sim.Snapshot.Villagers[0].Hand?.Kind);
        Assert.DoesNotContain(sim.Snapshot.Furniture, p => p.Id == source.FurnitureId);
        sim.Scenario.SendVillager(v.Id, graveyard.Centre + new System.Numerics.Vector2(graveyard.Size.X / 2 + 40, 0));
        sim.Advance(TimeSpan.FromHours(2.1));
        Assert.True(sim.Snapshot.Space.Distance(sim.Snapshot.Villagers[0].Position, source.Position) > 30,
            $"Cart carrier stayed at {sim.Snapshot.Villagers[0].Position}, source {source.Position}, destination {sim.Snapshot.Villagers[0].Destination}");
        Assert.DoesNotContain(sim.Snapshot.Furniture, p => p.Type == FurnitureType.ShoppingCart && p.Position == source.Position);
        Assert.DoesNotContain(sim.Snapshot.Gear, g => g.Kind == GearKind.ShoppingCart && g.Position == source.Position);
    }

    [Fact]
    public void Found_Gear_is_taken_and_batteries_power_a_flashlight_at_Closing_time()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var v = sim.Scenario.PlaceVillager(new(20, 104));
        var gear = sim.Scenario.PlaceGear(GearKind.Flashlight, new(22, 104));
        sim.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(gear.Id, Assert.Single(sim.Snapshot.Villagers).Hand?.Id);
        Assert.Empty(sim.Snapshot.Gear);
        sim.Scenario.SetCarriedLoad(v.Id, GoodsKind.Batteries, 1);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
        sim.Advance(TimeSpan.FromMinutes(10));
        var carried = Assert.Single(sim.Snapshot.Villagers);
        Assert.InRange(carried.Hand!.BatteryHours, 3.8, 3.9);
        Assert.Equal(0, carried.CarriedAmount);
        Assert.True(carried.Hand.Durability < 100);
    }

    [Fact]
    public void Full_Goods_and_Material_loads_slow_walking_and_carts_speed_hauling()
    {
        static float Walk(int goods, int materials, bool cart = false)
        {
            using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
            var v = sim.Scenario.PlaceVillager(new(20, 104));
            if (cart) sim.Scenario.EquipGear(v.Id, GearKind.ShoppingCart);
            sim.Scenario.SetCarriedLoad(v.Id, GoodsKind.TinnedFood, goods, materials);
            sim.Scenario.SendVillager(v.Id, new(60, 104));
            sim.Advance(TimeSpan.FromMinutes(6));
            return sim.Snapshot.Space.Distance(v.Position, Assert.Single(sim.Snapshot.Villagers).Position);
        }
        Assert.InRange(Walk(0, 0), 5.9f, 6.1f);
        Assert.InRange(Walk(2, 0), 2.9f, 3.1f);
        Assert.Equal(Walk(2, 0), Walk(1, 1));
        Assert.True(Walk(2, 0, true) > Walk(2, 0));
    }

    [Fact]
    public void Slots_reject_duplicates_and_a_cart_occupies_the_hand()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var v = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.EquipGear(v.Id, GearKind.ShoppingCart);
        sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag);
        sim.Scenario.EquipGear(v.Id, GearKind.QuiltedArmour);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.EquipGear(v.Id, GearKind.HexKey));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.EquipGear(v.Id, GearKind.CanvasBag));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.EquipGear(v.Id, GearKind.QuiltedArmour));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetCarriedLoad(v.Id, GoodsKind.TinnedFood, 33));
        var equipped = Assert.Single(sim.Snapshot.Villagers);
        Assert.NotNull(equipped.Hand);
        Assert.NotNull(equipped.Bag);
        Assert.NotNull(equipped.Armour);
    }

    [Fact]
    public void Canvas_bag_brings_more_Goods_home_in_one_trip()
    {
        Assert.Equal(2, FirstDelivery(false));
        Assert.Equal(8, FirstDelivery(true));
    }

    private static int FirstDelivery(bool bag)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.PlaceWaterPoint(new(22, 108));
        var band = sim.Scenario.PlaceBand([new(20, 104)]);
        if (bag)
            sim.Scenario.EquipGear(band.VillagerIds[0], GearKind.CanvasBag);
        for (var i = 0; i < 2000; i++)
        {
            sim.AdvanceTicks(1);
            if (sim.Snapshot.Villages.FirstOrDefault()?.Stores.GetValueOrDefault(GoodsKind.TinnedFood) is > 0 and var amount)
                return amount;
        }
        throw new Exception("No delivery within four Store hours.");
    }
}
