using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Where a Point of interest stands, chosen before any Furniture is placed.</summary>
/// <param name="Quarters">Quarter turns from the Point of interest's own frame (its width east-west) to the Store.</param>
internal sealed record PointOfInterestSite(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size, int Quarters);

/// <summary>The built Points of interest, their Furniture and the network the Ball pits make.</summary>
internal sealed record PointOfInterestLayout(IReadOnlyList<PointOfInterest> PointsOfInterest, IReadOnlyList<FurniturePiece> Pieces,
    BallPitNetwork BallPitNetwork);

/// <summary>
/// Places and builds the Points of interest from the seed: the canteens (in Restaurant/food market
/// Departments) and the cart graveyards, then, Sector by Sector, the dinner tables, restrooms and the lost and
/// found desk, then the rare ones: three Ball pits (in the Kids' area where there is room), one or two of each of
/// the armchair henge, EXIT sign, mannequin crowd, door to nowhere and display aquarium. Each
/// stands on a rectangle inside one department cell, turned by a quarter turn, clear of the Pillars and their
/// walkway rings, the Landmarks and each other. The Displays then keep off them all. Each is built from its own random stream as individual Furniture.
/// </summary>
internal static class PointOfInterestGenerator
{
    private const int PlanStream = 6;
    private const int BuildStream = 7;

    /// <summary>Ball pits in every Store.</summary>
    private const int BallPitCount = 3;

    /// <summary>Free floor kept between a Point of interest and a Pillar's walkway ring or a Landmark.</summary>
    private const float Clearance = 1f;
    /// <summary>Free floor kept between two Points of interest.</summary>
    private const float Gap = 2f;

    /// <summary>The Furniture-free margin Displays keep around a Point of interest.</summary>
    public const float DisplayMargin = 1f;

