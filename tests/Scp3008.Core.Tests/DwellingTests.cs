using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class DwellingTests
{
    [Fact]
    public void A_sleeping_Villager_follows_their_bed_when_it_moves()
    {
        using var sim = Ready(null, bed: true, people: 1);
        var place = sim.Snapshot.Villagers[0].SleepingPlace!;
        sim.Scenario.MoveFurniture(place.FurnitureId!.Value, new(30, 106), 0);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(place, sim.Snapshot.Villagers[0].SleepingPlace);
        Assert.True(sim.Snapshot.Space.Distance(sim.Snapshot.Villagers[0].Position, new(30, 106)) < 3);
    }

    [Fact]
    public void A_Dwelling_stays_on_the_Village_side_of_a_shelving_anchor()
    {
        using var sim = Ready(null);
        sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(17, 104), rotation: MathF.PI / 2);
        var wall = sim.Scenario.PlanBarricade(0);
        Assert.Equal(WallOutline.Freeform, wall.Outline);
        var dwelling = sim.Scenario.PlanDwelling(0, DwellingKind.House);
        Assert.True(dwelling.Position.X >= 19.5f, $"Dwelling behind the shelving anchor: {dwelling.Position}");
    }

    [Fact]
    public void Wrecked_cover_no_longer_provides_sleeping_places_and_can_be_saved()
    {
        using var sim = Ready(DwellingKind.Tent);
        var cover = sim.Snapshot.Villages[0].Dwellings[0].Slots[1].FurnitureId!.Value;
        sim.Scenario.DamageFurniture(cover, 1);
        sim.AdvanceTicks(1);
        Assert.False(sim.Snapshot.Villages[0].Dwellings[0].Complete);
        Assert.DoesNotContain(sim.Snapshot.Villagers, v => v.SleepingPlace?.DwellingId == 0);
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(0, loaded.Snapshot.Villages[0].Dwellings[0].SleepingPlaces);
    }

    [Fact]
    public void Builders_put_up_a_frame_and_fabric_tent()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, 2).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        sim.Scenario.PlaceFurniture(FurnitureType.CurtainRack, new(12, 100));
        sim.Scenario.PlaceFurniture(FurnitureType.Rug, new(12, 106));
        sim.Scenario.PlanDwelling(0, DwellingKind.Tent);
        for (var i = 0; i < 6000 && !sim.Snapshot.Villages[0].Dwellings[0].Complete; i++) sim.AdvanceTicks(1);
        var tent = sim.Snapshot.Villages[0].Dwellings[0];
        Assert.True(tent.Complete);
        Assert.Equal(2, tent.SleepingPlaces);
    }
    private static Simulation Ready(DwellingKind? kind, bool bed = false, int people = 2, Personality personality = Personality.Cautious,
        bool alternate = false, Action<long>? built = null, bool tentAlso = false)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var startTick = sim.Snapshot.Clock.Tick;
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids, personality);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        sim.Scenario.AssignRole(ids[0], Role.Builder);
        if (kind == DwellingKind.House) sim.Scenario.EquipGear(ids[0], GearKind.ShoppingCart);
        if (kind is { } k)
        {
            var types = k == DwellingKind.Tent ? new[] { FurnitureType.CurtainRack, FurnitureType.Rug } :
                new[] { alternate ? FurnitureType.Bookcase : FurnitureType.Wardrobe,
                    alternate ? FurnitureType.Bookcase : FurnitureType.Wardrobe,
                    alternate ? FurnitureType.Bookcase : FurnitureType.Wardrobe,
                    FurnitureType.Wardrobe, alternate ? FurnitureType.MeetingTable : FurnitureType.DiningTable,
                    alternate ? FurnitureType.MeetingTable : FurnitureType.DiningTable };
            for (var i = 0; i < types.Length; i++) sim.Scenario.PlaceFurniture(types[i], new(10 + i * 3, 96), tint: i);
            sim.Scenario.PlanDwelling(0, k);
            if (tentAlso)
            {
                sim.Scenario.PlaceFurniture(FurnitureType.CurtainRack, new(28, 96));
                sim.Scenario.PlaceFurniture(FurnitureType.Rug, new(31, 96));
                sim.Scenario.PlanDwelling(0, DwellingKind.Tent);
            }
            for (var i = 0; i < 16000 && sim.Snapshot.Villages[0].Dwellings.Any(d => !d.Complete); i++) sim.AdvanceTicks(1);
            Assert.True(sim.Snapshot.Villages[0].Dwellings[0].Complete, System.Text.Json.JsonSerializer.Serialize(new { D = sim.Snapshot.Villages[0].Dwellings[0], V = sim.Snapshot.Villagers, T = sim.Snapshot.FurnitureTasks }));
        }
        built?.Invoke(sim.Snapshot.Clock.Tick - startTick);
        if (bed) sim.Scenario.PlaceFurniture(FurnitureType.Bed, new(20, 106));
        foreach (var v in sim.Snapshot.Villagers)
        {
            sim.Scenario.AssignRole(v.Id, Role.Guard);
            sim.Scenario.SetNeeds(v.Id, new Needs(Rest: 0.1, Morale: 0.6));
            sim.Scenario.SendVillager(v.Id, new(18, 104));
        }
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        sim.Advance(TimeSpan.FromMinutes(30));
        return sim;
    }

    [Fact]
    public void House_tent_bed_and_floor_give_successively_less_rest_and_morale()
    {
        static Needs Gain(DwellingKind? kind, bool bed)
        {
            using var sim = Ready(kind, bed, people: 1);
            var before = sim.Snapshot.Villagers[0].Needs;
            sim.Advance(TimeSpan.FromHours(1));
            var after = sim.Snapshot.Villagers[0].Needs;
            return new Needs(Rest: after.Rest - before.Rest, Morale: after.Morale - before.Morale);
        }
        var house = Gain(DwellingKind.House, false);
        var tent = Gain(DwellingKind.Tent, false);
        var bed = Gain(null, true);
        var floor = Gain(null, false);
        Assert.True(house.Rest > tent.Rest && tent.Rest > bed.Rest && bed.Rest > floor.Rest);
        Assert.True(house.Morale > tent.Morale && tent.Morale > bed.Morale && bed.Morale > floor.Morale);
    }

    [Fact]
    public void The_best_free_places_fill_first_and_overflow_sleeps_on_the_floor()
    {
        using var sim = Ready(DwellingKind.Tent, bed: true, people: 4);
        var people = sim.Snapshot.Villagers;
        Assert.Equal(2, people.Count(v => v.SleepingPlace?.DwellingId == 0));
        Assert.Single(people, v => v.SleepingPlace?.FurnitureId is not null);
        var floor = Assert.Single(people, v => v.SleepingPlace is null);
        Assert.Equal(VillagerAction.Resting, floor.Action);
        var before = floor.Needs.Morale;
        sim.Advance(TimeSpan.FromHours(1));
        Assert.True(sim.Snapshot.Villagers.Single(v => v.Id == floor.Id).Needs.Morale < before);
        var usual = sim.Snapshot.Villagers.Select(v => v.SleepingPlace).ToArray();
        foreach (var v in sim.Snapshot.Villagers) sim.Scenario.SetNeeds(v.Id, new Needs(Rest: 1));
        sim.Advance(TimeSpan.FromMinutes(10));
        foreach (var v in sim.Snapshot.Villagers) sim.Scenario.SetNeeds(v.Id, new Needs(Rest: 0.1));
        sim.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(usual, sim.Snapshot.Villagers.Select(v => v.SleepingPlace));
    }

    [Fact]
    public void A_house_Blueprint_accepts_two_different_Furniture_sets_and_tents_go_up_much_faster()
    {
        long tentTicks = 0, houseTicks = 0;
        using var tent = Ready(DwellingKind.Tent, built: ticks => tentTicks = ticks);
        using var house = Ready(DwellingKind.House, built: ticks => houseTicks = ticks);
        using var other = Ready(DwellingKind.House, alternate: true);
        Assert.True(houseTicks > tentTicks * 1.5, $"House {houseTicks} ticks, tent {tentTicks} ticks");
        var a = house.Snapshot.Villages[0].Dwellings[0].Slots.Select(s => house.Snapshot.Furniture.Single(p => p.Id == s.FurnitureId).Type);
        var b = other.Snapshot.Villages[0].Dwellings[0].Slots.Select(s => other.Snapshot.Furniture.Single(p => p.Id == s.FurnitureId).Type);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(Personality.Cautious, 2)]
    [InlineData(Personality.Builders, 2)]
    [InlineData(Personality.Bold, 1)]
    public void Personality_controls_sharing_and_changes_who_can_sleep_there(Personality personality, int covered)
    {
        using var sim = Ready(DwellingKind.Tent, people: 4, personality: personality);
        Assert.Equal(covered, sim.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 0));
        Assert.Equal(covered, sim.Snapshot.Villages[0].CoveredSleepingPlaces);
        if (personality == Personality.Builders)
            Assert.Equal(new[] { 0, 1 }, sim.Snapshot.Villagers.Where(v => v.SleepingPlace?.DwellingId == 0).Select(v => v.Id));
        sim.Scenario.SetPersonality(0, Personality.Bold);
        sim.Advance(TimeSpan.FromMinutes(12));
        Assert.Single(sim.Snapshot.Villagers, v => v.SleepingPlace?.DwellingId == 0);
        sim.Scenario.SetPersonality(0, Personality.Cautious);
        sim.Advance(TimeSpan.FromMinutes(12));
        Assert.Equal(2, sim.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 0));
    }

    [Fact]
    public void Housing_expands_the_wall_outline_when_the_inner_space_runs_out()
    {
        using var sim = Ready(DwellingKind.Tent);
        var first = sim.Snapshot.Villages[0].HomeWalls().Last();
        for (var i = 0; i < 5; i++) sim.Scenario.PlanDwelling(0, DwellingKind.House);
        var village = sim.Snapshot.Villages[0];
        var outer = village.HomeWalls().Last();
        Assert.True(village.HomeWalls().Count() > 1);
        Assert.True(sim.Snapshot.Space.Distance(village.Position, outer.Vertices[0]) >
            sim.Snapshot.Space.Distance(village.Position, first.Vertices[0]));
        Assert.All(village.Dwellings, d => Assert.True(sim.Snapshot.Space.Distance(village.Position, d.Position) + 2 <
            sim.Snapshot.Space.Distance(village.Position, outer.Vertices[0])));
        sim.Scenario.AssignRole(0, Role.Builder);
        sim.Scenario.EquipGear(0, GearKind.ShoppingCart);
        for (var i = 0; i < 40000 && !sim.Snapshot.Villages[0].HomeWalls().All(w => w.Complete); i++)
        {
            if (i % 500 == 0)
            {
                sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
                sim.Scenario.SetNeeds(0, new Needs());
            }
            if (sim.Snapshot.Villagers[0].Hand is null) sim.Scenario.EquipGear(0, GearKind.ShoppingCart);
            if (sim.Snapshot.FurnitureTasks.Count == 0 && i % 50 == 0)
            {
                var slot = sim.Snapshot.Villages[0].HomeWalls().SelectMany(w => w.Slots).First(s => s.FurnitureId is null);
                var narrow = new FurniturePiece(-1, FurnitureType.Wardrobe, default, 0, 2);
                // Deliver outside the wall, clear of both the next slot and the Builder unloading at the previous one.
                var source = sim.Snapshot.Space.Wrap(slot.Position + System.Numerics.Vector2.Normalize(sim.Snapshot.Space.Delta(village.Position, slot.Position)) * 4);
                if (slot.Fits(narrow)) sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, source, variant: 2);
                else sim.Scenario.PlaceFurniture(FurnitureType.Dresser, source);
            }
            sim.AdvanceTicks(1);
        }
        Assert.All(sim.Snapshot.Villages[0].HomeWalls(), w => Assert.True(w.Complete,
            $"Only {w.Slots.Count(s => s.FurnitureId is not null)}/{w.Slots.Count} wall pieces built; {sim.Snapshot.Furniture.Count} Furniture; worker {sim.Snapshot.Villagers[0].Action}, tasks {sim.Snapshot.FurnitureTasks.Count}"));
    }

    [Fact]
    public void Sleeping_and_construction_survive_save_load_and_invalid_references_are_rejected()
    {
        using var sim = Ready(DwellingKind.House, people: 3);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Villagers.Select(v => v.SleepingPlace), loaded.Snapshot.Villagers.Select(v => v.SleepingPlace));
        sim.Advance(TimeSpan.FromMinutes(10)); loaded.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(sim.Snapshot.Villagers.Select(v => v.Needs), loaded.Snapshot.Villagers.Select(v => v.Needs));
        Assert.Equal(sim.Snapshot.Villages[0].CoveredSleepingPlaces, loaded.Snapshot.Villages[0].CoveredSleepingPlaces);
        foreach (var field in new[] { "FurnitureId", "ReservedFurnitureId" })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
            data["State"]!["Snapshot"]!["Villages"]![0]!["Dwellings"]![0]!["Slots"]![0]![field] = 999999;
            using var bad = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            Assert.Throws<InvalidDataException>(() => Simulation.Load(bad));
        }
        var invalidPlace = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        invalidPlace["State"]!["Villagers"]!["Walkers"]![0]!["Villager"]!["SleepingPlace"]!["DwellingId"] = 999999;
        using var invalidSleep = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalidPlace.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(invalidSleep));
        var wall = loaded.Snapshot.Villages[0].Dwellings[0].Slots[0].FurnitureId;
        loaded.Scenario.RemoveFurniture(p => p.Id == wall);
        loaded.AdvanceTicks(1);
        Assert.False(loaded.Snapshot.Villages[0].Dwellings[0].Complete);
        Assert.DoesNotContain(loaded.Snapshot.Villagers, v => v.SleepingPlace?.DwellingId == 0);
    }

    [Fact]
    public void A_house_is_upgraded_one_Furniture_piece_at_a_time()
    {
        using var sim = Ready(DwellingKind.House, alternate: true);
        sim.Scenario.SetMaterials(0, MaterialKind.Metal, 20);
        sim.Scenario.SetMaterials(0, MaterialKind.Foam, 30);
        sim.Scenario.SetNeeds(0, new Needs());
        sim.Scenario.AssignRole(0, Role.Builder);
        for (var i = 0; i < 1000 && !sim.Snapshot.Villages[0].Dwellings[0].Slots.Any(s => s.Strapped); i++) sim.AdvanceTicks(1);
        Assert.Single(sim.Snapshot.Villages[0].Dwellings[0].Slots, s => s.Strapped);
        for (var i = 0; i < 2000 && sim.Snapshot.Villages[0].Dwellings[0].Slots.Any(s => s.Tier < 3); i++) sim.AdvanceTicks(1);
        Assert.All(sim.Snapshot.Villages[0].Dwellings[0].Slots, s => Assert.Equal(3, s.Tier));
    }

    [Fact]
    public void A_reserved_piece_and_unfinished_fitting_survive_loading()
    {
        using var sim = Ready(null);
        sim.Scenario.SetNeeds(0, new Needs());
        sim.Scenario.AssignRole(0, Role.Builder);
        sim.Scenario.PlaceFurniture(FurnitureType.CurtainRack, new(10, 100));
        sim.Scenario.PlaceFurniture(FurnitureType.Rug, new(10, 106));
        sim.Scenario.PlanDwelling(0, DwellingKind.Tent);
        for (var i = 0; i < 2000 && !sim.Snapshot.Villages[0].Dwellings[0].Slots.Any(s => s.ReservedFurnitureId is not null); i++) sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villages[0].Dwellings[0].Slots, s => s.ReservedFurnitureId is not null);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        for (var i = 0; i < 4000 && !loaded.Snapshot.Villages[0].Dwellings[0].Complete; i++) loaded.AdvanceTicks(1);
        Assert.True(loaded.Snapshot.Villages[0].Dwellings[0].Complete);
        var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        data["State"]!["Snapshot"]!["Villages"]![0]!["Dwellings"]![0]!["SetupHours"] = -1;
        using var bad = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(bad));
    }

    [Fact]
    public void A_Village_builds_both_kinds_and_fills_house_then_tent_then_bed_then_floor()
    {
        using var sim = Ready(DwellingKind.House, bed: true, people: 6, alternate: true, tentAlso: true);
        Assert.All(sim.Snapshot.Villages[0].Dwellings, d => Assert.True(d.Complete));
        Assert.Equal(2, sim.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 0));
        Assert.Equal(2, sim.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 1));
        Assert.Single(sim.Snapshot.Villagers, v => v.SleepingPlace?.FurnitureId is not null);
        Assert.Single(sim.Snapshot.Villagers, v => v.SleepingPlace is null);
    }

}
