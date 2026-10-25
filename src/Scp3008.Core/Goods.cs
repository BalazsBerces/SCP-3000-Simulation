namespace Scp3008.Core;

/// <summary>The four Goods classes. There are no medicines in the Store.</summary>
public enum GoodsClass
{
    /// <summary>Food: fresh food spoils fast, tinned and dry food slowly.</summary>
    Food,
    /// <summary>Drink: bottled water, soft drinks, coffee.</summary>
    Drink,
    /// <summary>Comfort goods: they keep no one alive but raise morale.</summary>
    Comfort,
    /// <summary>Supplies: batteries, bulbs, cables, tape.</summary>
    Supplies,
}

/// <summary>A kind of Goods; each belongs to one <see cref="GoodsClass"/> (<see cref="GoodsCatalog.ClassOf"/>).</summary>
public enum GoodsKind
{
    // Food
    FreshFood,
    TinnedFood,
    DryFood,
    /// <summary>The live fish of an Aquarium aisle's tank (which kind of fish is in <see cref="TankFish"/>).</summary>
    LiveFish,
    /// <summary>Meatballs with mash and lingonberry, on a Cafeteria's hot food counter.</summary>
    Meatballs,
    /// <summary>Almond cake, on a Cafeteria's chilled dessert counter.</summary>
    AlmondCake,
    /// <summary>Marshmallows on sticks, the Camping ground's own.</summary>
    Marshmallows,
    // Drink
    BottledWater,
    SoftDrinks,
    Coffee,
    // Comfort goods
    Sweets,
    Candles,
    // Supplies
    Batteries,
    Bulbs,
    Cables,
    Tape,
}

/// <summary>
/// Goods on one piece of Furniture: a kind and an amount. Store stock lies on Stock Furniture
/// (<see cref="WorldSnapshot.IsStockFurniture"/>); a <paramref name="LeftBehind"/> find is the rare thing on Display Furniture.
/// </summary>
/// <param name="FurnitureId">The piece holding the Goods, by <see cref="FurniturePiece.Id"/>.</param>
/// <param name="Kind">What the Goods are.</param>
/// <param name="Amount">How many units of them, at least one.</param>
/// <param name="LeftBehind">A rare thing left behind in Display Furniture (a showroom fridge or kitchen cabinet), not store stock.</param>
public readonly record struct StockedGoods(int FurnitureId, GoodsKind Kind, int Amount, bool LeftBehind = false);

/// <summary>
/// How much Goods the Store generator puts where. Amounts are units per piece, drawn evenly from each range (both ends included).
/// </summary>
public sealed record StockConfig
{
    /// <summary>Units on each piece of Display Stock Furniture (grocery shelves, freezers, warehouse shelving and the like).</summary>
    public int ShelfAmountMin { get; init; } = 1;
    /// <inheritdoc cref="ShelfAmountMin"/>
    public int ShelfAmountMax { get; init; } = 3;
    /// <summary>The share of Display Stock Furniture that holds Goods; the rest stands bare until something stocks it.</summary>
    public float StockedShelfShare { get; init; } = 0.2f;
    /// <summary>The units at which a shelf of Stock Furniture looks full (<see cref="WorldSnapshot.FillStep"/>).</summary>
    public int ShelfCapacity { get; init; } = 3;
    /// <summary>Portions on each Cafeteria serving counter.</summary>
    public int MealPortionsMin { get; init; } = 20;
    /// <inheritdoc cref="MealPortionsMin"/>
    public int MealPortionsMax { get; init; } = 40;
    /// <summary>Live fish in each Aquarium tank.</summary>
    public int FishMin { get; init; } = 4;
    /// <inheritdoc cref="FishMin"/>
    public int FishMax { get; init; } = 14;
    /// <summary>Units on each Camping ground stand.</summary>
    public int CampStandAmountMin { get; init; } = 10;
    /// <inheritdoc cref="CampStandAmountMin"/>
    public int CampStandAmountMax { get; init; } = 24;
    /// <summary>
    /// Units on each stocked flat-pack box of the Rack canyon, twice a shelf's; a box is stocked as often as a shelf is
    /// (<see cref="StockedShelfShare"/>), with any kind of shop Goods of all classes but one, in the mix of <see cref="TargetPerHectare"/>.
    /// </summary>
    public int CanyonBoxAmountMin { get; init; } = 2;
    /// <inheritdoc cref="CanyonBoxAmountMin"/>
    public int CanyonBoxAmountMax { get; init; } = 6;
    /// <summary>How many finds each Lost and found holds, each on its own labelled box or cart.</summary>
    public int LostAndFoundFindsMin { get; init; } = 2;
    /// <inheritdoc cref="LostAndFoundFindsMin"/>
    public int LostAndFoundFindsMax { get; init; } = 4;
    /// <summary>The most units of one Lost and found find (at least one).</summary>
    public int LostAndFoundAmountMax { get; init; } = 2;
    /// <summary>
    /// The share of storage-like Display Furniture (fridges, freezers, kitchen cabinets, sideboards, display cabinets that are not Stock Furniture)
    /// that holds one left-behind find; the number of finds is this share of them, rounded down.
    /// </summary>
    public float LeftBehindShare { get; init; } = 0.004f;

