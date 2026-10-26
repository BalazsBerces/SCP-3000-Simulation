using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class FurnitureWorkTests
{
    [Fact]
    public void Fixed_Furniture_and_corrupt_task_or_Material_state_are_rejected()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shell = sim.Scenario.PlaceFurniture(FurnitureType.TreeHouse, new(80, 20));
        var v = sim.Scenario.PlaceVillager(new(18, 104));
        sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.CarryFurniture(shell.Id, [v.Id], new(40, 104)));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.DisassembleFurniture(shell.Id, v.Id));
        var box = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
        sim.Scenario.CarryFurniture(box.Id, [v.Id], new(40, 104));
        using var save = new MemoryStream();
        sim.Save(save);
        foreach (var corruptTask in new[] { true, false })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
            if (corruptTask) data["State"]!["Snapshot"]!["FurnitureTasks"]![0]!["VillagerIds"]![0] = 999;
            else data["State"]!["Snapshot"]!["Villages"]![0]!["Materials"]!["Metal"] = -1;
            using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            Assert.Throws<InvalidDataException>(() => Simulation.Load(corrupt));
        }
    }

    [Fact]
    public void Cancelling_a_carry_drops_the_piece_and_releases_the_workers()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var box = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
        var v = sim.Scenario.PlaceVillager(new(18, 104));
        sim.Scenario.CarryFurniture(box.Id, [v.Id], new(60, 104));
        sim.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(0.7f, sim.Snapshot.Furniture[0].Elevation);
        sim.Scenario.SendVillager(v.Id, new(10, 104));
        Assert.Empty(sim.Snapshot.FurnitureTasks);
        Assert.Equal(0, sim.Snapshot.Furniture[0].Elevation);
        Assert.Null(sim.Snapshot.Villagers[0].FurnitureTaskId);
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Empty(loaded.Snapshot.FurnitureTasks);
    }

    [Theory]
    [InlineData(FurnitureType.CardboardBox, null, MaterialKind.WoodPanel, 2)]
    [InlineData(FurnitureType.Bookcase, GearKind.HexKey, MaterialKind.WoodPanel, 4)]
    [InlineData(FurnitureType.Mattress, GearKind.Scissors, MaterialKind.Fabric, 4)]
    [InlineData(FurnitureType.TableLamp, GearKind.Screwdriver, MaterialKind.Electrics, 2)]
    [InlineData(FurnitureType.ShoppingCart, GearKind.Crowbar, MaterialKind.Metal, 6)]
    [InlineData(FurnitureType.Wardrobe, GearKind.HexKey, MaterialKind.WoodPanel, 4)]
    public void All_five_tool_levels_yield_Materials_and_heavy_Furniture_can_be_disassembled(
        FurnitureType type, GearKind? tool, MaterialKind material, int amount)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var piece = sim.Scenario.PlaceFurniture(type, new(20, 104));
        var v = sim.Scenario.PlaceVillager(new(17, 104));
        sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        if (tool is { } gear) sim.Scenario.EquipGear(v.Id, gear);
        var noise = new List<NoiseEmitted>();
        using var sub = sim.Subscribe(e => { if (e is NoiseEmitted n) noise.Add(n); });
        sim.Scenario.DisassembleFurniture(piece.Id, v.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Furniture);
        Assert.Equal(amount, sim.Snapshot.Villages[0].Materials[material]);
        Assert.Empty(noise);
    }

    [Fact]
    public void Repeated_work_builds_skill_and_makes_disassembly_faster_and_richer()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var v = sim.Scenario.PlaceVillager(new(18, 104));
        sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        sim.Scenario.EquipGear(v.Id, GearKind.KitchenKnife);
        for (var i = 0; i < 11; i++)
        {
            var box = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
            sim.Scenario.DisassembleFurniture(box.Id, v.Id);
            while (sim.Snapshot.FurnitureTasks.Count > 0) sim.AdvanceTicks(1);
        }
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 104));
        var start = sim.Snapshot.Clock.Tick;
        sim.Scenario.DisassembleFurniture(chair.Id, v.Id);
        while (sim.Snapshot.FurnitureTasks.Count > 0) sim.AdvanceTicks(1);
        Assert.Equal(5, sim.Snapshot.Villages[0].Materials[MaterialKind.Leather]);
        Assert.True(sim.Snapshot.Clock.Tick - start < StoreClock.TicksFor(TimeSpan.FromMinutes(20)));
    }

    [Fact]
    public void Actual_Landmark_and_Point_of_interest_Furniture_yields_its_own_Materials()
    {
        foreach (var landmark in new[] { true, false })
        {
            using var sim = Simulation.Create(new WorldConfig(3008, LandmarkShowcase: landmark,
                PointOfInterestShowcase: !landmark, StartingBands: false));
            var types = landmark ? new[] { FurnitureType.Mattress, FurnitureType.CardboardBox } :
                new[] { FurnitureType.Armchair, FurnitureType.ShoppingCart };
            foreach (var type in types)
            {
                var source = sim.Snapshot.Furniture.First(p => p.Type == type && p.Elevation == 0 &&
                    (type != FurnitureType.Armchair || p.Variant == 1));
                Assert.True(source.LandmarkId >= 0 || source.PointOfInterestId >= 0);
                sim.Scenario.MoveFurniture(source.Id, new(20, 20), 0);
                var v = sim.Scenario.PlaceVillager(new(15, 20));
                var village = sim.Scenario.PlaceVillage(v.Position, [v.Id]);
                if (type == FurnitureType.ShoppingCart) sim.Scenario.EquipGear(v.Id, GearKind.Crowbar);
                else if (type != FurnitureType.CardboardBox) sim.Scenario.EquipGear(v.Id, GearKind.Scissors);
                sim.Scenario.DisassembleFurniture(source.Id, v.Id);
                sim.Advance(TimeSpan.FromHours(1));
                Assert.DoesNotContain(sim.Snapshot.Furniture, p => p.Id == source.Id);
                var materials = sim.Snapshot.Villages[village.Id].Materials;
                if (type is FurnitureType.Mattress or FurnitureType.Armchair)
                {
                    Assert.Equal(4, materials[MaterialKind.Fabric]);
                    Assert.Equal(4, materials[MaterialKind.Foam]);
                }
                else Assert.Equal(type == FurnitureType.ShoppingCart ? 6 : 2,
                    materials[type == FurnitureType.ShoppingCart ? MaterialKind.Metal : MaterialKind.WoodPanel]);
            }
        }
    }

    [Fact]
    public void Reservations_and_invalid_work_are_rejected_without_changing_the_world()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var box = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
        var v = sim.Scenario.PlaceVillager(new(18, 104));
        Assert.Throws<ArgumentException>(() => sim.Scenario.CarryFurniture(box.Id, [v.Id, v.Id], new(40, 104)));
        Assert.Throws<ArgumentException>(() => sim.Scenario.CarryFurniture(box.Id, [v.Id], new(float.NaN, 104)));
        Assert.Throws<ArgumentException>(() => sim.Scenario.CarryFurniture(box.Id, [999], new(40, 104)));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.DisassembleFurniture(box.Id, v.Id));
        sim.Scenario.CarryFurniture(box.Id, [v.Id], new(40, 104));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.CarryFurniture(box.Id, [v.Id], new(40, 104)));
        sim.Scenario.RemoveFurniture(p => p.Id == box.Id);
        sim.AdvanceTicks(1);
        Assert.Empty(sim.Snapshot.FurnitureTasks);
        Assert.Null(sim.Snapshot.Villagers[0].FurnitureTaskId);
    }

    [Fact]
    public void Dragging_is_slow_and_noisy_while_two_to_four_workers_carry_at_walking_speed()
    {
        static (float Distance, NoiseEmitted[] Noise) Move(int count)
        {
            using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
            var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 104));
            var ids = Enumerable.Range(0, count).Select(_ => sim.Scenario.PlaceVillager(new(18, 104)).Id).ToArray();
            var noise = new List<NoiseEmitted>();
            using var subscription = sim.Subscribe(e => { if (e is NoiseEmitted n) noise.Add(n); });
            sim.Scenario.CarryFurniture(piece.Id, ids, new(60, 104));
            for (var i = 0; i < 500 && !sim.Snapshot.FurnitureTasks[0].Started; i++) sim.AdvanceTicks(1);
            var start = sim.Snapshot.Furniture[0].Position.X;
            sim.Advance(TimeSpan.FromMinutes(12));
            return (sim.Snapshot.Furniture[0].Position.X - start, noise.ToArray());
        }
        var solo = Move(1);
        Assert.InRange(solo.Distance, 2.5f, 4);
        Assert.NotEmpty(solo.Noise);
        Assert.All(solo.Noise, n => Assert.Equal(30, n.Radius));
        foreach (var count in new[] { 2, 3, 4 })
        {
            var team = Move(count);
            Assert.InRange(team.Distance, 11.9f, 12.1f);
            Assert.Empty(team.Noise);
        }
    }

    [Fact]
    public void The_heaviest_Furniture_requires_a_cart_or_disassembly_and_carts_haul_faster()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(20, 104));
        var ids = Enumerable.Range(0, 4).Select(_ => sim.Scenario.PlaceVillager(new(17, 104)).Id).ToArray();
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.CarryFurniture(piece.Id, [ids[0]], new(60, 104)));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.CarryFurniture(piece.Id, ids, new(60, 104)));
        sim.Scenario.EquipGear(ids[0], GearKind.ShoppingCart);
        sim.Scenario.CarryFurniture(piece.Id, [ids[0]], new(60, 104));
        sim.Advance(TimeSpan.FromMinutes(12));
        Assert.True(sim.Snapshot.Furniture[0].Position.X > 34);
        Assert.Equal(VillagerAction.CartHauling, sim.Snapshot.Villagers[0].Action);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(99, sim.Snapshot.Villagers[0].Hand!.Durability);
    }

    [Fact]
    public void Forced_disassembly_is_slower_noisier_and_yields_less_leather()
    {
        static (int Leather, int Noise, long Ticks) Dismantle(bool knife)
        {
            using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
            var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 104));
            var v = sim.Scenario.PlaceVillager(new(18, 104));
            sim.Scenario.PlaceVillage(v.Position, [v.Id]);
            if (knife) sim.Scenario.EquipGear(v.Id, GearKind.KitchenKnife);
            var noise = 0;
            using var sub = sim.Subscribe(e => { if (e is NoiseEmitted) noise++; });
            var startTick = sim.Snapshot.Clock.Tick;
            sim.Scenario.DisassembleFurniture(piece.Id, v.Id);
            for (var i = 0; i < 2000 && sim.Snapshot.Furniture.Count > 0; i++) sim.AdvanceTicks(1);
            Assert.Empty(sim.Snapshot.Furniture);
            return (sim.Snapshot.Villages[0].Materials[MaterialKind.Leather], noise, sim.Snapshot.Clock.Tick - startTick);
        }
        var forced = Dismantle(false);
        var tooled = Dismantle(true);
        Assert.Equal(2, forced.Leather);
        Assert.Equal(4, tooled.Leather);
        Assert.True(forced.Ticks > tooled.Ticks * 2);
        Assert.True(forced.Noise > tooled.Noise);
        Assert.Equal(0, tooled.Noise);
    }

    [Fact]
    public void Active_carrying_and_disassembly_survive_save_and_continue_to_the_same_result()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var box = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 112));
        var a = sim.Scenario.PlaceVillager(new(18, 104));
        var b = sim.Scenario.PlaceVillager(new(18, 112));
        sim.Scenario.PlaceVillage(b.Position, [b.Id]);
        sim.Scenario.EquipGear(b.Id, GearKind.KitchenKnife);
        sim.Scenario.CarryFurniture(box.Id, [a.Id], new(60, 104));
        sim.Scenario.DisassembleFurniture(chair.Id, b.Id);
        sim.Advance(TimeSpan.FromMinutes(12));
        var frozen = sim.Snapshot;
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(2, loaded.Snapshot.FurnitureTasks.Count);
        sim.Advance(TimeSpan.FromHours(1));
        loaded.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.FurnitureTasks);
        Assert.Empty(loaded.Snapshot.FurnitureTasks);
        Assert.Equal(sim.Snapshot.Furniture, loaded.Snapshot.Furniture);
        Assert.Equal(sim.Snapshot.Villages[0].Materials, loaded.Snapshot.Villages[0].Materials);
        Assert.Equal(sim.Snapshot.Villagers[1].Skills, loaded.Snapshot.Villagers[1].Skills);
        Assert.Equal(2, frozen.FurnitureTasks.Count);
        Assert.Empty(frozen.Villages[0].Materials);
    }

    [Fact]
    public void Disassembly_removes_the_armchair_and_deposits_Materials_in_its_Village()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 104));
        var worker = sim.Scenario.PlaceVillager(new(18, 104));
        sim.Scenario.PlaceVillage(new(18, 104), [worker.Id]);
        sim.Scenario.EquipGear(worker.Id, GearKind.KitchenKnife);
        sim.Scenario.DisassembleFurniture(piece.Id, worker.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Furniture);
        var materials = Assert.Single(sim.Snapshot.Villages).Materials;
        Assert.Equal(4, materials[MaterialKind.Leather]);
        Assert.Equal(4, materials[MaterialKind.Foam]);
        Assert.Equal(3, materials[MaterialKind.WoodPanel]);
        Assert.True(sim.Snapshot.Villagers[0].Skills[Role.Builder] > 0);
        Assert.True(sim.Snapshot.Villagers[0].Hand!.Durability < 100);
    }

    [Fact]
    public void Carrying_moves_Furniture_and_reopens_the_floor_for_navigation()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104));
        var worker = sim.Scenario.PlaceVillager(new(18, 104));
        sim.Scenario.CarryFurniture(piece.Id, [worker.Id], new(32, 104));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(new Vector2(32, 104), Assert.Single(sim.Snapshot.Furniture).Position);
        var visitor = sim.Scenario.PlaceVillager(new(20, 104));
        Assert.Equal(new Vector2(20, 104), visitor.Position);
        sim.Scenario.SendVillager(visitor.Id, new(32, 104));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.NotEqual(new Vector2(32, 104), sim.Snapshot.Villagers.Single(v => v.Id == visitor.Id).Position);
        Assert.Empty(sim.Snapshot.FurnitureTasks);
    }
}
