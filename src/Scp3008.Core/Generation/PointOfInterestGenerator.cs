using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Where a Point of interest stands, chosen before any Furniture is placed.</summary>
/// <param name="Quarters">Quarter turns from the Point of interest's own frame (its width east-west) to the Store.</param>
internal sealed record PointOfInterestSite(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size, int Quarters);

/// <summary>The built Points of interest, their Furniture, the network the Ball pits make, the fish in the Aquarium aisles' tanks and the meals on the Cafeterias' counters.</summary>
internal sealed record PointOfInterestLayout(IReadOnlyList<PointOfInterest> PointsOfInterest, IReadOnlyList<FurniturePiece> Pieces,
    BallPitNetwork BallPitNetwork, IReadOnlyList<TankFish> TankFish, IReadOnlyList<CounterMeal> CounterMeals);

/// <summary>
/// Places and builds the Points of interest from the seed: the canteens (in Restaurant/food market
/// Departments) and the cart graveyards, then, Sector by Sector, the dinner tables, restrooms and the lost and
/// found desk, then the rare ones: three Ball pits (ball pools, in the Kids' area where there is room), one or two of each of
/// the armchair henge, EXIT sign, mannequin crowd, door to nowhere and Aquarium aisle. Each
/// stands on a rectangle inside one department cell, turned by a quarter turn, clear of the Pillars and their
/// walkway rings, the Landmarks and each other. The Displays then keep off them all. Each is built from its own random stream as individual Furniture.
/// </summary>
internal static class PointOfInterestGenerator
{
    private const int PlanStream = 6;
    private const int BuildStream = 7;

    /// <summary>Ball pits in every Store.</summary>
    private const int BallPitCount = 3;

    /// <summary>Cafeterias in every Store.</summary>
    private const int CafeteriaCount = 3;
    /// <summary>The side of the block of Department cells a Cafeteria stands on.</summary>
    private const int CafeteriaBlockCells = 3;

    /// <summary>Free floor kept between a Point of interest and a Pillar's walkway ring or a Landmark.</summary>
    private const float Clearance = 1f;
    /// <summary>Free floor kept between two Points of interest.</summary>
    private const float Gap = 2f;

    /// <summary>The Furniture-free margin Displays keep around a Point of interest.</summary>
    public const float DisplayMargin = 1f;