    /// <summary>
    /// Whether the generator reshapes the Display stock's amounts so every Sector has a Surplus and a Shortage and each Goods
    /// class's Store total keeps to its band; off, every shelf keeps the amount drawn for it.
    /// </summary>
    public bool SectorGuarantee { get; init; } = true;
    /// <summary>A Sector has a Surplus in a Goods class holding at least this many times the Store's mean per Sector of it.</summary>
    public float SurplusRatio { get; init; } = 1.3f;
    /// <summary>A Sector has a Shortage in a Goods class holding at most this share of the Store's mean per Sector of it (scarce, not absent).</summary>
    public float ShortageRatio { get; init; } = 0.6f;
    /// <summary>
    /// The target Store-wide units of each Goods class per hectare of floor; the defaults are about what the Departments' shelves
    /// hold on average across seeds and layouts (a fifth of them stocked, a few units each).
    /// </summary>
    public IReadOnlyDictionary<GoodsClass, float> TargetPerHectare { get; init; } = new Dictionary<GoodsClass, float>
    {
        [GoodsClass.Food] = 33.5f, [GoodsClass.Drink] = 2.6f, [GoodsClass.Comfort] = 21f, [GoodsClass.Supplies] = 32.5f,
    };
    /// <summary>
    /// How far, as a share of its target, each Goods class's Store total may stray from <see cref="TargetPerHectare"/>; a Store
    /// whose shelves hold more or less is brought to the band's edge.
    /// </summary>
    public float ClassTotalBand { get; init; } = 0.2f;
}

/// <summary>
/// A Sector's store stock by Goods class and what it makes the Sector rich in or short of, derived from the Goods actually on
/// its Furniture, measured against the Store's mean per Sector of each class.
/// </summary>
/// <param name="SectorId">The Sector, by <see cref="Sector.Id"/>.</param>
/// <param name="ByClass">The units of Goods in the Sector per Goods class, every class listed.</param>
/// <param name="Surplus">The classes it is rich in (<see cref="StockConfig.SurplusRatio"/>), in class order.</param>
/// <param name="Shortage">The classes it lacks (<see cref="StockConfig.ShortageRatio"/>), in class order.</param>
public sealed record SectorStock(int SectorId, IReadOnlyDictionary<GoodsClass, int> ByClass, IReadOnlyList<GoodsClass> Surplus,
    IReadOnlyList<GoodsClass> Shortage);

/// <summary>What each Goods kind is, and which Furniture of which Department holds which kinds as store stock.</summary>
public static class GoodsCatalog
{
    /// <summary>The ordinary shop kinds, which Lost and founds and left-behind finds draw from: every kind but those of one Point of interest or Landmark.</summary>
    public static IReadOnlyList<GoodsKind> ShopKinds { get; } = Enum.GetValues<GoodsKind>()
        .Where(k => k is not (GoodsKind.LiveFish or GoodsKind.Meatballs or GoodsKind.AlmondCake or GoodsKind.Marshmallows)).ToArray();

    // Which Display Furniture is Stock Furniture, and the kinds it may hold: by its Department type, its Display's theme and its type.
    private static readonly Dictionary<(DepartmentType, Theme, FurnitureType), GoodsKind[]> DisplayStock = BuildDisplayStock();

    private static readonly Dictionary<DepartmentType, IReadOnlySet<GoodsKind>> OwnKinds = Enum.GetValues<DepartmentType>().ToDictionary(
        d => d, d => (IReadOnlySet<GoodsKind>)DisplayStock.Where(e => e.Key.Item1 == d).SelectMany(e => e.Value).ToHashSet());

