using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>Acceptance tests of ticket 08r: a shelf's look follows its stock amount in steps from full to bare.</summary>
public class ShelfFillTests
{
    private static Simulation Store() => Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));

    private static StockedGoods ShelfGoods(WorldSnapshot snapshot)
    {
        var pieces = snapshot.Furniture.ToDictionary(p => p.Id);
        return snapshot.Stock.First(g => !g.LeftBehind && pieces[g.FurnitureId].Type == FurnitureType.GroceryShelf);
    }

    [Fact]
    public void Every_stocked_shelf_shows_a_fill_step_between_low_and_full()
    {
        using var sim = Store();
        var snapshot = sim.Snapshot;
        var capacity = new StockConfig().ShelfCapacity;

        Assert.All(snapshot.Stock, g => Assert.InRange(snapshot.FillStep(g), 1, WorldSnapshot.FillSteps));
        Assert.Contains(snapshot.Stock, g => snapshot.FillStep(g) == WorldSnapshot.FillSteps);
        Assert.Contains(snapshot.Stock, g => snapshot.FillStep(g) < WorldSnapshot.FillSteps);
        Assert.All(snapshot.Stock.Where(g => g.Amount >= capacity), g => Assert.Equal(WorldSnapshot.FillSteps, snapshot.FillStep(g)));
    }

    [Fact]
    public void Setting_a_shelf_amount_steps_its_fill_from_full_to_bare_and_the_Sector_stock_follows()
    {
        using var sim = Store();
        var goods = ShelfGoods(sim.Snapshot);
        var capacity = new StockConfig().ShelfCapacity;
        var classOf = GoodsCatalog.ClassOf(goods.Kind);
        var before = sim.Snapshot.StockByClass()[classOf];
        var sectorsBefore = sim.Snapshot.SectorStocks.Sum(s => s.ByClass[classOf]);

        int StepAfter(int amount)
        {
            sim.Scenario.SetStock(goods.FurnitureId, goods.Kind, amount);
            var now = sim.Snapshot.Stock.Where(g => g.FurnitureId == goods.FurnitureId && g.Kind == goods.Kind).ToList();
            return now.Count == 0 ? 0 : sim.Snapshot.FillStep(Assert.Single(now));
        }

        Assert.Equal(3, StepAfter(capacity));
        Assert.Equal(2, StepAfter((capacity + 1) / 2));
        Assert.Equal(1, StepAfter(1));
        Assert.Equal(0, StepAfter(0));

        Assert.Equal(before - goods.Amount, sim.Snapshot.StockByClass()[classOf]);
        Assert.Equal(sectorsBefore - goods.Amount, sim.Snapshot.SectorStocks.Sum(s => s.ByClass[classOf]));

        Assert.Equal(WorldSnapshot.FillSteps, StepAfter(capacity * 2));
    }

    [Fact]
    public void Many_shelves_are_set_at_once_and_the_Sector_stock_follows()
    {
        using var sim = Store();
        var capacity = new StockConfig().ShelfCapacity;
        var before = sim.Snapshot.Stock.Where(g => !g.LeftBehind).Take(200).ToList();
        var goods = before.Select(g => (g.FurnitureId, g.Kind)).ToList();
        var sectorsBefore = sim.Snapshot.SectorStocks.Sum(s => s.ByClass.Values.Sum());

        sim.Scenario.SetStock(goods, capacity);

        var snapshot = sim.Snapshot;
        var set = goods.ToHashSet();
        var changed = snapshot.Stock.Where(g => !g.LeftBehind && set.Contains((g.FurnitureId, g.Kind))).ToList();
        Assert.Equal(goods.Count, changed.Count);
        Assert.All(changed, g => Assert.Equal(WorldSnapshot.FillSteps, snapshot.FillStep(g)));
        Assert.Equal(sectorsBefore + before.Sum(g => capacity - g.Amount), snapshot.SectorStocks.Sum(s => s.ByClass.Values.Sum()));
    }

    [Fact]
    public void Display_Furniture_cannot_be_stocked()
    {
        using var sim = Store();
        var snapshot = sim.Snapshot;
        var display = snapshot.Furniture.First(p => p.Type == FurnitureType.Fridge && !snapshot.IsStockFurniture(p));

        Assert.Throws<ArgumentException>(() => sim.Scenario.SetStock(display.Id, GoodsKind.BottledWater, 5));
        var shelf = ShelfGoods(snapshot);
        Assert.Throws<ArgumentException>(() => sim.Scenario.SetStock([(shelf.FurnitureId, shelf.Kind), (display.Id, GoodsKind.BottledWater)], 0));
        Assert.Same(snapshot.Stock, sim.Snapshot.Stock);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Scenario.SetStock(ShelfGoods(snapshot).FurnitureId, GoodsKind.TinnedFood, -1));
    }
}
