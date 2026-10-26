namespace Scp3008.Core.Generation;

/// <summary>
/// Gives Goods to the Store's Stock Furniture as real store stock, one kind and amount per piece: the Display stock by its
/// Department type, the Cafeteria meals, Aquarium fish and Camping ground goods the Points of interest and Landmarks set, any
/// non-Food kind on some of the Rack canyon's boxes, a few random finds in each Lost and found and, rarely, a thing left behind in Display Furniture
/// or a bit of food on a table. It then reshapes the amounts of the Display stock, the Cafeteria meals and the Aquarium fish so every Sector is rich in at least one Goods class (its Surplus) and short of at least one (its Shortage), and each
/// class's Store total keeps to a band around its target.
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
        IReadOnlyList<PointOfInterest> pointsOfInterest, IReadOnlyList<CampGoods> campGoods, IReadOnlyList<Landmark> landmarks)
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
                PointOfInterestType.SetDinnerTable => piece.Type == FurnitureType.DiningTable,
                PointOfInterestType.LostAndFound => piece.Type is FurnitureType.LabelledBox or FurnitureType.UnsortedCart,
                _ => false,
            };
        if (IsCanyonBox(piece, landmarks))
            return true;
        if (piece.LandmarkId >= 0)
            foreach (var goods in campGoods)
                if (goods.StandId == piece.Id)
                    return true;
        return false;
    }

    // An upright flat-pack box of the Rack canyon, on its racks or spilled from them: Goods of any kind can stand on top of it.
    private static bool IsCanyonBox(FurniturePiece piece, IReadOnlyList<Landmark> landmarks) =>
        piece.Type == FurnitureType.FlatPackBox && piece.Tilt == 0f && piece.Roll == 0f && piece.LandmarkId >= 0 && piece.LandmarkId < landmarks.Count &&
        landmarks[piece.LandmarkId].Type == LandmarkType.RackCanyon;

    public static IReadOnlyList<StockedGoods> Generate(int seed, StockConfig config, IReadOnlyList<FurniturePiece> pieces,
        IReadOnlyList<Display> displays, DepartmentMap departments, IReadOnlyList<PointOfInterest> pointsOfInterest,
        IReadOnlyList<TankFish> tankFish, IReadOnlyList<CounterMeal> counterMeals, IReadOnlyList<CampGoods> campGoods,
        IReadOnlyList<Landmark> landmarks, StoreSpace space, PillarLayout layout)
    {
        var random = SeededRandom.For(seed, StockStream);
        int Amount(int min, int max) => min + random.Next(Math.Max(max - min, 0) + 1);
        var stock = new List<StockedGoods>();
        var leftBehindCandidates = new List<int>();
        var lostAndFound = new Dictionary<int, List<int>>();
        var canyonBoxes = new List<int>();

        foreach (var piece in pieces)
        {
            if (piece.DisplayId >= 0)
            {
                var display = displays[piece.DisplayId];
                var kinds = GoodsCatalog.KindsOn(departments.All[display.DepartmentId].Type, display.Theme, piece.Type);
                if (kinds.Count > 0)
                {
                    if (random.Chance(config.StockedShelfShare))
                        stock.Add(new StockedGoods(piece.Id, random.Pick(kinds), Amount(config.ShelfAmountMin, config.ShelfAmountMax)));
                }
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
            else if (IsCanyonBox(piece, landmarks) && random.Chance(config.StockedShelfShare))
                canyonBoxes.Add(piece.Id);
        }

        // The Rack canyon's boxes hold any kind of shop Goods but Food, the other classes in the Store's mix, so its few Drinks stay
        // few. Food is left to the Cafeterias and the food shelves, and the canyon's Sector can still be short of it.
        if (canyonBoxes.Count > 0)
        {
            var classes = Enum.GetValues<GoodsClass>().Where(c => c != GoodsClass.Food && config.TargetPerHectare.GetValueOrDefault(c) > 0).ToList();
            var mix = classes.Sum(c => config.TargetPerHectare[c]);
            foreach (var id in canyonBoxes)
            {
                var roll = random.NextFloat() * mix;
                var goodsClass = classes.First(c => (roll -= config.TargetPerHectare[c]) < 0 || c == classes[^1]);
                var kinds = GoodsCatalog.ShopKinds.Where(k => GoodsCatalog.ClassOf(k) == goodsClass).ToList();
                stock.Add(new StockedGoods(id, random.Pick(kinds), Amount(config.CanyonBoxAmountMin, config.CanyonBoxAmountMax)));
            }
        }

        // A Set dinner table is laid for four; ordinary Display tables keep only their rare left-behind find.
        foreach (var piece in pieces.Where(p => p.Type == FurnitureType.DiningTable && p.PointOfInterestId >= 0 &&
                     pointsOfInterest[p.PointOfInterestId].Type == PointOfInterestType.SetDinnerTable))
            stock.Add(new StockedGoods(piece.Id, GoodsKind.FreshFood, 4));
        foreach (var fish in tankFish)
            stock.Add(new StockedGoods(fish.TankId, GoodsKind.LiveFish, Amount(config.FishMin, config.FishMax)));
        foreach (var meal in counterMeals)
            stock.Add(new StockedGoods(meal.CounterId, meal.Kind == MealKind.Meatballs ? GoodsKind.Meatballs : GoodsKind.AlmondCake,
                Amount(config.MealPortionsMin, config.MealPortionsMax)));
        // The backpacks of the supplies stand are Gear, not Goods, so they are not store stock.
        foreach (var goods in campGoods)
            if (goods.Kind != CampGoodsKind.Backpack)
                stock.Add(goods.Kind == CampGoodsKind.Marshmallows
                    ? new StockedGoods(goods.StandId, GoodsKind.Marshmallows, Amount(config.MarshmallowAmountMin, config.MarshmallowAmountMax))
                    : new StockedGoods(goods.StandId, GoodsKind.TinnedFood, Amount(config.CampStandAmountMin, config.CampStandAmountMax)));

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

        // Now and then a unit of shop food left on a table, of any Department, Point of interest or Landmark.
        var held = stock.Select(g => g.FurnitureId).ToHashSet();
        var food = GoodsCatalog.ShopKinds.Where(k => GoodsCatalog.ClassOf(k) == GoodsClass.Food).ToList();
        foreach (var piece in pieces)
            if (GoodsCatalog.FoodTables.Contains(piece.Type) && !held.Contains(piece.Id) && random.Chance(config.TableFoodChance))
                stock.Add(new StockedGoods(piece.Id, random.Pick(food), 1, LeftBehind: true));

        if (config.SectorGuarantee)
            Balance(stock, pieces, space, layout, config);
        return stock.AsReadOnly();
    }

    /// <summary>Each Sector's stock by Goods class, with its Surplus and Shortage, in Sector order.</summary>
    internal static IReadOnlyList<SectorStock> Summarize(IReadOnlyList<StockedGoods> stock, IReadOnlyList<FurniturePiece> pieces,
        StoreSpace space, PillarLayout layout, StockConfig config)
    {
        var sectors = layout.SectorsPerSide() * layout.SectorsPerSide();
        var classes = Enum.GetValues<GoodsClass>();
        var amounts = new int[sectors, classes.Length];
        var byId = pieces.ToDictionary(p => p.Id);  // ids are not list indexes once Furniture has been removed
        foreach (var goods in stock)
            amounts[PillarGenerator.SectorIndexAt(space, layout, byId[goods.FurnitureId].Position), (int)GoodsCatalog.ClassOf(goods.Kind)] += goods.Amount;
        var means = classes.Select(c => Enumerable.Range(0, sectors).Sum(s => (double)amounts[s, (int)c]) / sectors).ToArray();
        return Enumerable.Range(0, sectors).Select(s => new SectorStock(s,
            classes.ToDictionary(c => c, c => amounts[s, (int)c]),
            classes.Where(c => Standing(amounts[s, (int)c], means[(int)c], config) > 0).ToArray(),
            classes.Where(c => Standing(amounts[s, (int)c], means[(int)c], config) < 0).ToArray())).ToList().AsReadOnly();
    }

    // +1 for a Surplus, -1 for a Shortage, 0 for neither: a Sector's amount of a class against the Store's mean per Sector of it.
    private static int Standing(double amount, double mean, StockConfig config) =>
        mean <= 0 ? -1 : amount >= config.SurplusRatio * mean ? 1 : amount <= config.ShortageRatio * mean ? -1 : 0;

    // How far past a threshold the guarantee aims, so rounding the amounts back to whole units keeps the Sector on its side.
    private const double Margin = 0.12;

    /// <summary>
    /// Reshapes the amounts of the Display stock, the Cafeteria meals and the Aquarium fish (camp goods, canyon boxes and finds keep theirs): first each Goods class's Store total into its
    /// band, then, for each Sector without a Surplus, its best-stocked class is raised at the expense of the Sectors that can give
    /// it, and for each Sector without a Shortage its worst-stocked class is lowered in favour of the Sectors not short of it.
    /// Moving stock between Sectors keeps each class's total, so the band holds; every shelf keeps at least one unit, except in a
    /// Sector lowered to its Shortage, which may leave some bare.
    /// </summary>
    private static void Balance(List<StockedGoods> stock, IReadOnlyList<FurniturePiece> pieces, StoreSpace space, PillarLayout layout,
        StockConfig config)
    {
        var sectors = layout.SectorsPerSide() * layout.SectorsPerSide();
        var classCount = Enum.GetValues<GoodsClass>().Length;
        var drawn = new double[sectors, classCount];   // The Display stock as drawn,
        var display = new double[sectors, classCount]; // as reshaped,
        var shelves = new int[sectors, classCount];    // on how many pieces,
        var other = new double[sectors, classCount];   // and the stock that keeps its amounts.
        var cellOf = new (int Sector, int Class)[stock.Count];
        for (var i = 0; i < stock.Count; i++)
        {
            var piece = pieces[stock[i].FurnitureId];
            var cell = cellOf[i] = (PillarGenerator.SectorIndexAt(space, layout, piece.Position), (int)GoodsCatalog.ClassOf(stock[i].Kind));
            if (Reshaped(piece, stock[i]))
            {
                drawn[cell.Sector, cell.Class] += stock[i].Amount;
                shelves[cell.Sector, cell.Class]++;
            }
            else
                other[cell.Sector, cell.Class] += stock[i].Amount;
        }

        // Each class's total into its band: the Display stock is scaled evenly, which leaves every Sector's standing about as it was.
        var hectares = space.Size * space.Size / 10_000.0;
        var means = new double[classCount];
        for (var c = 0; c < classCount; c++)
        {
            double displayTotal = 0, otherTotal = 0;
            for (var s = 0; s < sectors; s++)
                (displayTotal, otherTotal) = (displayTotal + drawn[s, c], otherTotal + other[s, c]);
            var target = config.TargetPerHectare.GetValueOrDefault((GoodsClass)c) * hectares;
            // Aim inside the band's edge, so rounding to whole units cannot push the total out of it.
            var band = Math.Abs(config.ClassTotalBand) * target * (1 - Margin);
            var total = Math.Clamp(displayTotal + otherTotal, target - band, target + band);
            var scale = displayTotal > 0 ? Math.Max(total - otherTotal, 0) / displayTotal : 0;
            for (var s = 0; s < sectors; s++)
                display[s, c] = Math.Max(drawn[s, c] * scale, shelves[s, c]);
            means[c] = Enumerable.Range(0, sectors).Sum(s => display[s, c] + other[s, c]) / sectors;
        }

        double Amount(int s, int c) => display[s, c] + other[s, c];
        int StandingOf(int s, int c) => Standing(Amount(s, c), means[c], config);
        // A standing that rounding to whole units cannot lose: a Sector only just past a threshold is moved further.
        int FirmStandingOf(int s, int c) =>
            Amount(s, c) >= config.SurplusRatio * (1 + Margin / 2) * means[c] ? 1 : Amount(s, c) <= config.ShortageRatio * (1 - Margin / 2) * means[c] ? -1 : 0;
        // Moves units of class c into Sector s (out of it, if negative) from the other Sectors that can give (or take) them, each
        // in proportion to what it can; false, changing nothing, if they cannot.
        bool Move(int s, int c, double units)
        {
            // Lowering a Sector may leave some of its shelves bare, but never the whole class.
            if (shelves[s, c] == 0 || display[s, c] + units < (units < 0 ? 1 : shelves[s, c]))
                return false;
            var share = new double[sectors];
            for (var d = 0; d < sectors; d++)
                if (d != s && shelves[d, c] > 0)
                    share[d] = units > 0
                        // A giver keeps a unit per shelf, and its Surplus in the class if it has one.
                        ? Math.Max(0, display[d, c] - Math.Max(shelves[d, c],
                            StandingOf(d, c) > 0 ? config.SurplusRatio * (1 + Margin) * means[c] - other[d, c] : 0))
                        // A taker must not be short of the class, so no Sector loses its Shortage.
                        : StandingOf(d, c) < 0 ? 0 : display[d, c];
            var room = share.Sum();
            if (room <= 0 || (units > 0 && room < units))
                return false;
            for (var d = 0; d < sectors; d++)
                display[d, c] -= share[d] * units / room;
            display[s, c] += units;
            return true;
        }

        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            for (var s = 0; s < sectors; s++)
            {
                var byAmount = Enumerable.Range(0, classCount).OrderByDescending(c => means[c] > 0 ? Amount(s, c) / means[c] : 0).ToArray();
                if (!byAmount.Any(c => FirmStandingOf(s, c) > 0))
                    changed |= byAmount.Any(c => Move(s, c, config.SurplusRatio * (1 + Margin) * means[c] - Amount(s, c)));
                if (!byAmount.Any(c => FirmStandingOf(s, c) < 0))
                    changed |= byAmount.Reverse().Any(c => Move(s, c, config.ShortageRatio * (1 - Margin) * means[c] - Amount(s, c)));
            }
            if (!changed)
                break;
        }

        // Back to whole units a Sector and class at a time, so its total lands on the reshaped amount even when shelves hold only a few.
        foreach (var cell in Enumerable.Range(0, stock.Count).Where(i => Reshaped(pieces[stock[i].FurnitureId], stock[i])).GroupBy(i => cellOf[i]))
        {
            var (s, c) = cell.Key;
            var ideal = cell.ToDictionary(i => i, i => stock[i].Amount * display[s, c] / drawn[s, c]);
            var total = Math.Max(1, (int)Math.Round(display[s, c]));
            // Every shelf keeps a unit while there are units enough; a Sector lowered past that leaves some shelves bare.
            var least = total >= shelves[s, c] ? 1 : 0;
            var amounts = ideal.ToDictionary(e => e.Key, e => Math.Max(least, (int)Math.Floor(e.Value)));
            var left = total - amounts.Values.Sum();
            // Units short go to the shelves the rounding cut most, units over come off those it cut least.
            foreach (var i in ideal.OrderByDescending(e => e.Value - Math.Floor(e.Value)).Select(e => e.Key).Take(Math.Max(left, 0)))
                amounts[i]++;
            foreach (var i in ideal.Where(e => amounts[e.Key] > least).OrderBy(e => e.Value - Math.Floor(e.Value)).Select(e => e.Key).Take(Math.Max(-left, 0)))
                amounts[i]--;
            foreach (var (i, amount) in amounts)
                stock[i] = stock[i] with { Amount = amount };
        }
        stock.RemoveAll(g => g.Amount == 0);
    }

    // The meals and fish count with the shelves, so a Sector whose Cafeteria or Aquarium holds plenty of food can still be lowered to a Shortage of it.
    private static bool Reshaped(FurniturePiece piece, StockedGoods goods) =>
        !goods.LeftBehind && (piece.DisplayId >= 0 || goods.Kind is GoodsKind.Meatballs or GoodsKind.AlmondCake or GoodsKind.LiveFish);

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