    /// <summary>The Goods class a kind belongs to.</summary>
    public static GoodsClass ClassOf(GoodsKind kind) => kind switch
    {
        GoodsKind.FreshFood or GoodsKind.TinnedFood or GoodsKind.DryFood or GoodsKind.LiveFish or GoodsKind.Meatballs or GoodsKind.AlmondCake
            or GoodsKind.Marshmallows => GoodsClass.Food,
        GoodsKind.BottledWater or GoodsKind.SoftDrinks or GoodsKind.Coffee => GoodsClass.Drink,
        GoodsKind.Sweets or GoodsKind.Candles => GoodsClass.Comfort,
        GoodsKind.Batteries or GoodsKind.Bulbs or GoodsKind.Cables or GoodsKind.Tape => GoodsClass.Supplies,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whether food of this kind spoils fast once taken out of Limbo (fresh food, fish, meals); tinned and dry food spoil slowly.</summary>
    public static bool SpoilsFast(GoodsKind kind) =>
        kind is GoodsKind.FreshFood or GoodsKind.LiveFish or GoodsKind.Meatballs or GoodsKind.AlmondCake;

    /// <summary>The kinds a Department type stocks on its own Stock Furniture; empty for one without store stock.</summary>
    public static IReadOnlySet<GoodsKind> KindsOf(DepartmentType department) => OwnKinds[department];

    /// <summary>
    /// The kinds a piece of Display Furniture of this type may hold as store stock, standing in a Display of this theme in a
    /// Department of this type; empty if it is not Stock Furniture there.
    /// </summary>
    public static IReadOnlyList<GoodsKind> KindsOn(DepartmentType department, Theme theme, FurnitureType type) =>
        DisplayStock.TryGetValue((department, theme, type), out var kinds) ? kinds : [];

    private static Dictionary<(DepartmentType, Theme, FurnitureType), GoodsKind[]> BuildDisplayStock()
    {
        var stock = new Dictionary<(DepartmentType, Theme, FurnitureType), GoodsKind[]>();
        void Add(DepartmentType department, Theme theme, FurnitureType[] types, params GoodsKind[] kinds)
        {
            foreach (var type in types)
                stock[(department, theme, type)] = kinds;
        }
        FurnitureType[] shelvingRows = [FurnitureType.ShelfUnit, FurnitureType.ToyShelf, FurnitureType.GroceryShelf];

        // Restaurant/food market: food and drink.
        Add(DepartmentType.RestaurantAndFoodMarket, Theme.FoodMarket, [FurnitureType.GroceryShelf],
            GoodsKind.TinnedFood, GoodsKind.DryFood, GoodsKind.BottledWater, GoodsKind.SoftDrinks, GoodsKind.Coffee);
        Add(DepartmentType.RestaurantAndFoodMarket, Theme.FoodMarket, [FurnitureType.Freezer], GoodsKind.FreshFood);
        Add(DepartmentType.RestaurantAndFoodMarket, Theme.FoodMarket, [FurnitureType.Fridge], GoodsKind.FreshFood, GoodsKind.BottledWater, GoodsKind.SoftDrinks);
        Add(DepartmentType.RestaurantAndFoodMarket, Theme.FoodMarket, [FurnitureType.Counter], GoodsKind.FreshFood);
        Add(DepartmentType.RestaurantAndFoodMarket, Theme.Cafe, [FurnitureType.Counter], GoodsKind.Coffee, GoodsKind.SoftDrinks);
        // Lighting & electrical: bulbs and batteries, on the shelving rows and the lamp displays' wall shelves.
        Add(DepartmentType.LightingAndElectrical, Theme.ShelvingRows, shelvingRows, GoodsKind.Bulbs, GoodsKind.Batteries);
        Add(DepartmentType.LightingAndElectrical, Theme.LampDisplay, [FurnitureType.ShelfUnit], GoodsKind.Bulbs, GoodsKind.Batteries);
        // Self-serve warehouse: tinned and dry food, cables and tape on the pallet racks.
        Add(DepartmentType.SelfServeWarehouse, Theme.WarehouseRacks, [FurnitureType.PalletRack],
            GoodsKind.TinnedFood, GoodsKind.DryFood, GoodsKind.Cables, GoodsKind.Tape);
        // Textiles & Decor: candles in the boutique's bins.
        Add(DepartmentType.TextilesAndDecor, Theme.TextileBoutique, [FurnitureType.CushionBin], GoodsKind.Candles);
        // Kids' area: sweets on the shelving rows.
        Add(DepartmentType.KidsArea, Theme.ShelvingRows, shelvingRows, GoodsKind.Sweets);
        return stock;
    }
}
