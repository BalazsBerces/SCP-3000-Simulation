using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class RecipeTests
{
    private static Gear[] VillageGear(WorldSnapshot world) => world.Villages[0].Gear
        .Concat(world.Villagers.Where(v => v.VillageId == 0).SelectMany(v => new[] { v.Hand, v.Bag, v.Armour }).OfType<Gear>()
            .Where(g => Recipes.Basic.Any(r => r.Gear == g.Kind))).ToArray();

    [Fact]
    public void An_idle_Builder_makes_basic_Gear_autonomously_without_overspending_shared_stores()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, 3).Select(_ => sim.Scenario.PlaceVillager(new(20, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 10);
        sim.Scenario.SetMaterials(0, MaterialKind.WoodPanel, 2);
        sim.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(GearKind.TableLegClub, Assert.Single(VillageGear(sim.Snapshot)).Kind);
        Assert.Empty(sim.Snapshot.Villages[0].Materials);
        Assert.True(sim.Snapshot.Villagers.Single(v => v.Id == ids[1]).Skills[Role.Builder] > 0);
    }

    [Fact]
    public void Crafting_reserves_the_worker_and_corrupt_skills_or_tasks_are_rejected_on_load()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var w = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(w.Position, [w.Id]);
        sim.Scenario.SetMaterials(0, MaterialKind.Fabric, 2);
        sim.Scenario.MakeGear(w.Id, GearKind.Bandage);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(w.Id, GearKind.Bandage));
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.BlanketStack, new(22, 104));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.DisassembleFurniture(piece.Id, w.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetSkill(w.Id, Role.Builder, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetMaterials(0, MaterialKind.Fabric, -1));
        using var save = new MemoryStream();
        sim.Save(save);
        foreach (var corruptSkill in new[] { true, false })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
            if (corruptSkill)
                data["State"]!["Villagers"]!["Walkers"]![0]!["Villager"]!["Skills"]!["Builder"] = -1;
            else data["State"]!["Snapshot"]!["CraftingTasks"]![0]!["Progress"] = 1;
            using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            Assert.Throws<InvalidDataException>(() => Simulation.Load(corrupt));
        }
    }

    [Fact]
    public void Missing_tool_or_Materials_rejects_a_Recipe_without_spending_anything()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var worker = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(worker.Position, [worker.Id]);
        sim.Scenario.SetMaterials(0, MaterialKind.Fabric, 4);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(worker.Id, GearKind.CanvasBag));
        Assert.Equal(4, sim.Snapshot.Villages[0].Materials[MaterialKind.Fabric]);
        sim.Scenario.EquipGear(worker.Id, GearKind.Scissors);
        sim.Scenario.SetMaterials(0, MaterialKind.Fabric, 3);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(worker.Id, GearKind.CanvasBag));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.MakeGear(worker.Id, GearKind.Flashlight));
        Assert.Empty(sim.Snapshot.CraftingTasks);
    }

    [Fact]
    public void A_bandage_is_consumed_once_from_stores_or_the_hand()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var worker = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(worker.Position, [worker.Id]);
        sim.Scenario.SetMaterials(0, MaterialKind.Fabric, 1);
        sim.Scenario.MakeGear(worker.Id, GearKind.Bandage);
        sim.Advance(TimeSpan.FromMinutes(7));
        var used = new List<BandageUsed>();
        using var sub = sim.Subscribe(e => { if (e is BandageUsed b) used.Add(b); });
        Assert.True(sim.Scenario.UseBandage(worker.Id));
        Assert.False(sim.Scenario.UseBandage(worker.Id));
        Assert.Empty(sim.Snapshot.Villages[0].Gear);
        sim.Scenario.EquipGear(worker.Id, GearKind.Bandage);
        Assert.True(sim.Scenario.UseBandage(worker.Id));
        Assert.Null(sim.Snapshot.Villagers[0].Hand);
        Assert.False(sim.Scenario.UseBandage(worker.Id));
        Assert.Equal(2, used.Count);
    }

    [Fact]
    public void Practice_makes_the_same_Gear_faster_and_more_durable()
    {
        static (long Ticks, double Durability, double Skill) Make(double skill)
        {
            using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
            var w = sim.Scenario.PlaceVillager(new(20, 104));
            sim.Scenario.PlaceVillage(w.Position, [w.Id]);
            sim.Scenario.SetSkill(w.Id, Role.Builder, skill);
            sim.Scenario.SetMaterials(0, MaterialKind.WoodPanel, 2);
            sim.Scenario.MakeGear(w.Id, GearKind.TableLegClub);
            var start = sim.Snapshot.Clock.Tick;
            while (sim.Snapshot.Villages[0].Gear.Count == 0 && sim.Snapshot.Clock.Tick - start < 1000) sim.AdvanceTicks(1);
            return (sim.Snapshot.Clock.Tick - start, Assert.Single(VillageGear(sim.Snapshot)).Durability,
                sim.Snapshot.Villagers[0].Skills[Role.Builder]);
        }
        var novice = Make(0);
        var skilled = Make(2);
        Assert.True(skilled.Ticks < novice.Ticks / 2);
        Assert.True(skilled.Durability > novice.Durability);
        Assert.True(novice.Skill > 0);
        Assert.True(skilled.Skill > 2);
    }

    [Fact]
    public void Skilled_disassembly_recovers_more_Materials_from_the_same_Furniture()
    {
        static int Recover(double skill)
        {
            using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
            var w = sim.Scenario.PlaceVillager(new(20, 104));
            sim.Scenario.PlaceVillage(w.Position, [w.Id]);
            sim.Scenario.SetSkill(w.Id, Role.Builder, skill);
            var piece = sim.Scenario.PlaceFurniture(FurnitureType.BlanketStack, new(22, 104));
            sim.Scenario.DisassembleFurniture(piece.Id, w.Id);
            sim.Advance(TimeSpan.FromMinutes(30));
            return sim.Snapshot.Villages[0].Materials[MaterialKind.Fabric];
        }
        Assert.Equal(4, Recover(0));
        Assert.Equal(6, Recover(2));
    }

    [Theory]
    [InlineData(GearKind.TableLegClub, MaterialKind.WoodPanel, 2)]
    [InlineData(GearKind.CardboardArmour, MaterialKind.WoodPanel, 2)]
    [InlineData(GearKind.Bandage, MaterialKind.Fabric, 1)]
    public void Bare_hand_Recipes_need_no_Workshop(GearKind kind, MaterialKind material, int amount)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var w = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(w.Position, [w.Id]);
        sim.Scenario.SetMaterials(0, material, amount);
        sim.Scenario.MakeGear(w.Id, kind);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(kind, Assert.Single(VillageGear(sim.Snapshot)).Kind);
        Assert.Empty(sim.Snapshot.Villages[0].Materials);
    }

    [Fact]
    public void Candle_Goods_become_portable_Gear_and_every_community_knows_the_basics()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var band = sim.Scenario.PlaceBand([new(50, 104)]);
        Assert.Equal(5, band.Recipes.Count);
        var w = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(w.Position, [w.Id]);
        Assert.Equal(band.Recipes, sim.Snapshot.Villages[0].Recipes);
        sim.Scenario.SetStores(0, GoodsKind.Candles, 1);
        sim.Scenario.MakeGear(w.Id, GearKind.Candle);
        sim.Advance(TimeSpan.FromMinutes(7));
        Assert.Equal(GearKind.Candle, Assert.Single(VillageGear(sim.Snapshot)).Kind);
        Assert.False(sim.Snapshot.Villages[0].Stores.ContainsKey(GoodsKind.Candles));
    }

    [Fact]
    public void Skills_Recipes_Gear_and_active_work_survive_save_and_continue_without_repeating_the_first()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var w = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(w.Position, [w.Id]);
        sim.Scenario.SetSkill(w.Id, Role.Builder, 1);
        sim.Scenario.SetMaterials(0, MaterialKind.WoodPanel, 4);
        sim.Scenario.MakeGear(w.Id, GearKind.TableLegClub);
        while (sim.Snapshot.CraftingTasks.Count > 0) sim.AdvanceTicks(1);
        sim.Scenario.MakeGear(w.Id, GearKind.CardboardArmour);
        sim.AdvanceTicks(3);
        var frozen = sim.Snapshot;
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.CraftingTasks, loaded.Snapshot.CraftingTasks);
        Assert.Equal(sim.Snapshot.Villagers[0].Skills, loaded.Snapshot.Villagers[0].Skills);
        sim.Advance(TimeSpan.FromMinutes(30));
        loaded.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(VillageGear(sim.Snapshot), VillageGear(loaded.Snapshot));
        Assert.Equal(sim.Snapshot.Villages[0].Recipes, loaded.Snapshot.Villages[0].Recipes);
        Assert.Equal(sim.Snapshot.Villagers[0].Skills, loaded.Snapshot.Villagers[0].Skills);
        Assert.Single(loaded.Snapshot.Chronicle, line => line.Contains("first Gear"));
        Assert.Empty(loaded.Snapshot.CraftingTasks);
        Assert.Single(frozen.CraftingTasks);
        Assert.Single(frozen.Villages[0].Gear);
    }

    [Fact]
    public void A_Village_makes_a_canvas_bag_and_spends_its_fabric()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var worker = sim.Scenario.PlaceVillager(new(20, 104));
        var village = sim.Scenario.PlaceVillage(worker.Position, [worker.Id]);
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Fabric, 4);
        sim.Scenario.EquipGear(worker.Id, GearKind.Scissors);
        sim.Scenario.MakeGear(worker.Id, GearKind.CanvasBag);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(GearKind.CanvasBag, Assert.Single(VillageGear(sim.Snapshot)).Kind);
        Assert.Equal(GearKind.CanvasBag, sim.Snapshot.Villagers[0].Bag?.Kind);
        Assert.False(sim.Snapshot.Villages[0].Materials.ContainsKey(MaterialKind.Fabric));
        Assert.True(sim.Snapshot.Villagers[0].Skills[Role.Builder] > 0);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("first Gear") && line.Contains("Village 0"));
    }
}