    private static Vector2 OwnSize(PointOfInterestType type) => type switch
    {
        PointOfInterestType.Restroom => new(8f, 6f),
        PointOfInterestType.Canteen => new(12f, 10f),
        PointOfInterestType.SetDinnerTable => new(6f, 5f),
        PointOfInterestType.LostAndFoundDesk => new(5f, 5f),
        PointOfInterestType.ShoppingCartGraveyard => new(8f, 6f),
        PointOfInterestType.BallPit => new(8f, 8f),
        PointOfInterestType.ArmchairHenge => new(28f, 33f),
        PointOfInterestType.ExitSign => new(6f, 6f),
        PointOfInterestType.MannequinCrowd => new(12f, 9f),
        PointOfInterestType.DoorToNowhere => new(5f, 11f),
        PointOfInterestType.DisplayAquarium => new(9f, 5f),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static IReadOnlyList<PointOfInterestSite> Plan(int seed, StoreSpace space, DepartmentMap departments, IReadOnlyList<Sector> sectors,
        IReadOnlyList<Pillar> pillars, IReadOnlyList<LandmarkSite> landmarks)
    {
        var random = SeededRandom.For(seed, PlanStream);
        var cellSize = departments.CellSize;
        var sites = new List<PointOfInterestSite>();

        bool TryPlace(PointOfInterestType type, int firstCellX, int firstCellY, int cellsX, int cellsY, Func<int, int, bool> allowed)
        {
            var cells = new List<(int X, int Y)>();
            for (var y = firstCellY; y < firstCellY + cellsY; y++)
                for (var x = firstCellX; x < firstCellX + cellsX; x++)
                    if (allowed(x, y))
                        cells.Add((x, y));
            while (cells.Count > 0)
            {
                var pick = random.Next(cells.Count);
                var (cx, cy) = cells[pick];
                cells[pick] = cells[^1];
                cells.RemoveAt(cells.Count - 1);
                var quarters = random.Next(4);
                var own = OwnSize(type);
                var size = (quarters & 1) == 0 ? own : new Vector2(own.Y, own.X);
                // A Point of interest bigger than a department cell (the Armchair henge) stands centred on its cell.
                var slack = Vector2.Max(Vector2.Zero, (new Vector2(cellSize) - size) / 2f - new Vector2(Clearance));
                var centre = new Vector2((cx + 0.5f) * cellSize, (cy + 0.5f) * cellSize)
                    + new Vector2(random.Range(-slack.X, slack.X), random.Range(-slack.Y, slack.Y));
                if (!IsFree(space, pillars, landmarks, sites, centre, size))
                    continue;
                var sector = sectors.First(s => centre.X >= s.Origin.X && centre.X < s.Origin.X + s.Size &&
                                                centre.Y >= s.Origin.Y && centre.Y < s.Origin.Y + s.Size);
                if (centre.X - size.X / 2f < sector.Origin.X || centre.X + size.X / 2f > sector.Origin.X + sector.Size ||
                    centre.Y - size.Y / 2f < sector.Origin.Y || centre.Y + size.Y / 2f > sector.Origin.Y + sector.Size)
                    continue;
                sites.Add(new PointOfInterestSite(sites.Count, type, sector.Id, centre, size, quarters));
                return true;
            }
            return false;
        }

        void Place(PointOfInterestType type, int firstCellX, int firstCellY, int cellsX, int cellsY, Func<int, int, bool> allowed)
        {
            if (!TryPlace(type, firstCellX, firstCellY, cellsX, cellsY, allowed))
                throw new InvalidOperationException($"No room for a {type}.");
        }

        var side = departments.CellsPerSide;
        for (var n = 3 + random.Next(2); n > 0; n--)
            Place(PointOfInterestType.Canteen, 0, 0, side, side,
                (x, y) => departments.AtCell(x, y).Type == DepartmentType.RestaurantAndFoodMarket);
        for (var n = 2 + random.Next(3); n > 0; n--)
            Place(PointOfInterestType.ShoppingCartGraveyard, 0, 0, side, side, (_, _) => true);
        foreach (var sector in sectors)
        {
            var firstX = (int)MathF.Round(sector.Origin.X / cellSize);
            var firstY = (int)MathF.Round(sector.Origin.Y / cellSize);
            var cells = (int)MathF.Round(sector.Size / cellSize);
            void PlaceInSector(PointOfInterestType type) => Place(type, firstX, firstY, cells, cells, (_, _) => true);
            for (var n = 2 + random.Next(2); n > 0; n--)
                PlaceInSector(PointOfInterestType.SetDinnerTable);
            for (var n = 1 + random.Next(3); n > 0; n--)
                PlaceInSector(PointOfInterestType.Restroom);
            PlaceInSector(PointOfInterestType.LostAndFoundDesk);
        }

        // The rare ones, anywhere in the Store; the Ball pits in the Kids' area where it has room.
        for (var n = 0; n < BallPitCount; n++)
            if (!TryPlace(PointOfInterestType.BallPit, 0, 0, side, side, (x, y) => departments.AtCell(x, y).Type == DepartmentType.KidsArea))
                Place(PointOfInterestType.BallPit, 0, 0, side, side, (_, _) => true);
        foreach (var type in new[]
                 {
                     PointOfInterestType.ArmchairHenge, PointOfInterestType.ExitSign, PointOfInterestType.MannequinCrowd,
                     PointOfInterestType.DoorToNowhere, PointOfInterestType.DisplayAquarium,
                 })
            for (var n = 1 + random.Next(2); n > 0; n--)
                Place(type, 0, 0, side, side, (_, _) => true);
        return sites.AsReadOnly();
    }

    /// <summary>Free floor between neighbouring Points of interest in the showcase row.</summary>
    private const float ShowcaseGap = 6f;

    /// <summary>
    /// The Point of interest showcase: every kind once, unturned, side by side along x in a row centred on the Store, on
    /// the row nearest the middle of the Store that keeps clear of every Pillar and its walkway ring.
    /// </summary>
    public static IReadOnlyList<PointOfInterestSite> Showcase(StoreSpace space, IReadOnlyList<Sector> sectors, IReadOnlyList<Pillar> pillars)
    {
        var kinds = Enum.GetValues<PointOfInterestType>().ToList();
        var length = kinds.Sum(k => OwnSize(k).X) + ShowcaseGap * (kinds.Count - 1);
        if (length > space.Size)
            throw new InvalidOperationException($"The showcase row ({length:0} m) is longer than the Store.");
        var start = MathF.Round((space.Size - length) / 2f);
        var deepest = kinds.Max(k => OwnSize(k).Y);
        var rows = new List<float>();
        for (var y = deepest / 2f; y + deepest / 2f <= space.Size; y += 1f)
            rows.Add(y);
        foreach (var y in rows.OrderBy(y => MathF.Abs(y - space.Size / 2f)))
        {
            var sites = new List<PointOfInterestSite>();
            var x = start;
            foreach (var type in kinds)
            {
                var size = OwnSize(type);
                var centre = new Vector2(x + size.X / 2f, y);
                var sector = sectors.First(s => centre.X >= s.Origin.X && centre.X < s.Origin.X + s.Size &&
                                                centre.Y >= s.Origin.Y && centre.Y < s.Origin.Y + s.Size);
                sites.Add(new PointOfInterestSite(sites.Count, type, sector.Id, centre, size, 0));
                x += size.X + ShowcaseGap;
            }
            if (sites.All(site => pillars.All(p => Apart(space.Delta(p.Position, site.Centre), new Vector2(p.ClearHalfWidth), site.Size / 2f, Clearance))))
                return sites.AsReadOnly();
        }
        throw new InvalidOperationException("No row of the Store keeps the showcase clear of the Pillars.");
    }

    private static bool IsFree(StoreSpace space, IReadOnlyList<Pillar> pillars, IReadOnlyList<LandmarkSite> landmarks,
        List<PointOfInterestSite> sites, Vector2 centre, Vector2 size)
    {
        var half = size / 2f;
        foreach (var pillar in pillars)
            if (!Apart(space.Delta(pillar.Position, centre), new Vector2(pillar.ClearHalfWidth), half, Clearance))
                return false;
        foreach (var landmark in landmarks)
            if (!Apart(space.Delta(landmark.Centre, centre), landmark.Size / 2f, half, Clearance))
                return false;
        foreach (var other in sites)
            if (!Apart(space.Delta(other.Centre, centre), other.Size / 2f, half, Gap))
                return false;
        return true;
    }

    /// <summary>Whether two rectangles, this far apart, keep at least a gap between them along one axis.</summary>
    private static bool Apart(Vector2 delta, Vector2 halfA, Vector2 halfB, float gap) =>
        MathF.Abs(delta.X) >= halfA.X + halfB.X + gap || MathF.Abs(delta.Y) >= halfA.Y + halfB.Y + gap;

    public static PointOfInterestLayout Build(int seed, StoreSpace space, IReadOnlyList<PointOfInterestSite> sites, int firstPieceId)
    {
        var pois = new List<PointOfInterest>();
        var pieces = new List<FurniturePiece>();
        foreach (var site in sites)
        {
            var builder = new PointOfInterestBuilder(space, site, SeededRandom.For(seed, BuildStream, site.Id));
            switch (site.Type)
            {
                case PointOfInterestType.Restroom: BuildRestroom(builder); break;
                case PointOfInterestType.Canteen: BuildCanteen(builder); break;
                case PointOfInterestType.SetDinnerTable: BuildDinnerTable(builder); break;
                case PointOfInterestType.LostAndFoundDesk: BuildLostAndFound(builder); break;
                case PointOfInterestType.ShoppingCartGraveyard: BuildCartGraveyard(builder); break;
                case PointOfInterestType.BallPit: BuildBallPit(builder); break;
                case PointOfInterestType.ArmchairHenge: BuildArmchairHenge(builder); break;
                case PointOfInterestType.ExitSign: BuildExitSign(builder); break;
                case PointOfInterestType.MannequinCrowd: BuildMannequinCrowd(builder); break;
                case PointOfInterestType.DoorToNowhere: BuildDoorToNowhere(builder); break;
                case PointOfInterestType.DisplayAquarium: BuildAquarium(builder); break;
            }
            foreach (var piece in builder.Pieces)
                pieces.Add(piece with { Id = firstPieceId + pieces.Count });
            pois.Add(new PointOfInterest(site.Id, site.Type, site.SectorId, site.Centre, site.Size));
        }
        var network = new BallPitNetwork(pois.Where(p => p.Type == PointOfInterestType.BallPit).Select(p => p.Id).ToList().AsReadOnly());
        return new PointOfInterestLayout(pois.AsReadOnly(), pieces.AsReadOnly(), network);
    }

    private const float Quarter = MathF.PI / 2f;

    // In every build below the local frame has x east and y south, its origin at the centre of the rectangle;
    // a piece with rotation 0 faces south, π faces north, +π/2 faces west and -π/2 faces east.

    /// <summary>A row of stalls along the north wall, each with a toilet between partitions, and basins with mirrors along the south.</summary>
    private static void BuildRestroom(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var stalls = 2 + random.Next(2);
        const float stallWidth = 1.5f;
        var x0 = -4f + 0.3f;
        for (var i = 0; i <= stalls; i++)
            foreach (var y in new[] { -2.55f, -1.75f })
                b.Put(FurnitureType.Bookcase, new Vector2(x0 + i * stallWidth, y), 0f, Quarter);
        for (var i = 0; i < stalls; i++)
            b.Put(FurnitureType.Toilet, new Vector2(x0 + (i + 0.5f) * stallWidth, -2.65f), 0f, 0f);
        var basins = 2 + random.Next(2);
        var start = -(basins - 1) * 1.0f / 2f + random.Range(-0.6f, 0.6f);
        for (var i = 0; i < basins; i++)
        {
            b.Put(FurnitureType.VanityUnit, new Vector2(start + i * 1.0f, 2.2f), 0f, MathF.PI);
            b.Put(FurnitureType.FloorMirror, new Vector2(start + i * 1.0f, 2.74f), 0f, MathF.PI);
        }
        if (random.Chance(0.6f))
            b.Put(FurnitureType.HallBench, new Vector2(random.Range(-3f, 3f), 0.4f), 0f, random.Chance(0.5f) ? 0f : MathF.PI);
    }

    /// <summary>A serving line of counters with meals, chilled cabinets and shelves along the walls, and rows of dining tables.</summary>
    private static void BuildCanteen(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var counterHeight = FurnitureCatalog.Spec(FurnitureType.Counter).Height;
        for (var x = -4.1f; x <= 4.11f; x += 2.05f)
        {
            b.Put(FurnitureType.Counter, new Vector2(x, -4.6f), 0f, 0f);
            foreach (var dx in new[] { -0.45f, 0.45f })
                if (random.Chance(0.8f))
                    b.Put(FurnitureType.MealPlate, new Vector2(x + dx, -4.6f), counterHeight, 0f);
        }
        foreach (var y in new[] { -3.1f, -0.9f })
        {
            b.Put(FurnitureType.Freezer, new Vector2(-5.55f, y), 0f, -Quarter);
            b.Put(FurnitureType.GroceryShelf, new Vector2(5.55f, y), 0f, Quarter);
        }
        var tableVariant = random.Chance(0.4f) && FurnitureCatalog.VariantCount(FurnitureType.DiningTable) > 1 ? 1 : 0;
        var columns = 3 + random.Next(2);
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < columns; column++)
            {
                var at = new Vector2((column - (columns - 1) / 2f) * 2.4f, 0.4f + row * 2.7f);
                b.Put(FurnitureType.DiningTable, at, 0f, 0f, tableVariant);
                SeatsAround(b, at, tableVariant, plates: random.Chance(0.5f), sideSeats: 1);
            }
    }

