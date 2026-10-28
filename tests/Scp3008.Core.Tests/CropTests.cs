using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class CropTests
{
    private const int Day = 12000;

    // A Village of farmers by a Crop; the Water point stands in sight of it unless the scenario has none.
    private static Simulation Farm(bool water = true, int farmers = 2, int tinnedFood = 1000, params Vector2[] crops)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, farmers).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids);
        foreach (var id in ids) sim.Scenario.AssignRole(id, Role.Farmer);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, tinnedFood);
        if (water) sim.Scenario.PlaceWaterPoint(new(26, 104));
        foreach (var position in crops.Length > 0 ? crops : [new Vector2(22, 108)])
            sim.Scenario.PlaceFurniture(FurnitureType.Crop, position);
        return sim;
    }

    private static List<CropHarvested> Harvests(Simulation sim, int ticks)
    {
        var harvested = new List<CropHarvested>();
        using var subscription = sim.Subscribe(e => { if (e is CropHarvested h) harvested.Add(h); });
        sim.AdvanceTicks(ticks);
        return harvested;
    }

    [Fact]
    public void Generation_places_Crops_only_in_the_Plant_section()
    {
        using var sim = Simulation.Create(new WorldConfig(3008));
        var snapshot = sim.Snapshot;
        var crops = snapshot.Furniture.Where(p => p.Type == FurnitureType.Crop).ToArray();
        Assert.NotEmpty(crops);
        Assert.All(crops, p => Assert.Equal(DepartmentType.PlantSection, snapshot.Departments.At(p.Position).Type));
        Assert.Contains(snapshot.Furniture, p => p.Type == FurnitureType.PottedPlant);
        Assert.Equal(crops.Select(p => p.Id), snapshot.Crops.Select(c => c.FurnitureId).Order());
        Assert.All(snapshot.Crops, c => Assert.False(c.Tended));
    }

    [Fact]
    public void A_tended_Crop_yields_food_over_Store_days()
    {
        using var sim = Farm();
        var harvested = Harvests(sim, 5 * Day);
        Assert.True(harvested.Count >= 2, $"{harvested.Count} harvests");
        Assert.All(harvested, h => Assert.Equal(Crop.Yield, h.Amount));
        Assert.False(sim.Snapshot.Crops.Single().Withered);
    }

    [Fact]
    public void An_untended_Crop_stops_yielding_and_withers()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Crop, new(22, 108));
        sim.Scenario.WaterCrop(piece.Id);
        var withered = new List<CropWithered>();
        using var subscription = sim.Subscribe(e => { if (e is CropWithered w) withered.Add(w); });
        sim.AdvanceTicks(4 * Day);
        var crop = sim.Snapshot.Crops.Single();
        Assert.True(crop.Withered);
        Assert.False(crop.Ripe);
        Assert.Equal(piece.Id, Assert.Single(withered).FurnitureId);
        var growth = crop.Growth;
        sim.AdvanceTicks(Day);
        Assert.Equal(growth, sim.Snapshot.Crops.Single().Growth);
    }

    [Fact]
    public void Tending_needs_water_from_a_Water_point()
    {
        using var dry = Farm(water: false);
        Assert.Empty(Harvests(dry, 3 * Day));
        Assert.Equal(0, dry.Snapshot.Crops.Single().Growth);

        using var watered = Farm();
        Harvests(watered, Day / 2);
        Assert.True(watered.Snapshot.Crops.Single().Growth > 0);
        Assert.Contains(watered.Snapshot.Villagers, v => v.CarriedWater > 0 || v.Action == VillagerAction.Tending);
    }

    [Fact]
    public void A_Crop_carried_inside_the_walls_keeps_growing_when_tended()
    {
        using var sim = Farm(crops: [new(55, 104)]);
        var walls = sim.Scenario.PlanBarricade(0);
        var piece = sim.Snapshot.Furniture.Single(p => p.Type == FurnitureType.Crop);
        var harvested = Harvests(sim, 5 * Day);
        var home = sim.Snapshot.Furniture.Single(p => p.Id == piece.Id).Position;
        Assert.True(Inside(walls.Vertices, home), $"Crop at {home}");
        Assert.NotEmpty(harvested);
        Assert.False(sim.Snapshot.Crops.Single().Withered);
    }

    [Fact]
    public void A_Village_with_farmers_and_Crops_gets_food_without_scavenging()
    {
        using var sim = Farm(farmers: 3, tinnedFood: 0, crops: [new(22, 108), new(18, 108), new(22, 100)]);
        var delivered = 0;
        using var subscription = sim.Subscribe(e => { if (e is GoodsDelivered { Kind: GoodsKind.FreshFood }) delivered++; });
        for (var i = 0; i < 4 * Day / 50; i++)
        {
            sim.AdvanceTicks(50);
            Assert.DoesNotContain(sim.Snapshot.Villagers, v => v.Action == VillagerAction.Scavenging || v.Role != Role.Farmer);
        }
        Assert.True(delivered > 0);
    }

    [Fact]
    public void Crops_and_their_growth_survive_a_save_load_round_trip()
    {
        using var original = Farm();
        original.Scenario.PlaceFurniture(FurnitureType.Crop, new(40, 120));
        original.AdvanceTicks(Day / 2);
        Assert.Contains(original.Snapshot.Crops, c => c.Growth > 0);
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(original.Snapshot.Crops, loaded.Snapshot.Crops);
        original.AdvanceTicks(Day);
        loaded.AdvanceTicks(Day);
        Assert.True(loaded.Snapshot.Crops.Max(c => c.Growth) > 0);
    }

    private static bool Inside(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                inside = !inside;
        return inside;
    }
}