    private static Vector2 OwnSize(PointOfInterestType type) => type switch
    {
        PointOfInterestType.SetDinnerTable => new(6f, 5f),
        PointOfInterestType.ShoppingCartGraveyard => new(8f, 6f),
        PointOfInterestType.BallPit => new(8f, 8f),
        PointOfInterestType.ArmchairHenge => new(28f, 33f),
        PointOfInterestType.ExitSign => new(6f, 6f),
        PointOfInterestType.MannequinCrowd => new(12f, 9f),
        PointOfInterestType.DoorToNowhere => new(5f, 11f),
        PointOfInterestType.AquariumAisle => new(12f, 6f),
        PointOfInterestType.Cafeteria => new Vector2(48f, 40f),
        PointOfInterestType.StoreRestroom => new Vector2(10.4f, 6.0f),
        PointOfInterestType.LostAndFound => new Vector2(6.5f, 5.0f),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static IReadOnlyList<PointOfInterestSite> Plan(int seed, StoreSpace space, DepartmentMap departments, IReadOnlyList<Sector> sectors,
        IReadOnlyList<Pillar> pillars, IReadOnlyList<LandmarkSite> landmarks)
    {
        var random = SeededRandom.For(seed, PlanStream);
        var cellSize = departments.CellSize;
        var sites = new List<PointOfInterestSite>();
        // The 3 × 3 blocks of Department cells the Cafeterias stand on: no other Point of interest may touch them.
        var reserved = new List<(Vector2 Centre, Vector2 Size)>();

        bool TryPlace(PointOfInterestType type, int firstCellX, int firstCellY, int cellsX, int cellsY, Func<int, int, bool> allowed, int blockCells = 1)
        {
            var cells = new List<(int X, int Y)>();
            for (var y = firstCellY; y < firstCellY + cellsY - (blockCells - 1); y++)
                for (var x = firstCellX; x < firstCellX + cellsX - (blockCells - 1); x++)
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
                var block = new Vector2(cellSize * blockCells);
                var slack = Vector2.Max(Vector2.Zero, (block - size) / 2f - new Vector2(Clearance));
                var centre = new Vector2((cx + blockCells / 2f) * cellSize, (cy + blockCells / 2f) * cellSize)
                    + new Vector2(random.Range(-slack.X, slack.X), random.Range(-slack.Y, slack.Y));
                if (!IsFree(space, pillars, landmarks, sites, centre, size))
                    continue;
                // Only a Cafeteria may stand on a Cafeteria's block of cells.
                if (type != PointOfInterestType.Cafeteria &&
                    reserved.Any(r => !Apart(space.Delta(r.Centre, centre), r.Size / 2f, size / 2f, 0f)))
                    continue;
                var sector = sectors.First(s => centre.X >= s.Origin.X && centre.X < s.Origin.X + s.Size &&
                                                centre.Y >= s.Origin.Y && centre.Y < s.Origin.Y + s.Size);
                if (centre.X - size.X / 2f < sector.Origin.X || centre.X + size.X / 2f > sector.Origin.X + sector.Size ||
                    centre.Y - size.Y / 2f < sector.Origin.Y || centre.Y + size.Y / 2f > sector.Origin.Y + sector.Size)
                    continue;
                sites.Add(new PointOfInterestSite(sites.Count, type, sector.Id, centre, size, quarters));
                if (type == PointOfInterestType.Cafeteria)
                    reserved.Add((new Vector2((cx + blockCells / 2f) * cellSize, (cy + blockCells / 2f) * cellSize), block));
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
        // Each Cafeteria stands on a 3 × 3 block of cells that all belong to one Restaurant/food market Department; failing
        // that, on cells all of that type, or at least with one in the middle, so every Store has its Cafeterias.
        bool FoodMarket(int x, int y) => departments.AtCell(x, y).Type == DepartmentType.RestaurantAndFoodMarket;
        bool Block(int x, int y, Func<int, int, bool> cell)
        {
            for (var dy = 0; dy < CafeteriaBlockCells; dy++)
                for (var dx = 0; dx < CafeteriaBlockCells; dx++)
                    if (!cell(x + dx, y + dy))
                        return false;
            return true;
        }
        Func<int, int, bool>[] cafeteriaBlocks =
        [
            (x, y) => FoodMarket(x, y) && Block(x, y, (cx, cy) => departments.AtCell(cx, cy) == departments.AtCell(x, y)),
            (x, y) => Block(x, y, FoodMarket),
            (x, y) => FoodMarket(x + CafeteriaBlockCells / 2, y + CafeteriaBlockCells / 2),
        ];
        for (var n = CafeteriaCount; n > 0; n--)
            if (!cafeteriaBlocks.Any(allowed => TryPlace(PointOfInterestType.Cafeteria, 0, 0, side, side, allowed, CafeteriaBlockCells)))
                throw new InvalidOperationException("No room for a Cafeteria.");
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
                PlaceInSector(PointOfInterestType.StoreRestroom);
            PlaceInSector(PointOfInterestType.LostAndFound);
        }

        // The rare ones, anywhere in the Store; the Ball pits in the Kids' area where it has room.
        for (var n = 0; n < BallPitCount; n++)
            if (!TryPlace(PointOfInterestType.BallPit, 0, 0, side, side, (x, y) => departments.AtCell(x, y).Type == DepartmentType.KidsArea))
                Place(PointOfInterestType.BallPit, 0, 0, side, side, (_, _) => true);
        foreach (var type in new[]
                 {
                     PointOfInterestType.ArmchairHenge, PointOfInterestType.ExitSign, PointOfInterestType.MannequinCrowd,
                     PointOfInterestType.DoorToNowhere, PointOfInterestType.AquariumAisle,
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
        var fish = new List<TankFish>();
        var meals = new List<CounterMeal>();
        foreach (var site in sites)
        {
            var builder = new PointOfInterestBuilder(space, site, SeededRandom.For(seed, BuildStream, site.Id));
            switch (site.Type)
            {
                case PointOfInterestType.SetDinnerTable: BuildDinnerTable(builder); break;
                case PointOfInterestType.ShoppingCartGraveyard: BuildCartGraveyard(builder); break;
                case PointOfInterestType.BallPit: BuildBallPit(builder); break;
                case PointOfInterestType.ArmchairHenge: BuildArmchairHenge(builder); break;
                case PointOfInterestType.ExitSign: BuildExitSign(builder); break;
                case PointOfInterestType.MannequinCrowd: BuildMannequinCrowd(builder); break;
                case PointOfInterestType.DoorToNowhere: BuildDoorToNowhere(builder); break;
                case PointOfInterestType.AquariumAisle: BuildAquariumAisle(builder); break;
                case PointOfInterestType.Cafeteria: BuildCafeteria(builder); break;
                case PointOfInterestType.StoreRestroom: BuildStoreRestroom(builder); break;
                case PointOfInterestType.LostAndFound: BuildLostAndFoundServicePoint(builder); break;
            }
            var first = firstPieceId + pieces.Count;
            foreach (var (index, kind) in builder.Fish)
                fish.Add(new TankFish(first + index, kind));
            foreach (var (index, kind) in builder.Meals)
                meals.Add(new CounterMeal(first + index, kind));
            foreach (var piece in builder.Pieces)
                pieces.Add(piece with { Id = firstPieceId + pieces.Count });
            pois.Add(new PointOfInterest(site.Id, site.Type, site.SectorId, site.Centre, site.Size));
        }
        var network = new BallPitNetwork(pois.Where(p => p.Type == PointOfInterestType.BallPit).Select(p => p.Id).ToList().AsReadOnly());
        return new PointOfInterestLayout(pois.AsReadOnly(), pieces.AsReadOnly(), network, fish.AsReadOnly(), meals.AsReadOnly());
    }

    private const float Quarter = MathF.PI / 2f;
    // Centre of a side wall of the Store restroom: half of the 5.7 m between the front and back walls.
    private const float SideWallY = 1.425f;

    // In every build below the local frame has x east and y south, its origin at the centre of the rectangle;
    // a piece with rotation 0 faces south, π faces north, +π/2 faces west and -π/2 faces east.

    /// <summary>
    /// A recognisable, worn store restroom (10.4 × 6.0 m): tiled floor and perimeter walls with an entrance gap,
    /// stalls with doors (some ajar, one hanging off), toilets inside stalls, a row of washbasins with a long mirror
    /// (cracked mirror in variants), hand dryers, paper towel dispensers, a baby-changing table, restroom entrance sign,
    /// an out-of-order caution sign, and discarded paper litter on the floor. 3 floor-plan variants chosen from seed.
    /// </summary>
    private static void BuildStoreRestroom(PointOfInterestBuilder b)
    {
        var random = b.Random;
        // Tiled floor covering the entire 10.4 × 6.0 m room
        b.Put(FurnitureType.TiledFloor, Vector2.Zero, 0f, 0f);

        var plan = random.Next(3);
        switch (plan)
        {
            case 0:
            {
                // Plan 0: North stalls (4 stalls), South washbasins, East baby-changing station, SE entrance
                // Perimeter TiledWall segments (gap at South-East: x = 3.9)
                foreach (var x in new[] { -3.9f, -1.3f, 1.3f, 3.9f })
                    b.Put(FurnitureType.TiledWall, new Vector2(x, -2.925f), 0f, 0f);
                foreach (var x in new[] { -3.9f, -1.3f, 1.3f })
                    b.Put(FurnitureType.TiledWall, new Vector2(x, 2.925f), 0f, MathF.PI);
                // Side walls run between the front and back walls (variant 1 is half of the 5.7 m between them).
                foreach (var y in new[] { -SideWallY, SideWallY })
                {
                    b.Put(FurnitureType.TiledWall, new Vector2(-5.125f, y), 0f, Quarter, variant: 1);
                    b.Put(FurnitureType.TiledWall, new Vector2(5.125f, y), 0f, -Quarter, variant: 1);
                }

                // Restroom sign beside entrance
                b.Put(FurnitureType.RestroomSign, new Vector2(2.55f, 2.90f), 1.8f, 0f);

                // 4 stalls along North wall (x = -4.5 to 0.5)
                const float stallW = 1.25f;
                var x0 = -4.5f;
                for (var i = 0; i <= 4; i++)
                    b.Put(FurnitureType.StallPartition, new Vector2(x0 + i * stallW, -2.05f), 0f, 0f);

                for (var i = 0; i < 4; i++)
                {
                    var sx = x0 + (i + 0.5f) * stallW;
                    b.Put(FurnitureType.Toilet, new Vector2(sx, -2.45f), 0f, 0f);
                    if (i == 1)
                        b.Put(FurnitureType.StallDoor, new Vector2(sx, -1.25f), 0f, 0.45f); // ajar
                    else if (i == 2)
                    {
                        // hanging off broken
                        b.Put(FurnitureType.StallDoor, new Vector2(sx, -1.25f), 0f, -0.25f, tilt: -0.18f, roll: 0.08f);
                        b.Put(FurnitureType.OutOfOrderSign, new Vector2(sx, -0.55f), 0f, 0f);
                    }
                    else
                        b.Put(FurnitureType.StallDoor, new Vector2(sx, -1.25f), 0f, 0f); // closed
                }

                // Row of 3 washbasins along South wall
                foreach (var vx in new[] { -3.6f, -2.6f, -1.6f })
                    b.Put(FurnitureType.VanityUnit, new Vector2(vx, 2.55f), 0f, MathF.PI);
                // Long mirror above washbasins (variant 1: cracked)
                b.Put(FurnitureType.RestroomMirror, new Vector2(-2.6f, 2.82f), 1.1f, MathF.PI, variant: 1);
                // Hand dryer & paper towel dispenser
                b.Put(FurnitureType.HandDryer, new Vector2(-0.8f, 2.80f), 1.15f, MathF.PI);
                b.Put(FurnitureType.PaperTowelDispenser, new Vector2(-0.3f, 2.80f), 1.20f, MathF.PI);

                // Baby-changing table on East wall
                b.Put(FurnitureType.BabyChangingTable, new Vector2(4.80f, -1.0f), 0.85f, Quarter);

                // Discarded paper litter on floor
                b.Put(FurnitureType.LitterPaper, new Vector2(-0.5f, 2.0f), 0f, 0.5f);
                b.Put(FurnitureType.LitterPaper, new Vector2(-0.8f, 1.6f), 0f, -0.3f);
                b.Put(FurnitureType.LitterPaper, new Vector2(-1.2f, -0.1f), 0f, 1.2f);
                break;
            }
            case 1:
            {
                // Plan 1: West stalls (3 stalls), East washbasins, South baby-changing station, North entrance
                // Perimeter TiledWall segments (gap at North: x = 1.3)
                foreach (var x in new[] { -3.9f, -1.3f, 3.9f })
                    b.Put(FurnitureType.TiledWall, new Vector2(x, -2.925f), 0f, 0f);
                foreach (var x in new[] { -3.9f, -1.3f, 1.3f, 3.9f })
                    b.Put(FurnitureType.TiledWall, new Vector2(x, 2.925f), 0f, MathF.PI);
                // Side walls run between the front and back walls (variant 1 is half of the 5.7 m between them).
                foreach (var y in new[] { -SideWallY, SideWallY })
                {
                    b.Put(FurnitureType.TiledWall, new Vector2(-5.125f, y), 0f, Quarter, variant: 1);
                    b.Put(FurnitureType.TiledWall, new Vector2(5.125f, y), 0f, -Quarter, variant: 1);
                }

                // Restroom sign beside entrance
                b.Put(FurnitureType.RestroomSign, new Vector2(0f, -2.90f), 1.8f, MathF.PI);

                // 3 stalls along West wall (y = -1.8 to 1.2, facing East)
                const float stallH = 1.5f;
                var y0 = -2.55f;
                for (var i = 0; i <= 3; i++)
                    b.Put(FurnitureType.StallPartition, new Vector2(-4.25f, y0 + i * stallH), 0f, Quarter);

                for (var i = 0; i < 3; i++)
                {
                    var sy = y0 + (i + 0.5f) * stallH;
                    b.Put(FurnitureType.Toilet, new Vector2(-4.65f, sy), 0f, -Quarter);
                    if (i == 1)
                    {
                        // hanging off broken
                        b.Put(FurnitureType.StallDoor, new Vector2(-3.45f, sy), 0f, -Quarter, tilt: 0.18f, roll: -0.06f);
                        b.Put(FurnitureType.OutOfOrderSign, new Vector2(-2.65f, sy), 0f, -Quarter);
                    }
                    else if (i == 2)
                        b.Put(FurnitureType.StallDoor, new Vector2(-3.45f, sy), 0f, -Quarter + 0.4f); // ajar
                    else
                        b.Put(FurnitureType.StallDoor, new Vector2(-3.45f, sy), 0f, -Quarter); // closed
                }

                // Row of 3 washbasins along East wall
                foreach (var vy in new[] { -1.2f, -0.2f, 0.8f })
                    b.Put(FurnitureType.VanityUnit, new Vector2(4.75f, vy), 0f, Quarter);
                // Mirror above washbasins (variant 0: intact)
                b.Put(FurnitureType.RestroomMirror, new Vector2(5.02f, -0.2f), 1.1f, Quarter, variant: 0);
                // Hand dryer & paper towel dispenser
                b.Put(FurnitureType.HandDryer, new Vector2(5.00f, 1.7f), 1.15f, Quarter);
                b.Put(FurnitureType.PaperTowelDispenser, new Vector2(5.00f, 2.2f), 1.20f, Quarter);

                // Baby-changing table on South wall
                b.Put(FurnitureType.BabyChangingTable, new Vector2(2.0f, 2.50f), 0.85f, MathF.PI);

                // Discarded paper litter on floor
                b.Put(FurnitureType.LitterPaper, new Vector2(4.2f, 1.9f), 0f, 0.7f);
                b.Put(FurnitureType.LitterPaper, new Vector2(3.8f, 1.5f), 0f, -0.4f);
                b.Put(FurnitureType.LitterPaper, new Vector2(-2.2f, 0.0f), 0f, 1.1f);
                break;
            }
            default:
            {
                // Plan 2: Split stalls (2 North, 2 South on West half), East washbasins, East entrance
                // Perimeter TiledWall segments (gap at East wall center: y = 0)
                foreach (var x in new[] { -3.9f, -1.3f, 1.3f, 3.9f })
                {
                    b.Put(FurnitureType.TiledWall, new Vector2(x, -2.925f), 0f, 0f);
                    b.Put(FurnitureType.TiledWall, new Vector2(x, 2.925f), 0f, MathF.PI);
                }
                foreach (var y in new[] { -SideWallY, SideWallY })
                    b.Put(FurnitureType.TiledWall, new Vector2(-5.125f, y), 0f, Quarter, variant: 1);
                // East wall: a doorway 1 m wide at y = 0 to 1, walls to the front and back walls on both sides of it
                b.Put(FurnitureType.TiledWall, new Vector2(5.125f, -SideWallY), 0f, -Quarter, variant: 1);
                b.Put(FurnitureType.TiledWall, new Vector2(5.125f, 1.925f), 0f, -Quarter, variant: 2);

                // Restroom sign beside entrance
                b.Put(FurnitureType.RestroomSign, new Vector2(5.05f, -0.8f), 1.8f, Quarter);

                // 2 North stalls (x = -4.0, -2.6)
                foreach (var px in new[] { -4.7f, -3.3f, -1.9f })
                    b.Put(FurnitureType.StallPartition, new Vector2(px, -2.05f), 0f, 0f);
                b.Put(FurnitureType.Toilet, new Vector2(-4.0f, -2.45f), 0f, 0f);
                b.Put(FurnitureType.StallDoor, new Vector2(-4.0f, -1.25f), 0f, 0f);
                b.Put(FurnitureType.Toilet, new Vector2(-2.6f, -2.45f), 0f, 0f);
                b.Put(FurnitureType.StallDoor, new Vector2(-2.6f, -1.25f), 0f, 0.35f); // ajar

                // 2 South stalls (x = -4.0, -2.6)
                foreach (var px in new[] { -4.7f, -3.3f, -1.9f })
                    b.Put(FurnitureType.StallPartition, new Vector2(px, 2.05f), 0f, MathF.PI);
                b.Put(FurnitureType.Toilet, new Vector2(-4.0f, 2.45f), 0f, MathF.PI);
                // Hanging off broken
                b.Put(FurnitureType.StallDoor, new Vector2(-4.0f, 1.25f), 0f, MathF.PI, tilt: -0.16f, roll: 0.06f);
                b.Put(FurnitureType.OutOfOrderSign, new Vector2(-4.0f, 0.55f), 0f, MathF.PI);
                b.Put(FurnitureType.Toilet, new Vector2(-2.6f, 2.45f), 0f, MathF.PI);
                b.Put(FurnitureType.StallDoor, new Vector2(-2.6f, 1.25f), 0f, MathF.PI);

                // 3 Washbasins along North-East wall
                foreach (var vx in new[] { 1.0f, 2.0f, 3.0f })
                    b.Put(FurnitureType.VanityUnit, new Vector2(vx, -2.55f), 0f, 0f);
                // Mirror above washbasins (variant 1: cracked)
                b.Put(FurnitureType.RestroomMirror, new Vector2(2.0f, -2.82f), 1.1f, 0f, variant: 1);
                b.Put(FurnitureType.HandDryer, new Vector2(3.9f, -2.80f), 1.15f, 0f);
                b.Put(FurnitureType.PaperTowelDispenser, new Vector2(4.4f, -2.80f), 1.20f, 0f);

                // Baby-changing table on South-East wall
                b.Put(FurnitureType.BabyChangingTable, new Vector2(2.0f, 2.50f), 0.85f, MathF.PI);

                // Discarded paper litter on floor
                b.Put(FurnitureType.LitterPaper, new Vector2(3.5f, -2.0f), 0f, 0.3f);
                b.Put(FurnitureType.LitterPaper, new Vector2(-3.5f, 0.2f), 0f, -0.6f);
                break;
            }
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

    /// <summary>
    /// A recognisable store service point (6.5 × 5.0 m): service counter with a call bell, shelves of labelled boxes and bins,
    /// a rack of lost coats and umbrellas, a cart of unsorted things, a notice board of lost-item notes, and a generic store
    /// service sign; left abandoned with spilled open boxes, a toppled pile of boxes, items left on the counter, and a
    /// hand-written "back in 5 min" note. 3 floor-plan variants driven by seed.
    /// </summary>
    private static void BuildLostAndFoundServicePoint(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var plan = random.Next(3);
        const float counterHeight = 1.05f;

        switch (plan)
        {
            case 0:
            {
                // Plan 0: Front-facing service point (North shelving & notice board, South-central service counter)
                // North wall shelves (y = -2.15)
                b.Put(FurnitureType.ShelfUnit, new Vector2(-1.5f, -2.15f), 0f, 0f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.8f, -2.15f), 0.45f, 0f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.2f, -2.15f), 0.45f, 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.5f, -2.15f), 0.95f, -0.1f);

                b.Put(FurnitureType.ShelfUnit, new Vector2(1.2f, -2.15f), 0f, 0f);
                b.Put(FurnitureType.LabelledBox, new Vector2(0.8f, -2.15f), 0.45f, 0f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.5f, -2.15f), 0.45f, -0.15f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.1f, -2.15f), 0.95f, 0.1f);

                // Notice board between shelves on North wall
                b.Put(FurnitureType.NoticeBoard, new Vector2(-0.15f, -2.42f), 1.2f, 0f);

                // Storage bin beside east shelf
                b.Put(FurnitureType.CushionBin, new Vector2(2.75f, -2.0f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));

                // West side: coat rack and unsorted cart
                b.Put(FurnitureType.CoatRack, new Vector2(-2.7f, -1.0f), 0f, 0f);
                b.Put(FurnitureType.UnsortedCart, new Vector2(-2.4f, 1.2f), 0f, 0.25f);

                // Central service counter
                b.Put(FurnitureType.ServiceCounter, new Vector2(-0.2f, 0.4f), 0f, 0f);
                b.Put(FurnitureType.OfficeChair, new Vector2(-0.2f, -0.4f), 0f, 0.1f);

                // Items on counter
                b.Put(FurnitureType.ServiceBell, new Vector2(-0.9f, 0.4f), counterHeight, 0f);
                b.Put(FurnitureType.Note, new Vector2(0.2f, 0.4f), counterHeight, 0.15f);

                // Sign above counter
                b.Put(FurnitureType.LostAndFoundSign, new Vector2(-0.2f, 0.4f), 2.3f, 0f);

                // East floor abandonment: toppled pile and spilled open box
                b.Put(FurnitureType.LabelledBox, new Vector2(2.0f, 0.4f), 0f, 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(2.0f, 0.4f), 0.35f, -0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.4f, 0.5f), 0f, 0.4f, tilt: 0.38f, roll: -0.15f);
                b.Put(FurnitureType.LabelledBox, new Vector2(2.2f, 1.4f), 0f, -0.5f, variant: 1);
                break;
            }
            case 1:
            {
                // Plan 1: L-shaped service point (West back shelves, South counter facing east)
                // West wall shelves (x = -2.85)
                b.Put(FurnitureType.ShelfUnit, new Vector2(-2.85f, -1.1f), 0f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -1.4f), 0.45f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -0.8f), 0.45f, Quarter + 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -1.1f), 0.95f, Quarter - 0.15f);

                b.Put(FurnitureType.ShelfUnit, new Vector2(-2.85f, 1.0f), 0f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, 0.7f), 0.45f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, 1.3f), 0.45f, Quarter - 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, 1.0f), 0.95f, Quarter + 0.1f);

                b.Put(FurnitureType.CushionBin, new Vector2(-1.85f, -1.95f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));

                // North wall: notice board, coat rack, and unsorted cart
                b.Put(FurnitureType.NoticeBoard, new Vector2(-0.5f, -2.42f), 1.2f, 0f);
                b.Put(FurnitureType.CoatRack, new Vector2(1.2f, -2.0f), 0f, 0f);
                b.Put(FurnitureType.UnsortedCart, new Vector2(2.3f, -1.6f), 0f, -Quarter);

                // Counter facing East (rotation -Quarter)
                b.Put(FurnitureType.ServiceCounter, new Vector2(-0.8f, 0.5f), 0f, -Quarter);
                b.Put(FurnitureType.OfficeChair, new Vector2(-1.6f, 0.5f), 0f, -Quarter);

                // Items on counter
                b.Put(FurnitureType.ServiceBell, new Vector2(-0.8f, -0.1f), counterHeight, -Quarter);
                b.Put(FurnitureType.Note, new Vector2(-0.8f, 0.8f), counterHeight, -Quarter + 0.2f);

                // Sign above counter
                b.Put(FurnitureType.LostAndFoundSign, new Vector2(-0.8f, 0.5f), 2.3f, -Quarter);

                // Abandonment: toppled pile and spilled open box
                b.Put(FurnitureType.LabelledBox, new Vector2(1.0f, 0.4f), 0f, 0.2f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.0f, 0.4f), 0.35f, -0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(0.4f, 0.5f), 0f, -0.4f, tilt: -0.35f, roll: 0.2f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.2f, 1.4f), 0f, 0.4f, variant: 1);
                break;
            }
            default:
            {
                // Plan 2: Centred counter island with East/West dual storage racks
                // West and East storage racks
                b.Put(FurnitureType.ShelfUnit, new Vector2(-2.85f, -0.5f), 0f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -0.8f), 0.45f, Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -0.2f), 0.45f, Quarter + 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-2.85f, -0.5f), 0.95f, Quarter - 0.1f);

                b.Put(FurnitureType.ShelfUnit, new Vector2(2.85f, -0.5f), 0f, -Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(2.85f, -0.8f), 0.45f, -Quarter);
                b.Put(FurnitureType.LabelledBox, new Vector2(2.85f, -0.2f), 0.45f, -Quarter - 0.1f);
                b.Put(FurnitureType.LabelledBox, new Vector2(2.85f, -0.5f), 0.95f, -Quarter + 0.1f);

                b.Put(FurnitureType.CushionBin, new Vector2(2.6f, 1.4f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));

                // North wall
                b.Put(FurnitureType.NoticeBoard, new Vector2(0f, -2.42f), 1.2f, 0f);
                b.Put(FurnitureType.CoatRack, new Vector2(-1.6f, -2.0f), 0f, 0f);
                b.Put(FurnitureType.UnsortedCart, new Vector2(1.6f, -1.8f), 0f, 0.2f);

                // Central counter
                b.Put(FurnitureType.ServiceCounter, new Vector2(0f, 0.3f), 0f, 0f);
                b.Put(FurnitureType.OfficeChair, new Vector2(0f, -0.5f), 0f, 0.1f);

                // Items on counter
                b.Put(FurnitureType.ServiceBell, new Vector2(0.8f, 0.3f), counterHeight, 0f);
                b.Put(FurnitureType.Note, new Vector2(-0.3f, 0.3f), counterHeight, -0.15f);

                // Sign above counter
                b.Put(FurnitureType.LostAndFoundSign, new Vector2(0f, 0.3f), 2.3f, 0f);

                // Abandonment: toppled pile and spilled open box
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.7f, 0.9f), 0f, 0f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.7f, 0.9f), 0.35f, 0.2f);
                b.Put(FurnitureType.LabelledBox, new Vector2(-1.1f, 1.0f), 0f, 0.3f, tilt: 0.4f, roll: 0.18f);
                b.Put(FurnitureType.LabelledBox, new Vector2(1.6f, 1.2f), 0f, -0.3f, variant: 1);
                break;
            }
        }
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

    /// <summary>The Ball pit pool's centre in its Point of interest's frame, north of the middle so the step and bench fit in front.</summary>
    private static readonly Vector2 PoolCentre = new(0f, -0.6f);

    /// <summary>Thickness of the pool's padded walls.</summary>
    private const float PoolWall = 0.25f;

    /// <summary>Height of the ball fill in the pool, below the wall top: the bottom of the top layer of balls.</summary>
    private const float BallFill = 0.4f;

    /// <summary>
    /// A ball pool like a store's kids' play area: a low pool with padded foam walls, net railing on the walls, a gap
    /// with a foam step at the front (south), a small slide down into it through a gap in the west railing, and a layer
    /// of coloured balls filling it, each its own piece, with a few spilled on the floor around; a shoe cabinet and a
    /// bench for the grown-ups stand by.
    /// </summary>
    private static void BuildBallPit(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var pool = FurnitureCatalog.Spec(FurnitureType.BallPool);
        var (hw, hd) = (pool.Width / 2f, pool.Depth / 2f);
        b.Put(FurnitureType.BallPool, PoolCentre, 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));
        // Net railing on the wall tops, one metre a length: five along the back, four along the front round the
        // entrance gap, three along the east wall and two along the west round the slide.
        var railTint = random.Next(FurniturePalettes.TintCount);
        foreach (var x in new[] { -2f, -1f, 0f, 1f, 2f })
            b.Put(FurnitureType.NetRailing, PoolCentre + new Vector2(x, -hd + PoolWall / 2f), pool.Height, 0f, tint: railTint);
        foreach (var x in new[] { -2f, -1f, 1f, 2f })
            b.Put(FurnitureType.NetRailing, PoolCentre + new Vector2(x, hd - PoolWall / 2f), pool.Height, 0f, tint: railTint);
        foreach (var y in new[] { -1f, 0f, 1f })
            b.Put(FurnitureType.NetRailing, PoolCentre + new Vector2(hw - PoolWall / 2f, y), pool.Height, Quarter, tint: railTint);
        foreach (var y in new[] { -1f, 1f })
            b.Put(FurnitureType.NetRailing, PoolCentre + new Vector2(-hw + PoolWall / 2f, y), pool.Height, Quarter, tint: railTint);
        b.Put(FurnitureType.FoamStep, PoolCentre + new Vector2(0f, hd + 0.25f), 0f, 0f, tint: random.Next(FurniturePalettes.TintCount));
        // The slide stands against the outside of the west wall; its chute (not part of its footprint) runs east over the wall into the balls.
        var slide = FurnitureCatalog.Spec(FurnitureType.PlaySlide);
        var slideCentre = PoolCentre + new Vector2(-hw - slide.Depth / 2f, 0f);
        b.Put(FurnitureType.PlaySlide, slideCentre, 0f, -Quarter, tint: random.Next(FurniturePalettes.TintCount));
        var side = random.Chance(0.5f) ? 1f : -1f;
        var cabinet = new Vector2(2.9f * side, -3.6f);
        b.Put(FurnitureType.ShoeCabinet, cabinet, 0f, 0f);
        var bench = new Vector2(random.Range(1.4f, 2.4f) * side, 3.5f);
        b.Put(FurnitureType.HallBench, bench, 0f, MathF.PI);

        // The top layer of balls, rows staggered like packed balls, each a little jostled and heaped.
        const float spacing = 0.1f, rowSpacing = 0.087f;
        var ball = FurnitureCatalog.Spec(FurnitureType.PlayBall).Width;
        var inner = new Vector2(hw - PoolWall - ball / 2f - 0.01f, hd - PoolWall - ball / 2f - 0.01f);
        var row = 0;
        for (var y = -inner.Y; y <= inner.Y; y += rowSpacing, row++)
            for (var x = -inner.X + (row % 2) * spacing / 2f; x <= inner.X; x += spacing)
            {
                var at = new Vector2(Math.Clamp(x + random.Range(-0.01f, 0.01f), -inner.X, inner.X), Math.Clamp(y + random.Range(-0.01f, 0.01f), -inner.Y, inner.Y));
                b.Put(FurnitureType.PlayBall, PoolCentre + at, BallFill + random.Range(0f, 0.04f), random.Range(-MathF.PI, MathF.PI), tint: random.Next(FurniturePalettes.TintCount));
            }

        // A few spilled on the floor round the pool, clear of the step, slide, cabinet, bench and each other.
        (Vector2 Centre, Vector2 Half)[] taken =
        [
            (PoolCentre, new Vector2(hw, hd) + new Vector2(0.05f)),
            (PoolCentre + new Vector2(0f, hd + 0.25f), new Vector2(0.55f, 0.3f)),
            (slideCentre, new Vector2(slide.Depth / 2f, slide.Width / 2f) + new Vector2(0.05f)),
            (cabinet, new Vector2(0.45f, 0.25f)),
            (bench, new Vector2(0.65f, 0.25f)),
        ];
        var spilled = new List<Vector2>();
        var spill = 8 + random.Next(13);
        for (var tries = 0; spilled.Count < spill && tries < 400; tries++)
        {
            // Most land near the pool, where they fell over the walls.
            var at = PoolCentre + new Vector2(random.Range(-hw - 1.2f, hw + 1.2f), random.Range(-hd - 1.2f, hd + 1.2f));
            if (taken.Any(t => MathF.Abs(at.X - t.Centre.X) < t.Half.X && MathF.Abs(at.Y - t.Centre.Y) < t.Half.Y)
                || spilled.Any(s => Vector2.Distance(s, at) < ball * 1.1f)
                || !b.Put(FurnitureType.PlayBall, at, 0f, random.Range(-MathF.PI, MathF.PI), tint: random.Next(FurniturePalettes.TintCount)))
                continue;
            spilled.Add(at);
        }
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
                    var fallenTurn = angle + Quarter + random.Range(-0.4f, 0.4f);
                    var fallenTint = Tint();
                    // Outside the ring it may stick out of the site's rectangle; then it lies inside the ring instead.
                    if (!b.Put(FurnitureType.Sofa, At(centre, angle, ringRadius + side * 2.2f), 0f, fallenTurn, stoneVariant, tint: fallenTint))
                        b.Put(FurnitureType.Sofa, At(centre, angle, ringRadius - side * 2.2f), 0f, fallenTurn, stoneVariant, tint: fallenTint);
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
            b.Put(FurnitureType.Armchair, At(centre, angle, 3.6f), 0f, angle + Quarter,
                FurnitureCatalog.PickVariant(random, FurnitureType.Armchair), tint: Tint());
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

    /// <summary>
    /// A pet-shop row of tanks: two rows of steel racks face each other across the aisle, medium tanks on their bottom shelves,
    /// small ones on the top and either on the middle; one or two large tanks on their own cabinets close the east end, with a
    /// bench to watch them from. Each tank gets its own kind of fish, in turn from a shuffled list along each shelf, so neighbours differ.
    /// </summary>
    private static void BuildAquariumAisle(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var rack = FurnitureCatalog.Spec(FurnitureType.TankRack);
        var small = FurnitureCatalog.Spec(FurnitureType.FishTank, 0);
        var medium = FurnitureCatalog.Spec(FurnitureType.FishTank, 1);
        var large = FurnitureCatalog.Spec(FurnitureType.FishTank, FurnitureCatalog.LargeFishTank);
        var kinds = Enum.GetValues<FishKind>().OrderBy(_ => random.Next(1 << 30)).ToArray();
        var next = 0;
        void Stock(FurnitureType type, Vector2 local, float elevation, float rotation, int variant)
        {
            if (b.Put(type, local, elevation, rotation, variant))
                b.Stock(kinds[next++ % kinds.Length]);
        }

        const int racksPerRow = 4;
        var west = -6f + 0.1f;
        var middleMedium = Enumerable.Range(0, 2 * racksPerRow).Select(_ => random.Chance(0.5f)).ToArray();
        foreach (var (y, rotation, row) in new[] { (-3f + rack.Depth / 2f + 0.05f, 0f, 0), (3f - rack.Depth / 2f - 0.05f, MathF.PI, 1) })
        {
            for (var i = 0; i < racksPerRow; i++)
                b.Put(FurnitureType.TankRack, new Vector2(west + rack.Width * (i + 0.5f), y), 0f, rotation);
            for (var level = 0; level < FurnitureCatalog.TankRackLevels.Count; level++)
                for (var i = 0; i < racksPerRow; i++)
                {
                    var useMedium = level == 0 || (level == 1 && middleMedium[row * racksPerRow + i]);
                    var spec = useMedium ? medium : small;
                    var count = (int)(rack.Width / (spec.Width + 0.04f));
                    var step = rack.Width / count;
                    for (var t = 0; t < count; t++)
                        Stock(FurnitureType.FishTank, new Vector2(west + rack.Width * i + step * (t + 0.5f), y),
                            FurnitureCatalog.TankRackLevels[level], rotation, spec.Variant);
                }
        }

        var x = 6f - large.Depth / 2f - 0.1f;
        var ys = random.Chance(0.5f) ? new[] { 0f } : new[] { -large.Width / 2f - 0.1f, large.Width / 2f + 0.1f };
        foreach (var y in ys)
            Stock(FurnitureType.FishTank, new Vector2(x, y), 0f, Quarter, FurnitureCatalog.LargeFishTank);
        b.Put(FurnitureType.HallBench, new Vector2(x - 2.4f + random.Range(-0.2f, 0.2f), 0f), 0f, -Quarter);
    }

    // The length of a Cafeteria serving line, from its tray station to its last high chair.
    private const float ServingLineLength = 18.6f;
    // The radius of a Cafeteria's round dining zones, chairs included.
    private const float DiningZoneRadius = 9.2f;

    /// <summary>
    /// A large self-service dining hall: a serving island down the middle, two serving lines back to back along it (each with trays,
    /// two hot food counters, a dessert counter, coffee, a till, then tray trolleys and high chairs, running the other way to the
    /// other line) with menu boards over their counters, and about it four round dining zones of tables in rings, left mid-meal.
    /// The seed picks which way the lines run, how the rings are turned and the tables (rectangular, round or long).
    /// </summary>
    private static void BuildCafeteria(PointOfInterestBuilder b)
    {
        var random = b.Random;
        var half = OwnSize(PointOfInterestType.Cafeteria) / 2f;
        // The island runs north–south between the west and the east dining zones; one line faces east, the other west.
        var south = random.Chance(0.5f) ? 1f : -1f;
        foreach (var side in new[] { 1f, -1f })
        {
            var (u, n) = (new Vector2(0f, south * side), new Vector2(side, 0f));
            var facing = MathF.Atan2(-n.X, n.Y);
            Vector2 Along(float s, float d) => u * (s - ServingLineLength / 2f) + n * d;
            bool Line(FurnitureType type, float s, float d, float elevation = 0f) => b.Put(type, Along(s, d), elevation, facing);

            Line(FurnitureType.TrayStation, 1.6f, 0.5f);
            foreach (var (type, s, meal) in new[]
            {
                (FurnitureType.HotFoodCounter, 3.25f, MealKind.Meatballs),
                (FurnitureType.HotFoodCounter, 5.3f, MealKind.Meatballs),
                (FurnitureType.DessertCounter, 7.35f, MealKind.AlmondCake),
            })
            {
                // The board stands on the island's spine, behind its counter.
                Line(FurnitureType.MenuBoard, s, 0.08f);
                if (Line(type, s, 0.55f))
                    b.Serve(meal);
                Line(FurnitureType.TrayRail, s, 1.145f);
            }
            Line(FurnitureType.Counter, 9.4f, 0.5f);
            Line(FurnitureType.CoffeeMachine, 8.95f, 0.45f, FurnitureCatalog.Spec(FurnitureType.Counter).Height);
            Line(FurnitureType.Till, 12f, 0.5f);
            var trolleys = 2 + random.Next(2);
            for (var i = 0; i < trolleys; i++)
                Line(FurnitureType.TrayTrolley, 14f + 0.9f * i, 0.53f);
            var highChairs = 2 + random.Next(2);
            for (var i = 0; i < highChairs; i++)
                Line(FurnitureType.HighChair, 16.8f + 0.7f * i, 0.6f);
        }

        // The dining zones: one in each corner of the hall, rings of tables about its middle, each table along its ring with
        // its chairs inside and outside it; an innermost ring too small for its tables is one table in the middle.
        var tableVariant = random.Next(3);
        var table = FurnitureCatalog.Spec(FurnitureType.DiningTable, tableVariant);
        var sideSeats = tableVariant == 1 ? 1 : 2;
        var reach = table.Depth / 2f + 0.8f;
        var zones = new List<Vector2>();
        var places = new List<(Vector2 At, float Rotation)>();
        foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
        {
            var zone = corner * (half - new Vector2(DiningZoneRadius + 0.6f, DiningZoneRadius + 0.4f));
            zones.Add(zone);
            for (var r = DiningZoneRadius - reach; r > 0f; r -= table.Depth + 2.2f)
            {
                if (r < 0.8f * table.Width)
                {
                    places.Add((zone, random.Range(0f, MathF.PI)));
                    break;
                }
                var count = (int)MathF.Floor(2f * MathF.PI * r / (table.Width + 1f));
                var phase = random.Range(0f, 2f * MathF.PI);
                for (var i = 0; i < count; i++)
                {
                    var angle = phase + 2f * MathF.PI * i / count;
                    places.Add((zone + r * new Vector2(MathF.Cos(angle), MathF.Sin(angle)), angle - Quarter + random.Range(-0.06f, 0.06f)));
                }
            }
        }
        var abandoned = random.Next(places.Count);
        for (var i = 0; i < places.Count; i++)
        {
            var (at, rotation) = places[i];
            b.Put(FurnitureType.DiningTable, at, 0f, rotation, tableVariant);
            LeftSeats(b, at, rotation, tableVariant, sideSeats, forced: i == abandoned);
        }

        var spills = 2 + random.Next(3);
        for (var i = 0; i < spills; i++)
        {
            var angle = random.Range(0f, 2f * MathF.PI);
            var at = zones[random.Next(zones.Count)] + random.Range(0f, DiningZoneRadius - 1f) * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            b.Put(FurnitureType.Spill, at, 0f, random.Range(0f, 2f * MathF.PI));
        }
        var notes = 1 + random.Next(2);
        for (var i = 0; i < notes; i++)
        {
            var (at, rotation) = places[random.Next(places.Count)];
            b.Put(FurnitureType.Note, at + Rotated(new Vector2(random.Range(-0.3f, 0.3f), random.Range(-0.15f, 0.15f)), rotation), table.Height,
                random.Range(0f, 2f * MathF.PI));
        }
    }

    /// <summary>A local offset turned as a piece with this rotation is.</summary>
    private static Vector2 Rotated(Vector2 v, float rotation) =>
        new(v.X * MathF.Cos(rotation) - v.Y * MathF.Sin(rotation), v.X * MathF.Sin(rotation) + v.Y * MathF.Cos(rotation));

    /// <summary>
    /// The chairs of a Cafeteria table (turned by <paramref name="turn"/>) left mid-meal: some pushed back, some knocked over onto
    /// their backs, and perhaps a meal left on the table before one of them. A forced table always has a knocked-over chair and a meal left.
    /// </summary>
    private static void LeftSeats(PointOfInterestBuilder b, Vector2 table, float turn, int variant, int sideSeats, bool forced)
    {
        var random = b.Random;
        var spec = FurnitureCatalog.Spec(FurnitureType.DiningTable, variant);
        var edge = spec.Depth / 2f + 0.27f;
        var left = forced || random.Chance(0.45f);
        var mealSeat = forced ? 0 : random.Next(2 * sideSeats);
        var seat = 0;
        for (var i = 0; i < sideSeats; i++)
        {
            var x = (i - (sideSeats - 1) / 2f) * (spec.Width / sideSeats);
            foreach (var side in new[] { -1f, 1f })
            {
                // In the table's frame the north chair (side -1) faces south (0), the south chair north (π).
                var rotation = turn + (side < 0f ? 0f : MathF.PI);
                var offset = new Vector2(x, side * edge);
                var tilt = 0f;
                if ((forced && seat == 0) || random.Chance(0.12f))
                    tilt = -Quarter;
                else if (random.Chance(0.3f))
                {
                    offset.Y += side * 0.25f;
                    rotation += random.Range(-0.35f, 0.35f);
                }
                else
                    rotation += random.Range(-0.08f, 0.08f);
                b.Put(FurnitureType.Chair, table + Rotated(offset, turn), 0f, rotation, tilt: tilt);
                if (left && seat == mealSeat)
                {
                    var plate = table + Rotated(new Vector2(x, side * 0.24f), turn);
                    b.Put(FurnitureType.MealPlate, plate, spec.Height, turn);
                }
                seat++;
            }
        }
    }

}

