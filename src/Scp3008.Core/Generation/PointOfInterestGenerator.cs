using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Where a Point of interest stands, chosen before any Furniture is placed.</summary>
/// <param name="Quarters">Quarter turns from the Point of interest's own frame (its width east-west) to the Store.</param>
/// <param name="Scaffold">How the abandoned scaffold climbs its Pillar; null for every other kind.</param>
internal sealed record PointOfInterestSite(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size, int Quarters,
    ScaffoldPlan? Scaffold = null);

/// <summary>
/// How the abandoned scaffold climbs its Pillar, in the Pillar's own frame before its quarter turn: along the middle
/// of the walkway ring by the north face towards the east, one step diagonally across the north-east corner, then
/// south along the east face (mirrored: west, round the north-west corner, down the west face).
/// </summary>
/// <param name="StepsBeforeCorner">Steps along the first face.</param>
/// <param name="Steps">All its steps, the corner step included.</param>
internal sealed record ScaffoldPlan(Pillar Pillar, bool Mirrored, int StepsBeforeCorner, int Steps);

/// <summary>The built Points of interest, their Furniture and the network the Ball pits make.</summary>
internal sealed record PointOfInterestLayout(IReadOnlyList<PointOfInterest> PointsOfInterest, IReadOnlyList<FurniturePiece> Pieces,
    BallPitNetwork BallPitNetwork);

