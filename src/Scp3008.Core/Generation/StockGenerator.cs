namespace Scp3008.Core.Generation;

/// <summary>
/// Gives Goods to the Store's Stock Furniture as real store stock, one kind and amount per piece: the Display stock by its
/// Department type, the Cafeteria meals, Aquarium fish and Camping ground goods the Points of interest and Landmarks set, a few
/// random finds in each Lost and found and, rarely, a thing left behind in Display Furniture.
/// </summary>
internal static class StockGenerator
{
    private const int StockStream = 8;

    // Display Furniture a shopper could leave something in: the showroom fridges, kitchen cabinets and pantry-like cabinets.
    private static readonly HashSet<FurnitureType> LeftBehindTypes =
    [
        FurnitureType.Fridge, FurnitureType.Freezer, FurnitureType.KitchenCabinet, FurnitureType.Sideboard, FurnitureType.DisplayCabinet,
    ];

    /// <summary>Whether a piece is Stock Furniture: whether it holds Goods as store stock, whether or not any are on it now.</summary>
    internal static bool IsStockFurniture(FurniturePiece piece, IReadOnlyList<Display> displays, DepartmentMap departments,
        IReadOnlyList<PointOfInterest> pointsOfInterest, IReadOnlyList<CampGoods> campGoods)
    {
        if (piece.DisplayId >= 0)
        {
            var display = displays[piece.DisplayId];
            return GoodsCatalog.KindsOn(departments.All[display.DepartmentId].Type, display.Theme, piece.Type).Count > 0;
        }
        if (piece.PointOfInterestId >= 0)
            return pointsOfInterest[piece.PointOfInterestId].Type switch
            {
                PointOfInterestType.AquariumAisle => piece.Type == FurnitureType.FishTank,
                PointOfInterestType.Cafeteria => piece.Type is FurnitureType.HotFoodCounter or FurnitureType.DessertCounter,
                PointOfInterestType.LostAndFound => piece.Type is FurnitureType.LabelledBox or FurnitureType.UnsortedCart,
                _ => false,
            };
        if (piece.LandmarkId >= 0)
            foreach (var goods in campGoods)
                if (goods.StandId == piece.Id)
                    return true;
        return false;
    }

    public static IReadOnlyList<StockedGoods> Generate(int seed, StockConfig config, IReadOnlyList<FurniturePiece> pieces,
        IReadOnlyList<Display> displays, DepartmentMap departments, IReadOnlyList<PointOfInterest> pointsOfInterest,
        IReadOnlyList<TankFish> tankFish, IReadOnlyList<CounterMeal> counterMeals, IReadOnlyList<CampGoods> campGoods)
    {
        var random = SeededRandom.For(seed, StockStream);
        int Amount(int min, int max) => min + random.Next(Math.Max(max - min, 0) + 1);
        var stock = new List<StockedGoods>();
        var leftBehindCandidates = new List<int>();
        var lostAndFound = new Dictionary<int, List<int>>();

        foreach (var piece in pieces)
        {
            if (piece.DisplayId >= 0)
            {
                var display = displays[piece.DisplayId];
                var kinds = GoodsCatalog.KindsOn(departments.All[display.DepartmentId].Type, display.Theme, piece.Type);
                if (kinds.Count > 0)
                    stock.Add(new StockedGoods(piece.Id, random.Pick(kinds), Amount(config.ShelfAmountMin, config.ShelfAmountMax)));
                else if (LeftBehindTypes.Contains(piece.Type))
                    leftBehindCandidates.Add(piece.Id);
            }
            else if (piece.PointOfInterestId >= 0 && pointsOfInterest[piece.PointOfInterestId].Type == PointOfInterestType.LostAndFound &&
                     piece.Type is FurnitureType.LabelledBox or FurnitureType.UnsortedCart)
            {
                if (!lostAndFound.TryGetValue(piece.PointOfInterestId, out var holders))
                    lostAndFound[piece.PointOfInterestId] = holders = [];
                holders.Add(piece.Id);
            }
        }

        foreach (var fish in tankFish)
            stock.Add(new StockedGoods(fish.TankId, GoodsKind.LiveFish, Amount(config.FishMin, config.FishMax)));
        foreach (var meal in counterMeals)
            stock.Add(new StockedGoods(meal.CounterId, meal.Kind == MealKind.Meatballs ? GoodsKind.Meatballs : GoodsKind.AlmondCake,
                Amount(config.MealPortionsMin, config.MealPortionsMax)));
        // The backpacks of the supplies stand are Gear, not Goods, so they are not store stock.
        foreach (var goods in campGoods)
            if (goods.Kind != CampGoodsKind.Backpack)
                stock.Add(new StockedGoods(goods.StandId, goods.Kind == CampGoodsKind.Marshmallows ? GoodsKind.Marshmallows : GoodsKind.TinnedFood,
                    Amount(config.CampStandAmountMin, config.CampStandAmountMax)));

        // Each Lost and found: a few rare, random Goods of any class, each on its own labelled box or cart.
        foreach (var holders in lostAndFound.OrderBy(e => e.Key).Select(e => e.Value))
        {
            var finds = Math.Min(Amount(config.LostAndFoundFindsMin, config.LostAndFoundFindsMax), holders.Count);
            foreach (var id in PickDistinct(random, holders, finds))
                stock.Add(new StockedGoods(id, random.Pick(GoodsCatalog.ShopKinds), Amount(1, config.LostAndFoundAmountMax)));
        }

        // The rare things left behind in Display Furniture: a fixed share of the candidates, so it never exceeds the share.
        var leftBehind = (int)MathF.Floor(Math.Clamp(config.LeftBehindShare, 0f, 1f) * leftBehindCandidates.Count);
        foreach (var id in PickDistinct(random, leftBehindCandidates, leftBehind))
            stock.Add(new StockedGoods(id, random.Pick(GoodsCatalog.ShopKinds), 1, LeftBehind: true));

        return stock.AsReadOnly();
    }

    // A partial Fisher-Yates shuffle of a copy: count distinct items, in random order.
    private static IEnumerable<int> PickDistinct(SeededRandom random, IReadOnlyList<int> items, int count)
    {
        var pool = items.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = i + random.Next(pool.Length - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            yield return pool[i];
        }
    }
}