/// <summary>Places the Furniture of one Point of interest in its own frame, keeping every piece inside its rectangle.</summary>
internal sealed class PointOfInterestBuilder(StoreSpace space, PointOfInterestSite site, SeededRandom random)
{
    private readonly List<FurniturePiece> _pieces = [];

    public SeededRandom Random { get; } = random;

    public IReadOnlyList<FurniturePiece> Pieces => _pieces;

    /// <summary>The fish stocked so far, by index into <see cref="Pieces"/> of the tank that holds them.</summary>
    public List<(int PieceIndex, FishKind Kind)> Fish { get; } = [];

    /// <summary>Stocks the last piece placed, a tank, with live fish of one kind.</summary>
    public void Stock(FishKind kind) => Fish.Add((_pieces.Count - 1, kind));

    /// <summary>The meals served so far, by index into <see cref="Pieces"/> of the counter that holds them.</summary>
    public List<(int PieceIndex, MealKind Kind)> Meals { get; } = [];

    /// <summary>Serves a meal on the last piece placed, a serving counter.</summary>
    public void Serve(MealKind kind) => Meals.Add((_pieces.Count - 1, kind));

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
            if (MathF.Abs(corner.X - site.Centre.X) > half.X + 1e-4f || MathF.Abs(corner.Y - site.Centre.Y) > half.Y + 1e-4f)
                return false;
        _pieces.Add(piece with { Position = space.Wrap(piece.Position) });
        return true;
    }
}
