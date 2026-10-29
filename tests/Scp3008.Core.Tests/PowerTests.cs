using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PowerTests
{
    private static Simulation Home()
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var person = sim.Scenario.PlaceVillager(new(20, 104));
        sim.Scenario.PlaceVillage(person.Position, [person.Id]);
        sim.Scenario.AssignRole(person.Id, Role.Guard);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
        return sim;
    }

    [Fact]
    public void A_moved_Lamp_consumes_a_battery_and_goes_dark_when_it_depletes()
    {
        using var sim = Home();
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(10, 100));
        sim.Scenario.MoveFurniture(lamp.Id, new(21, 104), 0);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 1);
        sim.AdvanceTicks(1);
        Assert.Equal(1, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
        Assert.False(sim.Snapshot.Villages.Single().Stores.ContainsKey(GoodsKind.Batteries));
        sim.Advance(TimeSpan.FromHours(4.1));
        Assert.Equal(0, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
        Assert.Empty(sim.Snapshot.LitAreas);
    }

    [Fact]
    public void A_generator_lights_a_moved_Lamp_only_while_a_Villager_cranks_and_costs_rest()
    {
        using var sim = Home();
        sim.Scenario.EquipGear(0, GearKind.HandCrankedGenerator);
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(10, 100));
        sim.Scenario.MoveFurniture(lamp.Id, new(21, 104), 0);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(VillagerAction.Cranking, sim.Snapshot.Villagers.Single().Action);
        Assert.Equal(1, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
        Assert.InRange(sim.Snapshot.Villagers.Single().Needs.Rest, 0.8, 0.87);
        sim.Scenario.SendVillager(0, new(35, 104));
        sim.AdvanceTicks(1);
        Assert.NotEqual(VillagerAction.Cranking, sim.Snapshot.Villagers.Single().Action);
        Assert.Equal(0, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
        sim.Advance(TimeSpan.FromHours(4));
        Assert.NotEqual(VillagerAction.Cranking, sim.Snapshot.Villagers.Single().Action);
        Assert.Equal(0, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
    }

    [Fact]
    public void Candles_give_weak_light_then_burn_out()
    {
        using var sim = Home();
        sim.Scenario.EquipGear(0, GearKind.Candle);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0.3f, Assert.Single(sim.Snapshot.LitAreas).Brightness);
        Assert.InRange(sim.Snapshot.Villagers.Single().Hand!.Durability, 74.9, 75.1);
        sim.Advance(TimeSpan.FromHours(3.1));
        Assert.Null(sim.Snapshot.Villagers.Single().Hand);
        Assert.Empty(sim.Snapshot.LitAreas);
    }

    [Fact]
    public void A_lit_Village_sees_an_approaching_group_of_Staff_before_a_dark_Village()
    {
        using var lit = Home();
        using var dark = Home();
        lit.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(20, 106));
        foreach (var sim in new[] { lit, dark })
        {
            sim.Scenario.PlaceStaff(new(45, 104), [new(20, 104)]);
            sim.Scenario.PlaceStaff(new(46, 104), [new(20, 104)]);
            sim.AdvanceTicks(1);
        }
        Assert.Equal(2, lit.Snapshot.Villagers.Single().KnownMap.Dangers.Count);
        Assert.Empty(dark.Snapshot.Villagers.Single().KnownMap.Dangers);
        Assert.Equal(lit.Snapshot.Staff.Select(s => s.Position), dark.Snapshot.Staff.Select(s => s.Position));
    }

    [Fact]
    public void Light_keeps_Villagers_working_at_night_and_reduces_morale_loss()
    {
        using var lit = Home();
        using var dark = Home();
        lit.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(20, 106));
        foreach (var sim in new[] { lit, dark })
        {
            sim.Scenario.AssignRole(0, Role.Scavenger);
            var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(24, 104));
            sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
            sim.Advance(TimeSpan.FromMinutes(30));
        }
        Assert.True(lit.Snapshot.Villages.Single().Stores.GetValueOrDefault(GoodsKind.TinnedFood) > 0);
        Assert.Equal(0, dark.Snapshot.Villages.Single().Stores.GetValueOrDefault(GoodsKind.TinnedFood));
        Assert.True(lit.Snapshot.Villagers.Single().Needs.Morale > dark.Snapshot.Villagers.Single().Needs.Morale);
    }

    [Theory]
    [InlineData(GearKind.Candle)]
    [InlineData(GearKind.HandCrankedGenerator)]
    [InlineData(GearKind.Flashlight)]
    public void Light_state_and_its_continuation_survive_save_load(GearKind kind)
    {
        using var sim = Home();
        sim.Scenario.EquipGear(0, kind);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 2);
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(10, 100));
        sim.Scenario.MoveFurniture(lamp.Id, new(20, 106), 0);
        sim.Advance(TimeSpan.FromMinutes(30));
        using var stream = new MemoryStream();
        sim.Save(stream); stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(sim.Snapshot.LampPower, loaded.Snapshot.LampPower);
        Assert.Equal(sim.Snapshot.LitAreas, loaded.Snapshot.LitAreas);
        sim.Advance(TimeSpan.FromHours(4)); loaded.Advance(TimeSpan.FromHours(4));
        Assert.Equal(sim.Snapshot.LampPower, loaded.Snapshot.LampPower);
        Assert.Equal(sim.Snapshot.LitAreas, loaded.Snapshot.LitAreas);
        Assert.Equal(sim.Snapshot.Villagers.Single().Needs, loaded.Snapshot.Villagers.Single().Needs);
        Assert.Equal(sim.Snapshot.Villagers.Single().Hand, loaded.Snapshot.Villagers.Single().Hand);
    }

    [Fact]
    public void Villagers_switch_on_nearby_display_Lamps_during_open_hours_without_batteries()
    {
        using var sim = Home();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(20, 106));
        Assert.Equal(0, sim.Snapshot.LampLevel(lamp));
        sim.AdvanceTicks(1);
        Assert.Equal(1, sim.Snapshot.LampLevel(lamp));
        Assert.Empty(sim.Snapshot.Villages.Single().Stores);
        sim.Scenario.MoveFurniture(lamp.Id, new(21, 106), 0);
        sim.AdvanceTicks(1);
        Assert.Equal(0, sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single()));
    }

    [Fact]
    public void A_display_Lamp_lights_a_Wanderer_across_a_Seam_away_from_any_Village()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        sim.Scenario.PlaceVillager(new(126, 104));
        sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(1, 106));
        sim.Scenario.PlaceStaff(new(103, 104), [new(126, 104)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(23));
        sim.AdvanceTicks(1);
        Assert.Equal(1, sim.Snapshot.LightBrightnessAt(new(126, 104)));
        Assert.Single(sim.Snapshot.Villagers.Single().KnownMap.Dangers);
    }

    [Fact]
    public void Removing_a_powered_Lamp_removes_its_saved_power_state()
    {
        using var sim = Home();
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(10, 100));
        sim.Scenario.MoveFurniture(lamp.Id, new(20, 106), 0);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 1);
        sim.AdvanceTicks(1);
        sim.Scenario.RemoveFurniture(p => p.Id == lamp.Id);
        using var stream = new MemoryStream(); sim.Save(stream); stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Empty(loaded.Snapshot.LampPower);
        Assert.Empty(loaded.Snapshot.LitAreas);
    }
}
