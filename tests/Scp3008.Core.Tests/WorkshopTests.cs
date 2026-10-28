using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class WorkshopTests
{
    [Fact]
    public void A_middle_Recipe_requires_a_built_Workshop_and_survives_save()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var worker = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(worker.Position, [worker.Id]);
        var box = sim.Scenario.PlaceFurniture(FurnitureType.FlatPackBox, new(22, 104));
        sim.Scenario.PlaceAssemblyInstructions(box.Id, GearKind.Crowbar);
        sim.AdvanceTicks(1);
        sim.Scenario.SetMaterials(0, MaterialKind.Metal, 4);
        sim.Scenario.EquipGear(worker.Id, GearKind.HexKey);
        Assert.Contains(GearKind.Crowbar, sim.Snapshot.Villages[0].Recipes);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(worker.Id, GearKind.Crowbar));
        sim.Scenario.PlanWorkshop(0, WorkshopKind.Workbench);
        // A second member becomes the Builder when food is available.
        var band = sim.Scenario.PlaceBand([new(20, 104)], knownMap: new KnownMap
        { Villages = [new KnownVillage(0, new(20, 104), sim.Snapshot.Clock)] });
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 100);
        sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(25, 104));
        sim.Advance(TimeSpan.FromHours(2));
        Assert.True(sim.Snapshot.Villages[0].Workshops[0].Complete);
        sim.Scenario.SendVillager(worker.Id, new(20, 104));
        for (var i = 0; i < 2000 && sim.Snapshot.Space.Distance(sim.Snapshot.Villagers.Single(v => v.Id == worker.Id).Position, new(20, 104)) > 2; i++)
            sim.AdvanceTicks(1);
        sim.Scenario.MakeGear(worker.Id, GearKind.Crowbar);
        sim.AdvanceTicks(2);
        using var save = new MemoryStream();
        sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        loaded.Advance(TimeSpan.FromHours(1));
        Assert.Contains(loaded.Snapshot.Villages[0].Gear.Concat(loaded.Snapshot.Villagers.Select(v => v.Hand).OfType<Gear>()), g => g.Kind == GearKind.Crowbar);
        Assert.True(loaded.Snapshot.Villages[0].Workshops[0].Complete);
    }

    private static Simulation VillageWithWorkshops()
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var a = sim.Scenario.PlaceVillager(new(20, 104));
        var b = sim.Scenario.PlaceVillager(new(21, 104));
        sim.Scenario.PlaceVillage(a.Position, [a.Id, b.Id]);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        for (var i = 0; i < 3; i++) sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(25 + i * 3, 104));
        for (var i = 0; i < 5000 && (sim.Snapshot.Villages[0].Workshops.Count != 3 || sim.Snapshot.Villages[0].Workshops.Any(w => !w.Complete)); i++)
            sim.AdvanceTicks(1);
        Assert.Equal(3, sim.Snapshot.Villages[0].Workshops.Count);
        Assert.All(sim.Snapshot.Villages[0].Workshops, w => Assert.True(w.Complete));
        ReturnHome(sim, 1);
        return sim;
    }

    private static void ReturnHome(Simulation sim, int id)
    {
        sim.Scenario.SendVillager(id, new(20, 104));
        for (var i = 0; i < 2000 && sim.Snapshot.Space.Distance(sim.Snapshot.Villagers.Single(v => v.Id == id).Position, new(20, 104)) > 2; i++)
            sim.AdvanceTicks(1);
        Assert.InRange(sim.Snapshot.Space.Distance(sim.Snapshot.Villagers.Single(v => v.Id == id).Position, new(20, 104)), 0, 2);
    }

    [Fact]
    public void A_knife_recovers_a_leather_armchair_into_a_bigger_backpack_than_a_canvas_bag()
    {
        using var sim = VillageWithWorkshops();
        sim.Scenario.EquipGear(1, GearKind.KitchenKnife);
        var box = sim.Scenario.PlaceFurniture(FurnitureType.LabelledBox, new(22, 104));
        sim.Scenario.PlaceAssemblyInstructions(box.Id, GearKind.LeatherBackpack);
        sim.AdvanceTicks(50);
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(22, 101), variant: 0);
        sim.Scenario.DisassembleFurniture(chair.Id, 1);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.True(sim.Snapshot.Villages[0].Materials[MaterialKind.Leather] >= 4);
        ReturnHome(sim, 1);
        sim.Scenario.MakeGear(1, GearKind.LeatherBackpack);
        sim.Advance(TimeSpan.FromHours(1));
        var backpack = sim.Snapshot.Villages[0].Gear.Concat(sim.Snapshot.Villagers.Select(v => v.Bag).OfType<Gear>()).Single(g => g.Kind == GearKind.LeatherBackpack);
        Assert.Equal(GearSlot.Bag, backpack.Slot);
        var carrier = sim.Scenario.PlaceVillager(new(20, 110));
        sim.Scenario.EquipGear(carrier.Id, GearKind.LeatherBackpack);
        var canvas = sim.Scenario.PlaceVillager(new(23, 110));
        sim.Scenario.EquipGear(canvas.Id, GearKind.CanvasBag);
        Assert.Equal(20, sim.Snapshot.Villagers.Single(v => v.Id == carrier.Id).CarryingCapacity);
        Assert.Equal(8, sim.Snapshot.Villagers.Single(v => v.Id == canvas.Id).CarryingCapacity);
        sim.Scenario.SetCarriedLoad(carrier.Id, GoodsKind.TinnedFood, 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetCarriedLoad(canvas.Id, GoodsKind.TinnedFood, 20));
    }

    [Fact]
    public void Experimenting_spends_Materials_and_skill_accelerates_discovery_with_save_continuation()
    {
        static long Discover(double skill)
        {
            using var sim = VillageWithWorkshops();
            sim.Scenario.EquipGear(1, GearKind.Screwdriver);
            sim.Scenario.SetSkill(1, Role.Builder, skill);
            sim.Scenario.SetMaterials(0, MaterialKind.Electrics, 2);
            sim.Scenario.SetMaterials(0, MaterialKind.Glass, 2);
            var start = sim.Snapshot.Clock.Tick;
            for (var i = 0; i < 100 && sim.Snapshot.Experiments.Count == 0; i++) sim.AdvanceTicks(1);
            Assert.Single(sim.Snapshot.Experiments);
            Assert.Empty(sim.Snapshot.Villages[0].Materials);
            using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
            using var loaded = Simulation.Load(save);
            Assert.Equal(sim.Snapshot.Experiments, loaded.Snapshot.Experiments);
            for (var i = 0; i < 5000 && !loaded.Snapshot.Villages[0].Recipes.Contains(GearKind.Lantern); i++) loaded.AdvanceTicks(1);
            Assert.Contains(GearKind.Lantern, loaded.Snapshot.Villages[0].Recipes);
            Assert.Empty(loaded.Snapshot.Experiments);
            Assert.Contains(loaded.Snapshot.Chronicle, l => l.Contains("experimenting") && l.Contains("Lantern"));
            return loaded.Snapshot.Clock.Tick - start;
        }
        var novice = Discover(0);
        var skilled = Discover(3);
        Assert.True(skilled < novice / 2);
    }

    [Fact]
    public void A_joining_Refugee_brings_the_former_Villages_middle_Recipes()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var host = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(host.Position, [host.Id]);
        var refugee = sim.Scenario.PlaceBand([new(21, 104)], knownMap: new KnownMap
            { Villages = [new KnownVillage(0, host.Position, sim.Snapshot.Clock)] },
            recipes: Recipes.Basic.Select(r => r.Gear).Append(GearKind.HandCrankedGenerator).ToArray());
        sim.AdvanceTicks(1);
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Contains(GearKind.HandCrankedGenerator, sim.Snapshot.Villages[0].Recipes);
        Assert.Contains(refugee.VillagerIds[0], sim.Snapshot.Villages[0].VillagerIds);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Villages[0].Recipes, loaded.Snapshot.Villages[0].Recipes);
    }

    [Theory]
    [InlineData(GearKind.LeatherBackpack)]
    [InlineData(GearKind.Saw)]
    [InlineData(GearKind.Crowbar)]
    [InlineData(GearKind.Spear)]
    [InlineData(GearKind.TabletopShield)]
    [InlineData(GearKind.PlatformCart)]
    [InlineData(GearKind.QuiltedArmour)]
    [InlineData(GearKind.Lantern)]
    [InlineData(GearKind.HandCrankedGenerator)]
    public void Every_middle_Recipe_spends_all_ingredients_and_produces_usable_Gear(GearKind kind)
    {
        using var sim = VillageWithWorkshops();
        var recipe = Recipes.Middle.Single(r => r.Gear == kind);
        var box = sim.Scenario.PlaceFurniture(FurnitureType.FlatPackBox, new(20, 104));
        sim.Scenario.PlaceAssemblyInstructions(box.Id, kind);
        ReturnHome(sim, 1);
        for (var i = 0; i < 50 && !sim.Snapshot.Villages[0].Recipes.Contains(kind); i++) sim.AdvanceTicks(1);
        Assert.Contains(kind, sim.Snapshot.Villages[0].Recipes);
        ReturnHome(sim, 1);
        sim.Scenario.EquipGear(1, recipe.Tool!.Value);
        sim.Scenario.SetMaterials(0, recipe.Material!.Value, recipe.Amount);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(1, kind == GearKind.Spear ? GearKind.Saw : GearKind.Spear));
        if (recipe.ExtraMaterial is { } extra)
        {
            Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(1, kind));
            sim.Scenario.SetMaterials(0, extra, recipe.ExtraAmount);
        }
        sim.Scenario.MakeGear(1, kind);
        Assert.Empty(sim.Snapshot.Villages[0].Materials);
        sim.Advance(TimeSpan.FromHours(1));
        var output = sim.Snapshot.Villages[0].Gear.Concat(sim.Snapshot.Villagers.SelectMany(v => new[] { v.Hand, v.Bag, v.Armour }).OfType<Gear>());
        Assert.Contains(output, g => g.Kind == kind);
        if (kind == GearKind.Spear) Assert.DoesNotContain(output, g => g.Kind == GearKind.KitchenKnife);
    }

    [Fact]
    public void Losing_a_built_Workshop_blocks_crafting_and_invalid_saved_know_how_is_rejected()
    {
        using var sim = VillageWithWorkshops();
        var box = sim.Scenario.PlaceFurniture(FurnitureType.FlatPackBox, new(22, 104));
        sim.Scenario.PlaceAssemblyInstructions(box.Id, GearKind.Saw);
        using var save = new MemoryStream(); sim.Save(save);
        foreach (var corrupt in new[] { "instructions", "workshop" })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
            if (corrupt == "instructions") data["State"]!["Snapshot"]!["AssemblyInstructions"]![0]!["Recipe"] = "HexKey";
            else data["State"]!["Snapshot"]!["Villages"]![0]!["Workshops"]![0]!["Slot"]!["FurnitureId"] = 999999;
            using var bad = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            Assert.Throws<InvalidDataException>(() => Simulation.Load(bad));
        }
        var frozen = sim.Snapshot;
        var id = sim.Snapshot.Villages[0].Workshops[0].Slot.FurnitureId;
        sim.Scenario.RemoveFurniture(p => p.Id == id || p.Id == box.Id);
        Assert.False(sim.Snapshot.Villages[0].Workshops[0].Complete);
        Assert.True(frozen.Villages[0].Workshops[0].Complete);
        Assert.Empty(sim.Snapshot.AssemblyInstructions);
        using var clean = new MemoryStream(); sim.Save(clean); clean.Position = 0;
        using var loaded = Simulation.Load(clean);
        Assert.False(loaded.Snapshot.Villages[0].Workshops[0].Complete);
    }

    [Fact]
    public void The_saw_recovers_full_metal_and_the_platform_cart_hauls_heavy_Furniture()
    {
        using var sim = VillageWithWorkshops();
        sim.Scenario.EquipGear(0, GearKind.Saw);
        var rack = sim.Scenario.PlaceFurniture(FurnitureType.PalletRack, new(24, 110));
        sim.Scenario.DisassembleFurniture(rack.Id, 0);
        sim.Advance(TimeSpan.FromHours(2));
        Assert.Equal(6, sim.Snapshot.Villages[0].Materials[MaterialKind.Metal]);
        var hauler = sim.Scenario.PlaceVillager(new(40, 104));
        sim.Scenario.EquipGear(hauler.Id, GearKind.PlatformCart);
        var fridge = sim.Scenario.PlaceFurniture(FurnitureType.Fridge, new(42, 104));
        sim.Scenario.CarryFurniture(fridge.Id, [hauler.Id], new(48, 104));
        Assert.Equal(FurnitureWork.CartHaul, Assert.Single(sim.Snapshot.FurnitureTasks).Work);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(new System.Numerics.Vector2(48, 104), sim.Snapshot.Furniture.Single(p => p.Id == fridge.Id).Position);
    }

    [Fact]
    public void A_reserved_Workshop_Blueprint_and_unread_instructions_survive_loading()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var a = sim.Scenario.PlaceVillager(new(20, 104));
        var b = sim.Scenario.PlaceVillager(new(21, 104));
        sim.Scenario.PlaceVillage(a.Position, [a.Id, b.Id]);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(25, 104));
        var box = sim.Scenario.PlaceFurniture(FurnitureType.FlatPackBox, new(80, 80));
        sim.Scenario.PlaceAssemblyInstructions(box.Id, GearKind.Saw);
        sim.AdvanceTicks(1);
        Assert.NotNull(sim.Snapshot.Villages[0].Workshops[0].Slot.ReservedFurnitureId);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.AssemblyInstructions, loaded.Snapshot.AssemblyInstructions);
        var originalTask = Assert.Single(sim.Snapshot.FurnitureTasks);
        var loadedTask = Assert.Single(loaded.Snapshot.FurnitureTasks);
        Assert.Equal(originalTask.FurnitureId, loadedTask.FurnitureId);
        Assert.Equal(originalTask.Route, loadedTask.Route);
        Assert.Equal(originalTask.VillagerIds, loadedTask.VillagerIds);
        loaded.Advance(TimeSpan.FromHours(2));
        Assert.True(loaded.Snapshot.Villages[0].Workshops[0].Complete);
        Assert.False(sim.Snapshot.Villages[0].Workshops[0].Complete);
    }
}