    /// <summary>Chairs around a dining table, and a plate before each if it is laid.</summary>
    private static void SeatsAround(PointOfInterestBuilder b, Vector2 table, int variant, bool plates, int sideSeats = 2)
    {
        var spec = FurnitureCatalog.Spec(FurnitureType.DiningTable, variant);
        var tableHeight = spec.Height;
        void Seat(Vector2 offset, float rotation, Vector2 plate)
        {
            b.Put(FurnitureType.Chair, table + offset, 0f, rotation);
            if (plates)
                b.Put(FurnitureType.MealPlate, table + plate, tableHeight, 0f);
        }
        var edge = spec.Depth / 2f + 0.27f;
        for (var i = 0; i < sideSeats; i++)
        {
            var x = (i - (sideSeats - 1) / 2f) * (spec.Width / sideSeats);
            Seat(new(x, -edge), 0f, new(x, -0.22f));
            Seat(new(x, edge), MathF.PI, new(x, 0.22f));
        }
    }

    /// <summary>A rug, a table laid for a meal with chairs and plates, and a sideboard with more food against the wall.</summary>
    private static void BuildDinnerTable(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var variant = random.Next(FurnitureCatalog.VariantCount(FurnitureType.DiningTable));
        var table = new Vector2(0f, 0.3f);
        b.Put(FurnitureType.Rug, table, 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));
        b.Put(FurnitureType.DiningTable, table, 0f, 0f, variant);
        SeatsAround(b, table, variant, plates: true, sideSeats: 2);
        b.Put(FurnitureType.Sideboard, new Vector2(0f, -2.1f), 0f, 0f);
        b.Put(FurnitureType.MealPlate, new Vector2(random.Range(-0.5f, 0.5f), -2.1f), FurnitureCatalog.Spec(FurnitureType.Sideboard).Height, 0f);
    }

    /// <summary>A desk with the staff's chair before shelves and cabinets of what has been found, and boxes of it on the floor.</summary>
    private static void BuildLostAndFound(PointOfInterestBuilder b)
    {
        var random = b.Random;
        foreach (var x in new[] { -1.45f, 0.6f })
            b.Put(FurnitureType.ShelfUnit, new Vector2(x, -2.2f), 0f, 0f);
        b.Put(FurnitureType.FilingCabinet, new Vector2(2.1f, -2.2f), 0f, 0f);
        b.Put(FurnitureType.Desk, new Vector2(0f, 0.2f), 0f, 0f, FurnitureCatalog.VariantCount(FurnitureType.Desk) > 2 ? 2 : 0);
        b.Put(FurnitureType.OfficeChair, new Vector2(random.Range(-0.3f, 0.3f), -0.8f), 0f, random.Range(-0.3f, 0.3f));
        b.Put(FurnitureType.FilingCabinet, new Vector2(-2.1f, 0.6f), 0f, Quarter);
        b.Put(FurnitureType.CushionBin, new Vector2(-1.9f, 1.8f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));
        b.Put(FurnitureType.CoatRack, new Vector2(2.1f, 1.9f), 0f, 0f);
        // Boxes of what has been found, one stacked on another.
        b.Put(FurnitureType.FlatPackBox, new Vector2(0.4f, 1.5f), 0f, 0f);
        b.Put(FurnitureType.FlatPackBox, new Vector2(0.4f, 1.5f), FurnitureCatalog.Spec(FurnitureType.FlatPackBox).Height, random.Range(-0.3f, 0.3f));
    }

    /// <summary>A corral fenced with shelving on three sides, full of nested shopping carts, with a few knocked over outside.</summary>
    private static void BuildCartGraveyard(PointOfInterestBuilder b)
    {
        var random = b.Random;
        foreach (var x in new[] { -2.05f, 0f, 2.05f })
            b.Put(FurnitureType.ShelfUnit, new Vector2(x, -2.65f), 0f, 0f);
        foreach (var x in new[] { -3.5f, 3.5f })
            foreach (var y in new[] { -1.2f, 1.0f })
                b.Put(FurnitureType.ShelfUnit, new Vector2(x, y), 0f, Quarter);
        for (var x = -2.4f; x <= 2.41f; x += 0.8f)
            foreach (var y in new[] { -1.75f, -0.6f, 0.55f })
                if (random.Chance(0.75f))
                    b.Put(FurnitureType.ShoppingCart, new Vector2(x, y) + new Vector2(random.Range(-0.04f, 0.04f), random.Range(-0.03f, 0.03f)),
                        0f, MathF.PI + random.Range(-0.05f, 0.05f));
        // A few carts left askew outside the corral.
        foreach (var x in new[] { -2.2f, 0f, 2.2f })
            if (random.Chance(0.7f))
                b.Put(FurnitureType.ShoppingCart, new Vector2(x, 2.2f), 0f, random.Range(-0.5f, 0.5f));
    }

    /// <summary>A bouncy castle full of balls, with play cubes strewn before its open front, a toy chest and a shoe cabinet beside it.</summary>
    private static void BuildBallPit(PointOfInterestBuilder b)
    {
        var random = b.Random;
        b.Put(FurnitureType.BouncyCastle, new Vector2(0f, -1f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));
        // One play cube to a metre-wide slot, so they never overlap however they are turned.
        var slots = new List<float> { -2f, -1f, 0f, 1f, 2f };
        for (var i = 3 + random.Next(3); i > 0; i--)
        {
            var slot = random.Next(slots.Count);
            b.Put(FurnitureType.PlayCube, new Vector2(slots[slot] + random.Range(-0.1f, 0.1f), random.Range(2.3f, 3.4f)), 0f,
                random.Range(-0.8f, 0.8f), tint: random.Next(FurniturePalettes.TintCount));
            slots.RemoveAt(slot);
        }
        var side = random.Chance(0.5f) ? 1f : -1f;
        b.Put(FurnitureType.ToyChest, new Vector2(3.3f * side, random.Range(-2.5f, 0f)), 0f, side * Quarter, tint: random.Next(FurniturePalettes.TintCount));
        b.Put(FurnitureType.ShoeCabinet, new Vector2(-3.4f * side, 2.6f), 0f, -side * Quarter);
    }

    /// <summary>
    /// A copy of Stonehenge about 26 m across, built of sofas and armchairs only: an outer circle of sofas stood on end,
    /// capped by lintel sofas laid across them, with a few gaps, a fallen upright and fallen lintels; inside it a
    /// horseshoe of five trilithons (pairs of stacked upright sofas under a lintel sofa, the middle one the tallest) open
    /// towards the entrance axis, an inner horseshoe of armchairs, an altar sofa at the centre and a lone leaning heel
    /// stone on the entrance axis outside the ring.
    /// </summary>
    private static void BuildArmchairHenge(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var tint = random.Next(FurniturePalettes.TintCount);
        int Tint() => random.Chance(0.7f) ? tint : random.Next(FurniturePalettes.TintCount);
        // The big sofa (variant 2) is the stone: 2.7 m long, so 2.7 m tall on end.
        var stoneVariant = FurnitureCatalog.VariantCount(FurnitureType.Sofa) > 2 ? 2 : 0;
        var stone = FurnitureCatalog.Spec(FurnitureType.Sofa, stoneVariant);
        // The ring's centre, sat towards the back of the site so the heel stone fits in front on the +y entrance axis.
        var centre = new Vector2(0f, -2.5f);
        const int uprights = 28;
        const float ringRadius = 12.4f;
        static Vector2 At(Vector2 from, float angle, float distance) => from + new Vector2(distance * MathF.Cos(angle), distance * MathF.Sin(angle));

        // A sofa stood on its end, its footprint centred on a spot, its across axis along `rotation`, with `lowerStones`
        // of them below it. It is rolled about its base centre, so it is shifted back by half its height.
        void Upright(Vector2 spot, float rotation, int lowerStones, float lean = 0f)
        {
            var shift = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation)) * (stone.Height / 2f);
            b.Put(FurnitureType.Sofa, spot + shift, lowerStones * stone.Width + stone.Width / 2f, rotation, stoneVariant,
                tint: Tint(), roll: MathF.PI / 2f + lean);
        }
        void Lintel(Vector2 spot, float rotation, float elevation) =>
            b.Put(FurnitureType.Sofa, spot, elevation, rotation, stoneVariant, tint: Tint());

        // The outer circle: a few uprights missing, their lintels with them, one lying where it fell.
        var missing = new HashSet<int>();
        for (var tries = 0; missing.Count < 3 && tries < 100; tries++)
        {
            var i = random.Next(uprights);
            if (!missing.Contains(i) && !missing.Contains((i + 1) % uprights) && !missing.Contains((i + uprights - 1) % uprights))
                missing.Add(i);
        }
        var noLintel = new HashSet<int>();
        foreach (var i in missing)
        {
            noLintel.Add(i);
            noLintel.Add((i + uprights - 1) % uprights);
        }
        for (var n = 3; n > 0; n--)
            noLintel.Add(random.Next(uprights));
        var fallenUpright = missing.ElementAt(random.Next(missing.Count));
        for (var i = 0; i < uprights; i++)
        {
            var angle = i * MathF.Tau / uprights + random.Range(-0.01f, 0.01f);
            if (missing.Contains(i))
            {
                if (i == fallenUpright)
                {
                    var side = random.Chance(0.5f) ? 1f : -1f;
                    b.Put(FurnitureType.Sofa, At(centre, angle, ringRadius + side * 2.2f), 0f, angle + Quarter + random.Range(-0.4f, 0.4f),
                        stoneVariant, tint: Tint());
                }
                continue;
            }
            Upright(At(centre, angle, ringRadius), angle + Quarter, 0);
        }
        for (var i = 0; i < uprights; i++)
        {
            var angle = (i + 0.5f) * MathF.Tau / uprights;
            if (!noLintel.Contains(i))
                Lintel(At(centre, angle, ringRadius * MathF.Cos(MathF.PI / uprights)), angle + Quarter, stone.Width);
            else if (missing.Contains((i + 1) % uprights) && random.Chance(0.6f))
                // A fallen lintel, lying on its front inside the ring.
                b.Put(FurnitureType.Sofa, At(centre, angle, ringRadius - 2.4f), 0f, angle + Quarter + random.Range(-0.3f, 0.3f), stoneVariant,
                    MathF.PI / 2f, Tint());
        }

        // The horseshoe of five trilithons, open towards +y; the middle one (at the back, -y) is the tallest.
        const float trilithonRadius = 6.6f;
        for (var k = -2; k <= 2; k++)
        {
            var angle = -MathF.PI / 2f + k * 0.85f;
            var heap = k == 0 ? 3 : 2;
            var spot = At(centre, angle, trilithonRadius);
            var along = new Vector2(-MathF.Sin(angle), MathF.Cos(angle));
            foreach (var side in new[] { -0.85f, 0.85f })
                for (var level = 0; level < heap; level++)
                    Upright(spot + along * (side + random.Range(-0.03f, 0.03f)), angle + Quarter, level);
            Lintel(spot, angle + Quarter, heap * stone.Width);
        }

        // The inner horseshoe of armchairs, facing the centre and open towards +y.
        for (var i = 0; i < 7; i++)
        {
            var angle = -MathF.PI / 2f + (i - 3) * 0.5f;
            b.Put(FurnitureType.Armchair, At(centre, angle, 3.6f), 0f, angle + Quarter, tint: Tint());
        }
        // The altar: a sofa on its back at the centre, across the entrance axis.
        b.Put(FurnitureType.Sofa, centre + new Vector2(0f, stone.Height / 2f), 0f, 0f, stoneVariant, -MathF.PI / 2f, Tint());
        // The heel stone, alone and leaning, on the entrance axis outside the ring.
        Upright(centre + new Vector2(0f, 16f), MathF.PI, 0, random.Range(-0.12f, 0.12f));
    }

    /// <summary>A glowing EXIT sign at the mouth of a blind alcove of shelving, its arrow pointing sideways along shelving that leads nowhere.</summary>
    private static void BuildExitSign(PointOfInterestBuilder b)
    {
        var random = b.Random;
        foreach (var x in new[] { -1f, 1f })
            b.Put(FurnitureType.ShelfUnit, new Vector2(x, -2.65f), 0f, 0f);
        foreach (var x in new[] { -2.35f, 2.35f })
            foreach (var y in new[] { -1.2f, 0.8f })
                b.Put(FurnitureType.ShelfUnit, new Vector2(x, y), 0f, x < 0f ? -Quarter : Quarter);
        b.Put(FurnitureType.ExitSign, new Vector2(random.Range(-0.3f, 0.3f), 2.4f), 0f, 0f);
    }

    /// <summary>Fewest and most mannequins in a crowd: about three times the first, smaller crowd.</summary>
    private const int CrowdMin = 24, CrowdMax = 42;

    /// <summary>The standing poses and builds (variants 0–4); the damaged ones (variant 5, a fallen figure) are placed separately.</summary>
    private const int StandingVariants = 5, ArmlessVariant = 5;

    /// <summary>Free floor kept between two mannequins' bounding circles.</summary>
    private const float CrowdSpacing = 0.05f;

    /// <summary>
    /// A crowd of bare plastic mannequins standing about, most of them facing one way, as if watching; one lies knocked
    /// over and, half of the time, another has lost an arm. Their bounding circles never overlap.
    /// </summary>
    private static void BuildMannequinCrowd(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var facing = random.Range(-MathF.PI, MathF.PI);
        var placed = new List<(Vector2 At, float Radius)>();
        var wanted = CrowdMin + random.Next(CrowdMax - CrowdMin + 1);
        var damaged = new List<(int Variant, float Tilt)> { (0, MathF.PI / 2f) };
        if (random.Chance(0.5f))
            damaged.Add((ArmlessVariant, 0f));

        for (var attempt = 0; attempt < 4000 && placed.Count < wanted; attempt++)
        {
            var (variant, tilt) = placed.Count < damaged.Count ? damaged[placed.Count] : (random.Next(StandingVariants), 0f);
            var at = new Vector2(random.Range(-5.4f, 5.4f), random.Range(-3.9f, 3.9f));
            var spec = FurnitureCatalog.Spec(FurnitureType.Mannequin, variant);
            // A fallen figure lies along its full height.
            var radius = tilt == 0f ? MathF.Sqrt(spec.Width * spec.Width + spec.Depth * spec.Depth) / 2f : spec.Height / 2f + 0.1f;
            if (placed.Any(p => Vector2.Distance(p.At, at) < p.Radius + radius + CrowdSpacing))
                continue;
            var rotation = tilt != 0f || !random.Chance(0.75f) ? random.Range(-MathF.PI, MathF.PI) : facing + random.Range(-0.25f, 0.25f);
            if (b.Put(FurnitureType.Mannequin, at, 0f, rotation, variant, tilt, tint: random.Next(FurniturePalettes.TintCount)))
                placed.Add((at, radius));
        }
    }

    /// <summary>
    /// A free-standing door frame in the middle of an aisle of shelving that runs on alike on both sides of it,
    /// with a mat before it either way, so the same aisle is seen through the door.
    /// </summary>
    private static void BuildDoorToNowhere(PointOfInterestBuilder b)
    {
        var tint = b.Random.Next(FurniturePalettes.TintCount);
        foreach (var x in new[] { -1.9f, 1.9f })
            for (var y = -4.4f; y <= 4.41f; y += 2.2f)
                b.Put(FurnitureType.ShelfUnit, new Vector2(x, y), 0f, x < 0f ? -Quarter : Quarter);
        b.Put(FurnitureType.DoorFrame, Vector2.Zero, 0f, 0f);
        foreach (var y in new[] { -2f, 2f })
            b.Put(FurnitureType.Rug, new Vector2(0f, y), 0f, 0f, tint: tint);
    }

    /// <summary>A huge glowing aquarium of live fish, with benches to watch it from, plants beside it and a box of fish food.</summary>
    private static void BuildAquarium(PointOfInterestBuilder b)
    {
        var random = b.Random;
        b.Put(FurnitureType.Aquarium, new Vector2(0f, -1.5f), 0f, 0f);
        foreach (var x in new[] { -1.4f, 1.4f })
            b.Put(FurnitureType.HallBench, new Vector2(x + random.Range(-0.2f, 0.2f), 1.4f), 0f, MathF.PI);
        foreach (var x in new[] { -3.6f, 3.6f })
            b.Put(FurnitureType.LargePlant, new Vector2(x, -1.6f), 0f, random.Range(-MathF.PI, MathF.PI), tint: random.Next(FurniturePalettes.TintCount));
        b.Put(FurnitureType.FlatPackBox, new Vector2(random.Chance(0.5f) ? -3.3f : 3.3f, 1.5f), 0f, random.Range(-0.3f, 0.3f));
    }

}