/// <summary>
/// Places and builds the Points of interest from the seed: the canteens (in Restaurant/food market
/// Departments) and the cart graveyards, then, Sector by Sector, the dinner tables, restrooms and the lost and
/// found desk, then the rare ones: three Ball pits (in the Kids' area where there is room), one or two of each of
/// the armchair henge, EXIT sign, mannequin crowd, door to nowhere and display aquarium, and the abandoned
/// scaffold. Each but the scaffold stands on a rectangle inside one department cell, turned by a quarter turn,
/// clear of the Pillars and their walkway rings, the Landmarks and each other; the scaffold climbs one Pillar on
/// its walkway ring. The Displays then keep off them all. Each is built from its own random stream as individual Furniture.
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
        PointOfInterestType.ArmchairHenge => new(13f, 13f),
        PointOfInterestType.ExitSign => new(6f, 6f),
        PointOfInterestType.MannequinCrowd => new(7f, 6f),
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
                var slack = (new Vector2(cellSize) - size) / 2f - new Vector2(Clearance);
                var centre = new Vector2((cx + 0.5f) * cellSize, (cy + 0.5f) * cellSize)
                    + new Vector2(random.Range(-slack.X, slack.X), random.Range(-slack.Y, slack.Y));
                if (!IsFree(space, pillars, landmarks, sites, centre, size))
                    continue;
                var sector = sectors.First(s => centre.X >= s.Origin.X && centre.X < s.Origin.X + s.Size &&
                                                centre.Y >= s.Origin.Y && centre.Y < s.Origin.Y + s.Size);
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
        sites.Add(PlanScaffold(random, space, sectors, pillars, sites.Count));
        return sites.AsReadOnly();
    }

    /// <summary>Picks the Pillar the abandoned scaffold climbs, which way round and how far, and the rectangle its steps cover.</summary>
    private static PointOfInterestSite PlanScaffold(SeededRandom random, StoreSpace space, IReadOnlyList<Sector> sectors, IReadOnlyList<Pillar> pillars, int id)
    {
        var pillar = pillars[random.Next(pillars.Count)];
        var quarters = random.Next(4);
        var plan = new ScaffoldPlan(pillar, random.Chance(0.5f), 6 + random.Next(5), 18 + random.Next(7));
        var (centre, size) = ScaffoldBounds(plan);
        var position = space.Wrap(pillar.Position + LandmarkBuilder.Turn(centre, quarters));
        var sector = sectors.First(s => s.Name == pillar.Coordinate);
        return new PointOfInterestSite(id, PointOfInterestType.AbandonedScaffold, sector.Id, position,
            (quarters & 1) == 0 ? size : new Vector2(size.Y, size.X), quarters, plan);
    }

    /// <summary>Centre and size, in the Pillar's frame before its quarter turn, of the ring segment the scaffold's steps cover.</summary>
    private static (Vector2 Centre, Vector2 Size) ScaffoldBounds(ScaffoldPlan plan)
    {
        var half = plan.Pillar.Width / 2f;
        var outer = plan.Pillar.ClearHalfWidth;
        // Along the first face from the first step to the ring's outer edge; down the second from the ring's outer edge to the last step.
        var low = new Vector2(half - plan.StepsBeforeCorner * ScaffoldRun, -outer);
        var high = new Vector2(outer, -half + (plan.Steps - plan.StepsBeforeCorner - 1) * ScaffoldRun);
        var centre = (low + high) / 2f;
        return (plan.Mirrored ? centre with { X = -centre.X } : centre, high - low);
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
            Vector3? lookout = null;
            if (site.Scaffold is { } plan)
            {
                var top = BuildScaffold(builder, plan);
                var at = space.Wrap(site.Centre + LandmarkBuilder.Turn(new Vector2(top.X, top.Y), site.Quarters));
                lookout = new Vector3(at.X, at.Y, top.Z);
            }
            foreach (var piece in builder.Pieces)
                pieces.Add(piece with { Id = firstPieceId + pieces.Count });
            pois.Add(new PointOfInterest(site.Id, site.Type, site.SectorId, site.Centre, site.Size)
            {
                PillarId = site.Scaffold?.Pillar.Id ?? -1,
                Lookout = lookout,
            });
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
    /// A ring of armchairs facing in, some pairs bridged by a sofa laid on their backs like a lintel, a horseshoe of
    /// armchairs inside it, and a rug with a low table at the centre; one of the ring's chairs knocked over.
    /// </summary>
    private static void BuildArmchairHenge(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var tint = random.Next(FurniturePalettes.TintCount);
        int Tint() => random.Chance(0.7f) ? tint : random.Next(FurniturePalettes.TintCount);
        const int ring = 12;
        const float radius = 5.4f;
        var start = random.Range(0f, MathF.Tau / ring);
        static Vector2 At(float angle, float distance) => new(distance * MathF.Cos(angle), distance * MathF.Sin(angle));
        // Every other pair of neighbouring chairs may carry a lintel.
        var lintels = new HashSet<int>();
        for (var pair = random.Next(2); pair < ring; pair += 2)
            if (random.Chance(0.6f))
                lintels.Add(pair);
        var fallen = random.Next(ring);
        for (var i = 0; i < ring; i++)
        {
            var angle = start + i * MathF.Tau / ring;
            // Facing the centre from the angle a: its front (-sin r, cos r) is (-cos a, -sin a), so r = a + π/2.
            var knocked = i == fallen && !lintels.Contains(i) && !lintels.Contains((i + ring - 1) % ring);
            b.Put(FurnitureType.Armchair, At(angle, radius), 0f, angle + Quarter, tilt: knocked ? Quarter : 0f, tint: Tint());
        }
        var lintel = FurnitureCatalog.VariantCount(FurnitureType.Sofa) > 2 ? 2 : 0;
        var chairHeight = FurnitureCatalog.Spec(FurnitureType.Armchair).Height;
        foreach (var pair in lintels)
        {
            var angle = start + (pair + 0.5f) * MathF.Tau / ring;
            b.Put(FurnitureType.Sofa, At(angle, radius * MathF.Cos(MathF.PI / ring)), chairHeight, angle + Quarter, lintel, tint: Tint());
        }
        // The inner horseshoe, open towards the first chair of the ring.
        for (var i = 0; i < 5; i++)
        {
            var angle = start + MathF.PI + (i - 2) * 0.55f;
            b.Put(FurnitureType.Armchair, At(angle, 3f), 0f, angle + Quarter, tint: Tint());
        }
        b.Put(FurnitureType.Rug, Vector2.Zero, 0f, start, tint: tint);
        b.Put(FurnitureType.CoffeeTable, Vector2.Zero, 0f, start + Quarter);
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

    /// <summary>A crowd of mannequins standing about, most of them facing one way, as if watching.</summary>
    private static void BuildMannequinCrowd(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var facing = random.Range(-MathF.PI, MathF.PI);
        var placed = new List<Vector2>();
        var wanted = 8 + random.Next(7);
        for (var attempt = 0; attempt < 200 && placed.Count < wanted; attempt++)
        {
            var at = new Vector2(random.Range(-3.2f, 3.2f), random.Range(-2.7f, 2.7f));
            if (placed.Any(p => Vector2.Distance(p, at) < 0.8f))
                continue;
            var rotation = random.Chance(0.75f) ? facing + random.Range(-0.25f, 0.25f) : random.Range(-MathF.PI, MathF.PI);
            if (b.Put(FurnitureType.Mannequin, at, 0f, rotation))
                placed.Add(at);
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

    /// <summary>Length of the walkway ring each step of the abandoned scaffold takes.</summary>
    private const float ScaffoldRun = 1f;

    /// <summary>
    /// The abandoned scaffold: a stair of Furniture up its Pillar's walkway ring. Each step is a column of shelf units
    /// with wardrobes laid flat on top, the top one its tread, spanning the middle of the ring; every fourth step adds a
    /// shelf unit beneath and starts again from one wardrobe. It turns one corner of the Pillar and stops in mid-air.
    /// </summary>
    /// <returns>The middle of the top tread in the Point of interest's own frame (X, Y) and its height (Z).</returns>
    private static Vector3 BuildScaffold(PointOfInterestBuilder b, ScaffoldPlan plan)
    {
        var half = plan.Pillar.Width / 2f;
        var middle = half + Pillar.WalkwayWidth / 2f;
        var (centre, _) = ScaffoldBounds(plan);
        var shelf = FurnitureCatalog.Spec(FurnitureType.ShelfUnit);
        var wardrobe = FurnitureCatalog.Spec(FurnitureType.Wardrobe);
        var top = Vector3.Zero;
        for (var k = 0; k < plan.Steps; k++)
        {
            // The middle of the step and the way out from the Pillar, in the Pillar's frame.
            Vector2 at, outward;
            if (k < plan.StepsBeforeCorner)
                (at, outward) = (new Vector2(half - (plan.StepsBeforeCorner - k - 0.5f) * ScaffoldRun, -middle), new Vector2(0f, -1f));
            else if (k == plan.StepsBeforeCorner)
                (at, outward) = (new Vector2(middle, -middle), Vector2.Normalize(new Vector2(1f, -1f)));
            else
                (at, outward) = (new Vector2(middle, -half + (k - plan.StepsBeforeCorner - 0.5f) * ScaffoldRun), new Vector2(1f, 0f));
            if (plan.Mirrored)
                (at, outward) = (at with { X = -at.X }, outward with { X = -outward.X });
            var local = at - centre;
            // Its front (-sin r, cos r) faces out; a shelf unit turned a quarter back has its width across the ring.
            var facingOut = MathF.Atan2(-outward.X, outward.Y);
            var height = 0f;
            for (var i = 0; i < k / 4; i++, height += shelf.Height)
                Step(FurnitureType.ShelfUnit, local, height, facingOut - Quarter, 0f);
            for (var i = 0; i <= k % 4; i++, height += wardrobe.Depth)
                // A wardrobe laid on its front lies from its base out along its front: start it half its height further in.
                Step(FurnitureType.Wardrobe, local - outward * (wardrobe.Height / 2f), height + wardrobe.Depth / 2f, facingOut, Quarter);
            top = new Vector3(local.X, local.Y, height);
        }
        return top;

        // The rectangle is the stair's own bounding box, so a missing piece would leave a gap in it: never drop one.
        void Step(FurnitureType type, Vector2 local, float elevation, float rotation, float tilt)
        {
            if (!b.Put(type, local, elevation, rotation, tilt: tilt))
                throw new InvalidOperationException("A step of the abandoned scaffold sticks out of its rectangle.");
        }
    }
}

/// <summary>Places the Furniture of one Point of interest in its own frame, keeping every piece inside its rectangle.</summary>
internal sealed class PointOfInterestBuilder(StoreSpace space, PointOfInterestSite site, SeededRandom random)
{
    private readonly List<FurniturePiece> _pieces = [];

    public SeededRandom Random { get; } = random;

    public IReadOnlyList<FurniturePiece> Pieces => _pieces;

    /// <summary>Places a piece with the centre of its base at a local position and elevation; false if it would stick out of the rectangle.</summary>
    public bool Put(FurnitureType type, Vector2 local, float elevation, float rotation, int variant = 0, float tilt = 0f, int tint = 0)
    {
        var turn = site.Quarters * MathF.PI / 2f;
        var piece = new FurniturePiece(0, type, site.Centre + LandmarkBuilder.Turn(local, site.Quarters), elevation,
            (float)Math.IEEERemainder(rotation + turn, 2.0 * Math.PI), variant, Tint: tint, Tilt: tilt, PointOfInterestId: site.Id);
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
