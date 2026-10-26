using Scp3008.Core;
using Xunit.Abstractions;

namespace Scp3008.Core.Tests;

/// <summary>Acceptance tests of ticket 08q: no Sector has everything; each has a Surplus and a Shortage Goods class.</summary>
public class SectorStockTests(ITestOutputHelper output)
{
    private static readonly int[] Seeds = [3008, 42, 7];

    public static IEnumerable<object[]> LayoutsAndSeeds =>
        PillarLayouts.All.SelectMany(layout => Seeds.Select(seed => new object[] { layout, seed }));

    private static WorldSnapshot Store(PillarLayout layout, int seed) => TestStores.Get(seed, layout);

    private static float Hectares(WorldSnapshot snapshot) => snapshot.Space.Size * snapshot.Space.Size / 10_000f;

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Every_sector_has_a_surplus_and_a_shortage_and_none_has_everything(PillarLayout layout, int seed)
    {
        var snapshot = Store(layout, seed);

        Assert.Equal(snapshot.Sectors.Select(s => s.Id), snapshot.SectorStocks.Select(s => s.SectorId));
        Assert.All(snapshot.SectorStocks, sector =>
        {
            var name = snapshot.Sectors[sector.SectorId].Name;
            Assert.True(sector.Surplus.Count >= 1, $"{name} has no Surplus.");
            Assert.True(sector.Shortage.Count >= 1, $"{name} has no Shortage.");
            Assert.True(sector.Surplus.Count < 4, $"{name} has a Surplus in every Goods class.");
            Assert.Empty(sector.Surplus.Intersect(sector.Shortage));
        });
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Sector_stock_is_derived_from_the_goods_actually_on_its_furniture(PillarLayout layout, int seed)
    {
        var snapshot = Store(layout, seed);
        var config = new StockConfig();
        var classes = Enum.GetValues<GoodsClass>();
        var expected = snapshot.Sectors.ToDictionary(s => s.Id, _ => classes.ToDictionary(c => c, _ => 0));
        foreach (var goods in snapshot.Stock)
            expected[snapshot.SectorAt(snapshot.Furniture[goods.FurnitureId].Position).Id][GoodsCatalog.ClassOf(goods.Kind)] += goods.Amount;

        var totals = snapshot.StockByClass();
        foreach (var sector in snapshot.SectorStocks)
        {
            Assert.Equal(expected[sector.SectorId], sector.ByClass);
            foreach (var c in classes)
            {
                // Against the Store's mean per Sector of the class.
                var ratio = sector.ByClass[c] * (double)snapshot.Sectors.Count / totals[c];
                Assert.Equal(ratio >= config.SurplusRatio, sector.Surplus.Contains(c));
                Assert.Equal(ratio <= config.ShortageRatio, sector.Shortage.Contains(c));
            }
        }
        // A Shortage means scarce, not absent: every Display shelf keeps some stock.
        Assert.All(snapshot.Stock, g => Assert.True(g.Amount > 0));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_goods_class_store_total_stays_within_its_band_of_the_target(PillarLayout layout, int seed)
    {
        var snapshot = Store(layout, seed);
        var config = new StockConfig();
        foreach (var (goodsClass, total) in snapshot.StockByClass())
        {
            var target = config.TargetPerHectare[goodsClass] * Hectares(snapshot);
            Assert.InRange(total, target * (1 - config.ClassTotalBand), target * (1 + config.ClassTotalBand));
        }
    }

    [Fact]
    public void The_target_and_band_are_tunable()
    {
        var config = new StockConfig
        {
            TargetPerHectare = new Dictionary<GoodsClass, float>
            {
                [GoodsClass.Food] = 600f, [GoodsClass.Drink] = 300f, [GoodsClass.Comfort] = 2000f, [GoodsClass.Supplies] = 1470f,
            },
            ClassTotalBand = 0.05f,
        };
        using var sim = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 1, Stock: config));
        var snapshot = sim.Snapshot;
        foreach (var (goodsClass, total) in snapshot.StockByClass())
        {
            var target = config.TargetPerHectare[goodsClass] * Hectares(snapshot);
            Assert.InRange(total, target * 0.95f, target * 1.05f);
        }
        Assert.All(snapshot.SectorStocks, s => Assert.True(s.Surplus.Count >= 1 && s.Shortage.Count >= 1));
    }

    [Fact]
    public void The_same_seed_gives_the_same_surpluses_and_shortages()
    {
        using var again = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 99));
        var first = Store(PillarLayout.ThreeByThree, 3008).SectorStocks;
        var second = again.Snapshot.SectorStocks;
        Assert.Equal(first.Select(s => s.Surplus), second.Select(s => s.Surplus));
        Assert.Equal(first.Select(s => s.Shortage), second.Select(s => s.Shortage));
        Assert.Equal(first.Select(s => s.ByClass), second.Select(s => s.ByClass));
        Assert.NotEqual(first.Select(s => s.ByClass), Store(PillarLayout.ThreeByThree, 42).SectorStocks.Select(s => s.ByClass));
    }

    [Fact]
    public void Prints_each_sectors_surplus_and_shortage()
    {
        foreach (var layout in PillarLayouts.All)
        {
            var snapshot = Store(layout, 3008);
            output.WriteLine($"{layout} 3008: " + string.Join(", ", snapshot.StockByClass().Select(t => $"{t.Key} {t.Value / Hectares(snapshot):0}/ha")));
            foreach (var sector in snapshot.SectorStocks)
                output.WriteLine($"  {snapshot.Sectors[sector.SectorId].Name}: " + string.Join(", ", sector.ByClass.Select(t => $"{t.Key} {t.Value}")) +
                    $"  Surplus [{string.Join(", ", sector.Surplus)}]  Shortage [{string.Join(", ", sector.Shortage)}]");
        }
    }
}
