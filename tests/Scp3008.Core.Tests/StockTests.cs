using Scp3008.Core;
using Xunit.Abstractions;

namespace Scp3008.Core.Tests;

/// <summary>Acceptance tests of ticket 08o: Stock Furniture holds Goods in four Goods classes; Display Furniture is empty.</summary>
public class StockTests(ITestOutputHelper output)
{
    private static readonly int[] Seeds = [3008, 42, 7];

    private static WorldSnapshot Store(int seed) => TestStores.Get(seed);

    private static WorldSnapshot PointOfInterestShowcase(int seed, StockConfig? stock = null)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PointOfInterestShowcase: true, Stock: stock));
        return sim.Snapshot;
    }

    private static WorldSnapshot LandmarkShowcase(int seed)
    {
        return TestStores.Get(seed, landmarkShowcase: true);
    }

    private static Dictionary<int, FurniturePiece> PiecesById(WorldSnapshot snapshot) => snapshot.Furniture.ToDictionary(p => p.Id);

    private static DepartmentType DepartmentOf(WorldSnapshot snapshot, FurniturePiece piece) =>
        snapshot.Departments.All[snapshot.Displays[piece.DisplayId].DepartmentId].Type;

    // The store stock the ticket names: grocery shelves, freezers, warehouse shelving, Cafeteria counters, Aquarium tanks,
    // the Camping ground's stands, the Rack canyon's boxes, and the shelves of the Lighting, Kids' and Textiles departments.
    private static readonly HashSet<FurnitureType> StockTypes =
    [
        FurnitureType.GroceryShelf, FurnitureType.Freezer, FurnitureType.Fridge, FurnitureType.Counter, FurnitureType.PalletRack,
        FurnitureType.ShelfUnit, FurnitureType.ToyShelf, FurnitureType.CushionBin,
        FurnitureType.HotFoodCounter, FurnitureType.DessertCounter, FurnitureType.FishTank,
        FurnitureType.LabelledBox, FurnitureType.UnsortedCart, FurnitureType.FlatPackBox,
        FurnitureType.DiningTable,
    ];

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void Every_goods_entry_sits_on_a_piece_of_stock_furniture_except_the_rare_left_behind_finds(int seed)
    {
        var snapshot = Store(seed);
        var pieces = PiecesById(snapshot);

        Assert.NotEmpty(snapshot.Stock);
        Assert.All(snapshot.Stock, goods =>
        {
            Assert.True(goods.Amount > 0);
            var piece = pieces[goods.FurnitureId];
            if (goods.LeftBehind)
            {
                Assert.False(snapshot.IsStockFurniture(piece));
                Assert.True(piece.DisplayId >= 0 || GoodsCatalog.FoodTables.Contains(piece.Type), "Left-behind finds lie on Display Furniture or a table.");
            }
            else
            {
                Assert.True(snapshot.IsStockFurniture(piece), $"{piece.Type} {piece.Id} holds Goods but is not Stock Furniture.");
                Assert.Contains(piece.Type, StockTypes);
            }
        });
        // About the stocked share of the Display Stock Furniture holds Goods; the rest stands bare.
        var stocked = snapshot.Stock.Select(g => g.FurnitureId).ToHashSet();
        var displayStock = snapshot.Furniture.Where(p => p.DisplayId >= 0 && snapshot.IsStockFurniture(p)).ToList();
        var share = new StockConfig().StockedShelfShare;
        Assert.InRange(displayStock.Count(p => stocked.Contains(p.Id)) / (double)displayStock.Count, share * 0.8, share * 1.2);
        // Grocery shelves, freezers and warehouse shelving are stock in every Store.
        var stockTypes = snapshot.Stock.Where(g => !g.LeftBehind).Select(g => pieces[g.FurnitureId].Type).ToHashSet();
        Assert.Contains(FurnitureType.GroceryShelf, stockTypes);
        Assert.Contains(FurnitureType.Freezer, stockTypes);
        Assert.Contains(FurnitureType.PalletRack, stockTypes);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void No_food_lies_on_the_pallet_racks(int seed)
    {
        var snapshot = Store(seed);
        var pieces = PiecesById(snapshot);

        Assert.DoesNotContain(snapshot.Stock, goods =>
            pieces[goods.FurnitureId].Type == FurnitureType.PalletRack && GoodsCatalog.ClassOf(goods.Kind) == GoodsClass.Food);
    }

    [Fact]
    public void Almost_all_showroom_fridges_and_kitchen_cabinets_are_empty_and_left_behind_finds_stay_under_the_share()
    {
        var share = new StockConfig().LeftBehindShare;
        Assert.InRange(share, 0.001f, 0.05f);
        int showroomPieces = 0, holding = 0, leftBehind = 0;
        foreach (var seed in Seeds)
        {
            var snapshot = Store(seed);
            var held = snapshot.Stock.Select(g => g.FurnitureId).ToHashSet();
            var showroom = snapshot.Furniture
                // Every type a find can be left in, so the finds cannot crowd into a subset of them past the share.
                .Where(p => p.Type is FurnitureType.Fridge or FurnitureType.Freezer or FurnitureType.KitchenCabinet or FurnitureType.Sideboard
                    or FurnitureType.DisplayCabinet && p.DisplayId >= 0 && !snapshot.IsStockFurniture(p))
                .ToList();
            showroomPieces += showroom.Count;
            holding += showroom.Count(p => held.Contains(p.Id));
            leftBehind += snapshot.Stock.Count(g => g.LeftBehind);
            // Each find is a single, small thing.
            Assert.All(snapshot.Stock.Where(g => g.LeftBehind), g => Assert.Equal(1, g.Amount));
            // Kitchens and appliance displays are pure Display Furniture.
            Assert.DoesNotContain(snapshot.Furniture, p => p.DisplayId >= 0 && snapshot.IsStockFurniture(p) &&
                DepartmentOf(snapshot, p) == DepartmentType.BathroomsAndKitchens);
        }
        Assert.True(showroomPieces > 100, $"Only {showroomPieces} showroom fridges and kitchen cabinets in {Seeds.Length} Stores.");
        Assert.True(leftBehind > 0, "No left-behind finds at all.");
        Assert.True(holding <= share * showroomPieces, $"{holding} of {showroomPieces} showroom fridges and kitchen cabinets hold Goods.");
    }

    [Fact]
    public void The_left_behind_share_is_tunable()
    {
        foreach (var share in new[] { 0f, 0.2f })
        {
            using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, Stock: new StockConfig { LeftBehindShare = share }));
            var snapshot = sim.Snapshot;
            var candidates = snapshot.Furniture.Count(p => p.DisplayId >= 0 && !snapshot.IsStockFurniture(p) &&
                p.Type is FurnitureType.Fridge or FurnitureType.Freezer or FurnitureType.KitchenCabinet or FurnitureType.Sideboard or FurnitureType.DisplayCabinet);
            var pieces = PiecesById(snapshot);
            var finds = snapshot.Stock.Count(g => g.LeftBehind && !GoodsCatalog.FoodTables.Contains(pieces[g.FurnitureId].Type));
            Assert.True(finds <= share * candidates, $"{finds} finds among {candidates} pieces at share {share}.");
            if (share > 0f)
                Assert.True(finds >= share * candidates - 1, $"{finds} finds among {candidates} pieces at share {share}.");
            else
                Assert.Equal(0, finds);
        }
    }

    [Fact]
    public void Every_goods_kind_belongs_to_one_of_the_four_goods_classes()
    {
        Assert.Equal(4, Enum.GetValues<GoodsClass>().Length);
        foreach (var kind in Enum.GetValues<GoodsKind>())
            Assert.True(Enum.IsDefined(GoodsCatalog.ClassOf(kind)), $"{kind} has no Goods class.");
        Assert.Equal(GoodsClass.Food, GoodsCatalog.ClassOf(GoodsKind.FreshFood));
        Assert.Equal(GoodsClass.Food, GoodsCatalog.ClassOf(GoodsKind.TinnedFood));
        Assert.Equal(GoodsClass.Food, GoodsCatalog.ClassOf(GoodsKind.DryFood));
        Assert.Equal(GoodsClass.Drink, GoodsCatalog.ClassOf(GoodsKind.BottledWater));
        Assert.Equal(GoodsClass.Drink, GoodsCatalog.ClassOf(GoodsKind.SoftDrinks));
        Assert.Equal(GoodsClass.Drink, GoodsCatalog.ClassOf(GoodsKind.Coffee));
        Assert.Equal(GoodsClass.Comfort, GoodsCatalog.ClassOf(GoodsKind.Sweets));
        Assert.Equal(GoodsClass.Comfort, GoodsCatalog.ClassOf(GoodsKind.Candles));
        Assert.Equal(GoodsClass.Supplies, GoodsCatalog.ClassOf(GoodsKind.Batteries));
        Assert.Equal(GoodsClass.Supplies, GoodsCatalog.ClassOf(GoodsKind.Bulbs));
        Assert.Equal(GoodsClass.Supplies, GoodsCatalog.ClassOf(GoodsKind.Cables));
        Assert.Equal(GoodsClass.Supplies, GoodsCatalog.ClassOf(GoodsKind.Tape));
        // Fresh food spoils fast, tinned and dry food slowly.
        Assert.True(GoodsCatalog.SpoilsFast(GoodsKind.FreshFood));
        Assert.False(GoodsCatalog.SpoilsFast(GoodsKind.TinnedFood));
        Assert.False(GoodsCatalog.SpoilsFast(GoodsKind.DryFood));
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void Each_department_type_stocks_only_its_own_kinds(int seed)
    {
        var snapshot = Store(seed);
        var pieces = PiecesById(snapshot);
        var expected = new Dictionary<DepartmentType, HashSet<GoodsKind>>
        {
            [DepartmentType.RestaurantAndFoodMarket] =
            [
                GoodsKind.FreshFood, GoodsKind.TinnedFood, GoodsKind.DryFood, GoodsKind.BottledWater, GoodsKind.SoftDrinks, GoodsKind.Coffee,
            ],
            [DepartmentType.LightingAndElectrical] = [GoodsKind.Bulbs, GoodsKind.Batteries],
            [DepartmentType.SelfServeWarehouse] = [GoodsKind.Cables, GoodsKind.Tape],
            [DepartmentType.TextilesAndDecor] = [GoodsKind.Candles],
            [DepartmentType.KidsArea] = [GoodsKind.Sweets],
        };
        foreach (var department in Enum.GetValues<DepartmentType>())
            Assert.Equal(expected.GetValueOrDefault(department, []).OrderBy(k => k), GoodsCatalog.KindsOf(department).OrderBy(k => k));

        var seen = new Dictionary<DepartmentType, HashSet<GoodsKind>>();
        foreach (var goods in snapshot.Stock.Where(g => !g.LeftBehind && pieces[g.FurnitureId].DisplayId >= 0))
        {
            var department = DepartmentOf(snapshot, pieces[goods.FurnitureId]);
            Assert.Contains(goods.Kind, expected[department]);
            (seen.TryGetValue(department, out var kinds) ? kinds : seen[department] = []).Add(goods.Kind);
        }
        // The food market sells both food and drink; the warehouse shelving supplies.
        Assert.Contains(seen[DepartmentType.RestaurantAndFoodMarket], k => GoodsCatalog.ClassOf(k) == GoodsClass.Food);
        Assert.Contains(seen[DepartmentType.RestaurantAndFoodMarket], k => GoodsCatalog.ClassOf(k) == GoodsClass.Drink);
        Assert.Contains(seen[DepartmentType.SelfServeWarehouse], k => GoodsCatalog.ClassOf(k) == GoodsClass.Supplies);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void Each_lost_and_found_holds_a_few_goods(int seed)
    {
        foreach (var snapshot in new[] { Store(seed), PointOfInterestShowcase(seed) })
        {
            var pieces = PiecesById(snapshot);
            var config = new StockConfig();
            var lostAndFounds = snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.LostAndFound).ToList();
            Assert.NotEmpty(lostAndFounds);
            foreach (var laf in lostAndFounds)
            {
                var finds = snapshot.Stock.Where(g => pieces[g.FurnitureId].PointOfInterestId == laf.Id).ToList();
                Assert.InRange(finds.Count, config.LostAndFoundFindsMin, config.LostAndFoundFindsMax);
                Assert.All(finds, g => Assert.InRange(g.Amount, 1, config.LostAndFoundAmountMax));
            }
        }
    }

    [Fact]
    public void Lost_and_found_goods_are_random_and_of_any_class()
    {
        var classes = new HashSet<GoodsClass>();
        foreach (var seed in Enumerable.Range(1, 30))
        {
            var snapshot = PointOfInterestShowcase(seed);
            var pieces = PiecesById(snapshot);
            var laf = snapshot.PointsOfInterest.Single(p => p.Type == PointOfInterestType.LostAndFound);
            classes.UnionWith(snapshot.Stock.Where(g => pieces[g.FurnitureId].PointOfInterestId == laf.Id).Select(g => GoodsCatalog.ClassOf(g.Kind)));
        }
        Assert.Equal(Enum.GetValues<GoodsClass>().ToHashSet(), classes);
    }

    [Fact]
    public void The_same_seed_gives_the_same_stock()
    {
        using var again = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 99));
        Assert.Equal(Store(3008).Stock, again.Snapshot.Stock);
        Assert.NotEqual(Store(3008).Stock, Store(42).Stock);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void Cafeteria_meals_aquarium_fish_and_camping_goods_are_in_the_same_stock_listing(int seed)
    {
        var showcase = PointOfInterestShowcase(seed);
        var stock = showcase.Stock.ToDictionary(g => g.FurnitureId);
        Assert.NotEmpty(showcase.TankFish);
        Assert.All(showcase.TankFish, fish => Assert.Equal(GoodsKind.LiveFish, stock[fish.TankId].Kind));
        Assert.NotEmpty(showcase.CounterMeals);
        Assert.All(showcase.CounterMeals, meal => Assert.Equal(
            meal.Kind == MealKind.Meatballs ? GoodsKind.Meatballs : GoodsKind.AlmondCake, stock[meal.CounterId].Kind));

        var landmarks = LandmarkShowcase(seed);
        var camp = landmarks.Stock.Where(g => landmarks.CampGoods.Any(c => c.StandId == g.FurnitureId)).ToList();
        Assert.Contains(camp, g => g.Kind == GoodsKind.Marshmallows);
        Assert.Contains(camp, g => g.Kind == GoodsKind.TinnedFood);
        // Every camping stand is Stock Furniture; the backpacks are Gear, not Goods, and stay out of the stock listing.
        var pieces = PiecesById(landmarks);
        Assert.All(landmarks.CampGoods, c => Assert.True(landmarks.IsStockFurniture(pieces[c.StandId])));
        Assert.All(landmarks.Stock, g => Assert.True(Enum.IsDefined(GoodsCatalog.ClassOf(g.Kind))));
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void Some_rack_canyon_boxes_hold_goods_of_any_kind_but_food_twice_a_shelfs_amount(int seed)
    {
        var landmarks = LandmarkShowcase(seed);
        var canyon = landmarks.Landmarks.Single(l => l.Type == LandmarkType.RackCanyon).Id;
        var boxes = landmarks.Furniture.Where(p => p.LandmarkId == canyon && p.Type == FurnitureType.FlatPackBox).ToList();
        // Only the upright boxes, which Goods can stand on.
        Assert.All(boxes, box => Assert.Equal(box.Tilt == 0f && box.Roll == 0f, landmarks.IsStockFurniture(box)));
        boxes = boxes.Where(landmarks.IsStockFurniture).ToList();
        var ids = boxes.Select(b => b.Id).ToHashSet();
        var stocked = landmarks.Stock.Where(g => ids.Contains(g.FurnitureId)).ToList();
        var config = new StockConfig();
        Assert.InRange(stocked.Count / (double)boxes.Count, config.StockedShelfShare * 0.5, config.StockedShelfShare * 1.5);
        Assert.All(stocked, g => Assert.InRange(g.Amount, config.CanyonBoxAmountMin, config.CanyonBoxAmountMax));
        Assert.True(stocked.Select(g => GoodsCatalog.ClassOf(g.Kind)).Distinct().Count() > 1, "The canyon's boxes hold only one class of Goods.");
        Assert.DoesNotContain(stocked, g => GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food);
    }

    [Fact]
    public void About_one_table_in_a_hundred_has_a_unit_of_shop_food_left_on_it()
    {
        int tables = 0, withFood = 0;
        foreach (var seed in Seeds)
        {
            var snapshot = Store(seed);
            var pieces = PiecesById(snapshot);
            tables += snapshot.Furniture.Count(p => GoodsCatalog.FoodTables.Contains(p.Type) && !snapshot.IsStockFurniture(p));
            var onTables = snapshot.Stock.Where(g => GoodsCatalog.FoodTables.Contains(pieces[g.FurnitureId].Type) && !snapshot.IsStockFurniture(pieces[g.FurnitureId])).ToList();
            Assert.All(onTables, g =>
            {
                Assert.True(g.LeftBehind);
                Assert.Equal(1, g.Amount);
                Assert.Equal(GoodsClass.Food, GoodsCatalog.ClassOf(g.Kind));
                Assert.Contains(g.Kind, GoodsCatalog.ShopKinds);
            });
            withFood += onTables.Count;
        }
        var chance = new StockConfig().TableFoodChance;
        Assert.InRange(withFood / (double)tables, chance * 0.7, chance * 1.3);

        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, Stock: new StockConfig { TableFoodChance = 0f }));
        var bare = PiecesById(sim.Snapshot);
        Assert.DoesNotContain(sim.Snapshot.Stock, g => GoodsCatalog.FoodTables.Contains(bare[g.FurnitureId].Type) && !sim.Snapshot.IsStockFurniture(bare[g.FurnitureId]));
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void The_marshmallow_stand_holds_three_times_another_stands_units(int seed)
    {
        var config = new StockConfig();
        Assert.Equal(3 * config.CampStandAmountMin, config.MarshmallowAmountMin);
        Assert.Equal(3 * config.CampStandAmountMax, config.MarshmallowAmountMax);
        var landmarks = LandmarkShowcase(seed);
        var stands = landmarks.CampGoods.Select(c => c.StandId).ToHashSet();
        var camp = landmarks.Stock.Where(g => stands.Contains(g.FurnitureId)).ToList();
        Assert.All(camp.Where(g => g.Kind == GoodsKind.Marshmallows), g => Assert.InRange(g.Amount, config.MarshmallowAmountMin, config.MarshmallowAmountMax));
        Assert.All(camp.Where(g => g.Kind != GoodsKind.Marshmallows), g => Assert.InRange(g.Amount, config.CampStandAmountMin, config.CampStandAmountMax));
    }

    [Fact]
    public void Stock_amounts_come_from_the_configuration()
    {
        // Without the Sector guarantee (ticket 08q), which reshapes the Display stock's amounts.
        var config = new StockConfig
        {
            ShelfAmountMin = 3, ShelfAmountMax = 3, FishMin = 2, FishMax = 2, MealPortionsMin = 9, MealPortionsMax = 9, SectorGuarantee = false,
        };
        using var sim = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 1, Stock: config));
        var snapshot = sim.Snapshot;
        var pieces = PiecesById(snapshot);
        Assert.All(snapshot.Stock.Where(g => !g.LeftBehind && pieces[g.FurnitureId].DisplayId >= 0), g => Assert.Equal(3, g.Amount));

        var showcase = PointOfInterestShowcase(42, config);
        Assert.All(showcase.TankFish, f => Assert.Equal(2, showcase.Stock.Single(g => g.FurnitureId == f.TankId).Amount));
        Assert.All(showcase.CounterMeals, m => Assert.Equal(9, showcase.Stock.Single(g => g.FurnitureId == m.CounterId).Amount));
    }

    [Fact]
    public void Prints_the_stock_count_per_goods_class()
    {
        foreach (var seed in Seeds)
        {
            var snapshot = Store(seed);
            var totals = snapshot.StockByClass();
            Assert.Equal(Enum.GetValues<GoodsClass>().ToHashSet(), totals.Keys.ToHashSet());
            Assert.All(totals.Values, total => Assert.True(total > 0));
            var pieces = snapshot.Stock.Select(g => g.FurnitureId).Distinct().Count();
            output.WriteLine($"Seed {seed}: {pieces} stocked pieces, " +
                string.Join(", ", totals.Select(t => $"{t.Key} {t.Value}")) +
                $"; {snapshot.Stock.Count(g => g.LeftBehind)} left-behind finds");
        }
    }
}
