using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StorehouseTests
{
    private const int Day = 12000;

    private static Simulation Village(Personality personality = Personality.Cautious, int people = 3, int shelves = 3)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
        sim.Scenario.PlaceVillage(new(20, 104), ids, personality);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
        for (var i = 0; i < shelves; i++) sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(28 + i * 3, 110));
        return sim;
    }

    private static Simulation WithStorehouse(Personality personality = Personality.Cautious)
    {
        var sim = Village(personality);
        for (var i = 0; i < 12000 && sim.Snapshot.Villages[0].Storehouse?.Complete != true; i++) sim.AdvanceTicks(1);
        Assert.True(sim.Snapshot.Villages[0].Storehouse!.Complete);
        return sim;
    }

    private static int Spoiled(Simulation sim, GoodsKind kind, int ticks)
    {
        var lost = 0;
        using var subscription = sim.Subscribe(e => { if (e is FoodSpoiled f && f.Kind == kind) lost += f.Amount; });
        sim.AdvanceTicks(ticks);
        return lost;
    }

    [Fact]
    public void Food_on_a_Store_shelf_does_not_spoil()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.FreshFood, 10);
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 10);
        sim.AdvanceTicks(5 * Day);
        Assert.Equal(10, sim.Snapshot.Stock.Single(g => g.Kind == GoodsKind.FreshFood).Amount);
        Assert.Equal(10, sim.Snapshot.Stock.Single(g => g.Kind == GoodsKind.TinnedFood).Amount);
    }

    [Fact]
    public void Taken_food_spoils_and_fresh_food_spoils_faster_than_tinned()
    {
        using var sim = Village(shelves: 0);
        sim.Scenario.SetStores(0, GoodsKind.FreshFood, 100);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 100);
        var fresh = 0;
        var tinned = 0;
        using var subscription = sim.Subscribe(e =>
        {
            if (e is FoodSpoiled f) { if (f.Kind == GoodsKind.FreshFood) fresh += f.Amount; else if (f.Kind == GoodsKind.TinnedFood) tinned += f.Amount; }
        });
        sim.AdvanceTicks(Day);
        Assert.True(tinned > 0);
        Assert.True(fresh > 2 * tinned);
    }

    [Fact]
    public void Food_put_back_on_a_shelf_keeps_spoiling()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        sim.Scenario.PutBack(shelf.Id, GoodsKind.FreshFood, 20);
        Assert.Equal(20, sim.Snapshot.Stock.Single(g => g.Kind == GoodsKind.FreshFood).Amount);
        sim.AdvanceTicks(2 * Day);
        Assert.True(sim.Snapshot.Stock.Where(g => g.Kind == GoodsKind.FreshFood).Sum(g => g.Amount) < 20);
    }

    [Fact]
    public void Food_in_a_Storehouse_spoils_more_slowly_than_in_an_open_pile()
    {
        using var housed = WithStorehouse();
        using var open = Village(shelves: 0);
        foreach (var sim in new[] { housed, open })
        {
            sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 0);
            sim.Scenario.SetStores(0, GoodsKind.BottledWater, 0);
            sim.Scenario.SetStores(0, GoodsKind.FreshFood, 36);
        }
        var inside = Spoiled(housed, GoodsKind.FreshFood, Day / 2);
        var pile = Spoiled(open, GoodsKind.FreshFood, Day / 2);
        Assert.Equal(0, housed.Snapshot.Villages[0].PileTotal);
        Assert.True(inside > 0);
        Assert.True(pile > inside);
    }

    [Fact]
    public void Stores_beyond_the_Storehouse_capacity_go_to_the_pile()
    {
        using var sim = WithStorehouse();
        var village = sim.Snapshot.Villages[0];
        Assert.Equal(36, village.StorehouseCapacity);
        sim.Scenario.SetStores(0, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(0, GoodsKind.BottledWater, 0);
        village = sim.Snapshot.Villages[0];
        Assert.Equal(64, village.PileTotal);
        Assert.Equal(64, village.PileGoods().Sum(g => g.Value));
    }

    [Fact]
    public void A_cautious_Village_keeps_its_Furniture_yard_inside_the_walls()
    {
        using var sim = Village(Personality.Cautious, shelves: 0);
        for (var i = 0; i < 400; i++) sim.AdvanceTicks(1);
        var village = sim.Snapshot.Villages[0];
        var yard = village.FurnitureYard;
        Assert.NotNull(yard);
        Assert.True(yard.Inside);
        Assert.Null(yard.WallIndex);
        Assert.True(sim.Snapshot.Space.Distance(yard.Position, village.Position) < 6);
    }

    [Fact]
    public void Another_Village_keeps_its_Furniture_yard_outside_behind_its_own_barricade()
    {
        using var sim = Village(Personality.Bold, shelves: 0);
        for (var i = 0; i < 400; i++) sim.AdvanceTicks(1);
        var village = sim.Snapshot.Villages[0];
        var yard = village.FurnitureYard;
        Assert.NotNull(yard);
        Assert.False(yard.Inside);
        var wall = village.Walls[yard.WallIndex!.Value];
        Assert.All(wall.Vertices, v => Assert.True(sim.Snapshot.Space.Distance(v, yard.Position) < 3));
        Assert.DoesNotContain(village.HomeWalls(), w => w == wall);
        Assert.True(sim.Snapshot.Space.Distance(yard.Position, village.Position) > village.HomeWalls().Select(w => w.Vertices.Max(v => sim.Snapshot.Space.Distance(v, village.Position))).DefaultIfEmpty(0).Max());
    }

    [Fact]
    public void Builders_take_Furniture_from_the_yard_and_gather_loose_pieces_into_it()
    {
        using var sim = Village(Personality.Cautious, shelves: 0);
        var loose = sim.Scenario.PlaceFurniture(FurnitureType.Chair, new(30, 100));
        var yard = sim.Scenario.PlanFurnitureYard(0);
        for (var i = 0; i < 6000; i++)
        {
            sim.AdvanceTicks(1);
            if (sim.Snapshot.Space.Distance(sim.Snapshot.Furniture.Single(p => p.Id == loose.Id).Position, yard.Position) <= FurnitureYard.Radius) break;
        }
        Assert.True(sim.Snapshot.Space.Distance(sim.Snapshot.Furniture.Single(p => p.Id == loose.Id).Position, yard.Position) <= FurnitureYard.Radius);
    }

    [Fact]
    public void Yard_pieces_are_built_into_a_Storehouse_before_pieces_lying_about()
    {
        using var sim = Village(Personality.Cautious, shelves: 0);
        var yard = sim.Scenario.PlanFurnitureYard(0);
        var near = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(23, 99));
        var kept = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, yard.Position + new Vector2(30, 0));
        var storedAway = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, yard.Position);
        for (var i = 0; i < 6000 && sim.Snapshot.Villages[0].Storehouse?.Slots.Any(s => s.FurnitureId is not null) != true; i++) sim.AdvanceTicks(1);
        var slots = sim.Snapshot.Villages[0].Storehouse!.Slots;
        Assert.Equal(storedAway.Id, slots.First(s => s.FurnitureId is not null).FurnitureId);
    }

    [Fact]
    public void Spoiling_food_Storehouse_and_Furniture_yard_survive_a_save_and_load()
    {
        using var original = WithStorehouse(Personality.Bold);
        var shelf = original.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(40, 112));
        original.Scenario.SetStores(0, GoodsKind.FreshFood, 80);
        original.Scenario.PutBack(shelf.Id, GoodsKind.FreshFood, 9);
        original.AdvanceTicks(3000);
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        var a = original.Snapshot.Villages[0];
        var b = loaded.Snapshot.Villages[0];
        Assert.Equal(a.Storehouse!.Slots.Select(s => s.FurnitureId), b.Storehouse!.Slots.Select(s => s.FurnitureId));
        Assert.Equal(a.FurnitureYard, b.FurnitureYard);
        Assert.Equal(a.SpoilProgress, b.SpoilProgress);
        Assert.Equal(a.Stores, b.Stores);
        original.AdvanceTicks(2 * Day);
        loaded.AdvanceTicks(2 * Day);
        Assert.Equal(original.Snapshot.Villages[0].Stores, loaded.Snapshot.Villages[0].Stores);
        Assert.Equal(original.Snapshot.Stock, loaded.Snapshot.Stock);
    }
}
