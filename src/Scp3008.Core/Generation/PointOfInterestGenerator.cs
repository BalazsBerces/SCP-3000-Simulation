using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Where a Point of interest stands, chosen before any Furniture is placed.</summary>
/// <param name="Quarters">Quarter turns from the Point of interest's own frame (its width east-west) to the Store.</param>
internal sealed record PointOfInterestSite(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size, int Quarters);

/// <summary>The built Points of interest and their Furniture.</summary>
internal sealed record PointOfInterestLayout(IReadOnlyList<PointOfInterest> PointsOfInterest, IReadOnlyList<FurniturePiece> Pieces);

/// <summary>
/// Places and builds the common Points of interest from the seed: the canteens (in Restaurant/food market
/// Departments) and the cart graveyards, then, Sector by Sector, the dinner tables, restrooms and the lost and
/// found desk. Each stands on a rectangle inside one department cell of its Sector, turned by a quarter turn,
/// clear of the Pillars and their walkway rings, the Landmarks and each other; the Displays then keep off it.
/// Each is built from its own random stream as individual Furniture.
/// </summary>
internal static class PointOfInterestGenerator
{
    private const int PlanStream = 6;
    private const int BuildStream = 7;

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
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static IReadOnlyList<PointOfInterestSite> Plan(int seed, StoreSpace space, DepartmentMap departments, IReadOnlyList<Sector> sectors,
        IReadOnlyList<Pillar> pillars, IReadOnlyList<LandmarkSite> landmarks)
    {
        var random = SeededRandom.For(seed, PlanStream);
        var cellSize = departments.CellSize;
        var sites = new List<PointOfInterestSite>();

        void Place(PointOfInterestType type, int firstCellX, int firstCellY, int cellsX, int cellsY, Func<int, int, bool> allowed)
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
                return;
            }
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
        return sites.AsReadOnly();
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
            }
            foreach (var piece in builder.Pieces)
                pieces.Add(piece with { Id = firstPieceId + pieces.Count });
            pois.Add(new PointOfInterest(site.Id, site.Type, site.SectorId, site.Centre, site.Size));
        }
        return new PointOfInterestLayout(pois.AsReadOnly(), pieces.AsReadOnly());
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