/// <summary>Places the Furniture of one Point of interest in its own frame, keeping every piece inside its rectangle.</summary>
internal sealed class PointOfInterestBuilder(StoreSpace space, PointOfInterestSite site, SeededRandom random)
{
    private readonly List<FurniturePiece> _pieces = [];

    public SeededRandom Random { get; } = random;

    public IReadOnlyList<FurniturePiece> Pieces => _pieces;

    /// <summary>Places a piece with the centre of its base at a local position and elevation; false if it would stick out of the rectangle.</summary>
    public bool Put(FurnitureType type, Vector2 local, float elevation, float rotation, int variant = 0, float tilt = 0f, int tint = 0, float roll = 0f)
    {
        var turn = site.Quarters * MathF.PI / 2f;
        var piece = new FurniturePiece(0, type, site.Centre + LandmarkBuilder.Turn(local, site.Quarters), elevation,
            (float)Math.IEEERemainder(rotation + turn, 2.0 * Math.PI), variant, Tint: tint, Tilt: tilt, PointOfInterestId: site.Id, Roll: roll);
        var corners = FurnitureCatalog.Corners(piece);
        var lowest = corners.Min(c => c.Z);
        if (lowest < 0f)
        {
            piece = piece with { Elevation = piece.Elevation - lowest };
            corners = FurnitureCatalog.Corners(piece);
        }
        var half = site.Size / 2f;
        foreach (var corner in corners)
            if (MathF.Abs(corner.X - site.Centre.X) > half.X || MathF.Abs(corner.Y - site.Centre.Y) > half.Y)
                return false;
        _pieces.Add(piece with { Position = space.Wrap(piece.Position) });
        return true;
    }
}
