using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Where a Landmark stands, chosen before any Furniture is placed.</summary>
/// <param name="Quarters">Quarter turns from the Landmark's own frame (its length east-west) to the Store.</param>
internal sealed record LandmarkSite(int Id, LandmarkType Type, int SectorId, Vector2 Centre, Vector2 Size, int Quarters);

/// <summary>The built Landmarks and their Furniture.</summary>
internal sealed record LandmarkLayout(IReadOnlyList<Landmark> Landmarks, IReadOnlyList<FurniturePiece> Pieces);

/// <summary>
/// Places and builds the Landmarks from the seed. Every world has the ruined Village; each other
/// Landmark is in it with a fixed chance. Each takes its own Sector, the Pillar-less Sectors first
/// (in an order picked by the seed), then the Sectors with a Pillar, and stands on a rectangle of
/// whole 16 m department cells inside the Sector, a cell away from its edges, from the Pillar's
/// walkway ring, and no more than a quarter of it. Each Landmark is then built from its own random
/// stream as individual Furniture, stacked and tipped over (see <see cref="FurniturePiece.Tilt"/>).
/// </summary>
internal static class LandmarkGenerator
{
    private const int PlanStream = 4;
    private const int BuildStream = 5;
    private const float OtherLandmarkChance = 2f / 3f;

    private static readonly LandmarkType[] Others =
    [
        LandmarkType.RackCanyon, LandmarkType.BoxFort, LandmarkType.MattressMountain,
        LandmarkType.DeadZone, LandmarkType.ClockWall, LandmarkType.FountainCourt,
    ];

    /// <summary>The size of each Landmark in its own frame: sized for its idea, read from far away, and in whole cells.</summary>
    private static Vector2 OwnSize(LandmarkType type) => type switch
    {
        LandmarkType.RuinedVillage => new(96f, 96f),
        LandmarkType.RackCanyon => new(160f, 112f),
        LandmarkType.BoxFort => new(80f, 80f),
        LandmarkType.MattressMountain => new(80f, 80f),
        LandmarkType.DeadZone => new(112f, 112f),
        LandmarkType.ClockWall => new(128f, 32f),
        LandmarkType.FountainCourt => new(96f, 96f),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static IReadOnlyList<LandmarkSite> Plan(int seed, StoreSpace space, IReadOnlyList<Sector> sectors, IReadOnlyList<Pillar> pillars, float cellSize)
    {
        var random = SeededRandom.For(seed, PlanStream);
        var kinds = new List<LandmarkType> { LandmarkType.RuinedVillage };
        foreach (var other in Others)
            if (random.Chance(OtherLandmarkChance))
                kinds.Add(other);
        Shuffle(random, kinds);
        while (kinds.Count > sectors.Count)
            kinds.Remove(kinds.Last(k => k != LandmarkType.RuinedVillage));

        bool HasPillar(Sector sector) => pillars.Any(p => p.Coordinate == sector.Name);
        var pillarLess = sectors.Where(s => !HasPillar(s)).ToList();
        var withPillar = sectors.Where(HasPillar).ToList();
        Shuffle(random, pillarLess);
        Shuffle(random, withPillar);
        var order = pillarLess.Concat(withPillar).ToList();

        var sites = new List<LandmarkSite>();
        foreach (var (type, sector) in kinds.Zip(order))
        {
            var quarters = random.Next(4);
            var own = OwnSize(type);
            var size = (quarters & 1) == 0 ? own : new Vector2(own.Y, own.X);
            if (size.X * size.Y > sector.Size * sector.Size / 4f)
                throw new InvalidOperationException($"The {type} is more than a quarter of Sector {sector.Name}.");
            var candidates = new List<Vector2>();
            var cells = (int)MathF.Round(sector.Size / cellSize);
            for (var y = 1; y + size.Y / cellSize <= cells - 1; y++)
                for (var x = 1; x + size.X / cellSize <= cells - 1; x++)
                {
                    var centre = sector.Origin + new Vector2(x, y) * cellSize + size / 2f;
                    if (pillars.All(p => Clear(space.Delta(p.Position, centre), p.ClearHalfWidth + cellSize, size / 2f)))
                        candidates.Add(centre);
                }
            if (candidates.Count == 0)
                throw new InvalidOperationException($"No room for the {type} in Sector {sector.Name}.");
            sites.Add(new LandmarkSite(sites.Count, type, sector.Id, random.Pick(candidates), size, quarters));
        }
        return sites.AsReadOnly();
    }

    /// <summary>Gap between neighbouring Landmarks in the showcase row.</summary>
    private const float ShowcaseGap = 32f;

    /// <summary>
    /// The Landmark showcase: every Landmark type once, unturned, side by side along x in a row centred
    /// on the Store, on the cell grid, on the row nearest the middle of the Store that keeps clear of every Pillar.
    /// </summary>
    public static IReadOnlyList<LandmarkSite> Showcase(StoreSpace space, IReadOnlyList<Sector> sectors, IReadOnlyList<Pillar> pillars, float cellSize)
    {
        var kinds = new[] { LandmarkType.RuinedVillage }.Concat(Others).ToList();
        var length = kinds.Sum(k => OwnSize(k).X) + ShowcaseGap * (kinds.Count - 1);
        if (length > space.Size)
            throw new InvalidOperationException($"The showcase row ({length:0} m) is longer than the Store.");
        var start = MathF.Round((space.Size - length) / 2f / cellSize) * cellSize;
        var deepest = kinds.Max(k => OwnSize(k).Y);
        var rows = new List<float>();
        for (var y = deepest / 2f; y + deepest / 2f <= space.Size; y += cellSize)
            rows.Add(y);
        foreach (var y in rows.OrderBy(y => MathF.Abs(y - space.Size / 2f)))
        {
            var sites = new List<LandmarkSite>();
            var x = start;
            foreach (var type in kinds)
            {
                var size = OwnSize(type);
                var centre = new Vector2(x + size.X / 2f, y);
                var sector = sectors.First(s => centre.X >= s.Origin.X && centre.X < s.Origin.X + s.Size &&
                                                centre.Y >= s.Origin.Y && centre.Y < s.Origin.Y + s.Size);
                sites.Add(new LandmarkSite(sites.Count, type, sector.Id, centre, size, 0));
                x += size.X + ShowcaseGap;
            }
            if (sites.All(site => pillars.All(p => Clear(space.Delta(p.Position, site.Centre), p.ClearHalfWidth + cellSize, site.Size / 2f))))
                return sites.AsReadOnly();
        }
        throw new InvalidOperationException("No row of the Store keeps the showcase clear of the Pillars.");
    }

    public static LandmarkLayout Build(int seed, StoreSpace space, IReadOnlyList<LandmarkSite> sites, int firstPieceId)
    {
        var landmarks = new List<Landmark>();
        var pieces = new List<FurniturePiece>();
        foreach (var site in sites)
        {
            var builder = new LandmarkBuilder(space, site, SeededRandom.For(seed, BuildStream, site.Id));
            switch (site.Type)
            {
                case LandmarkType.RuinedVillage: BuildRuinedVillage(builder); break;
                case LandmarkType.RackCanyon: BuildRackCanyon(builder); break;
                case LandmarkType.BoxFort: BuildBoxFort(builder); break;
                case LandmarkType.MattressMountain: BuildMattressMountain(builder); break;
                case LandmarkType.DeadZone: BuildDeadZone(builder); break;
                case LandmarkType.ClockWall: BuildClockWall(builder); break;
                case LandmarkType.FountainCourt: BuildFountainCourt(builder); break;
            }
            var first = pieces.Count;
            foreach (var piece in builder.Pieces)
                pieces.Add(piece with { Id = firstPieceId + pieces.Count });
            var landmark = new Landmark(site.Id, site.Type, site.SectorId, site.Centre, site.Size, builder.Top, builder.Lookouts.AsReadOnly());
            if (site.Type == LandmarkType.ClockWall)
                landmark = SetClocks(landmark, pieces.Skip(first).ToList(), builder.Random);
            landmarks.Add(landmark);
        }
        return new LandmarkLayout(landmarks.AsReadOnly(), pieces.AsReadOnly());
    }

    /// <summary>Whether a rectangle of the given half size, this far from a square's centre, stays out of the square.</summary>
    private static bool Clear(Vector2 delta, float squareHalfWidth, Vector2 halfSize) =>
        MathF.Abs(delta.X) >= squareHalfWidth + halfSize.X || MathF.Abs(delta.Y) >= squareHalfWidth + halfSize.Y;

    private static void Shuffle<T>(SeededRandom random, List<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    // --- The ruined Village: a fallen Furniture castle ---

    private static void BuildRuinedVillage(LandmarkBuilder b)
    {
        var random = b.Random;
        const float wallLine = 38f;
        const int fullCourses = 5;
        // Broken curtain walls: two rows of tall Furniture stacked in courses, worn down to stumps and breached.
        for (var side = 0; side < 4; side++)
        {
            var (phase, phase2) = (random.Range(0f, MathF.Tau), random.Range(0f, MathF.Tau));
            var breaches = Enumerable.Range(0, 2).Select(_ => (Centre: random.Range(-26f, 26f), Half: random.Range(2.5f, 4.5f))).ToArray();
            for (var s = -32.8f; s <= 32.81f; s += 1.6f)
            {
                var height = 2.6f + 1.6f * MathF.Sin(s * 0.09f + phase) + 1.1f * MathF.Sin(s * 0.23f + phase2) + random.Range(-0.8f, 0.8f);
                var courses = Math.Clamp((int)MathF.Round(height), 0, fullCourses);
                if (breaches.Any(br => MathF.Abs(s - br.Centre) < br.Half))
                    courses = Math.Min(courses, random.Next(2));
                if (side == 2 && MathF.Abs(s) < 2.4f)
                    courses = 0;
                foreach (var row in new[] { -1, 1 })
                    WallColumn(b, Side(side, s, wallLine + row * 0.3f), SideHeading(side, row), courses, random.Chance(0.4f));
                // What has fallen off the wall lies at its foot.
                for (var fallen = fullCourses - courses; fallen > 0; fallen--)
                    if (random.Chance(0.3f))
                        Rubble(b, Side(side, s + random.Range(-1f, 1f), wallLine + random.Range(-7f, 7f)));
            }
        }

        // Toppled corner towers: a stump at each corner and a trail of the Furniture it fell into.
        foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
        {
            var centre = corner * wallLine;
            for (var side = 0; side < 4; side++)
                for (var s = -2.4f; s <= 2.41f; s += 1.6f)
                    WallColumn(b, centre + Side(side, s, 3.2f), SideHeading(side, 1), random.Next(3), tiltTop: true);
            var fall = Vector2.Transform(-Vector2.Normalize(corner), Matrix3x2.CreateRotation(random.Range(-0.7f, 0.7f)));
            var across = new Vector2(-fall.Y, fall.X);
            var heading = MathF.Atan2(-fall.X, fall.Y);
            for (var t = 5f; t <= 20f; t += 0.9f)
                for (var n = random.Next(2) + 1; n > 0; n--)
                {
                    var (type, variant) = WallMaterial(random);
                    b.Rest(type, centre + fall * t + across * random.Range(-2.5f, 2.5f), random.Next(3) * 0.6f,
                        heading + random.Range(-0.35f, 0.35f), MathF.PI / 2f + random.Range(-0.3f, 0.3f), variant, random.Next(FurniturePalettes.TintCount));
                }
        }

        // The stump of the keep: a jagged square tower, still higher on one side than anything around.
        var tallSide = random.Next(4);
        for (var side = 0; side < 4; side++)
            for (var s = -6.4f; s <= 6.41f; s += 1.6f)
            {
                var courses = side == tallSide
                    ? 10 + random.Next(2)
                    : Math.Clamp(6 + random.Next(4) - (int)(MathF.Abs(s) / 3f), 3, 9);
                foreach (var row in new[] { -1, 1 })
                    WallColumn(b, Side(side, s, 7f + row * 0.3f), SideHeading(side, row), courses, random.Chance(0.5f),
                        wardrobesOnly: side == tallSide);
            }
        for (var i = 0; i < 90; i++)
        {
            var angle = random.Range(0f, MathF.Tau);
            Rubble(b, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * random.Range(8.5f, 15f));
        }

        // What is left of the Village in the courtyard: its Furniture overturned and scattered.
        FurnitureType[] remains = [FurnitureType.Bed, FurnitureType.Sofa, FurnitureType.DiningTable, FurnitureType.Chair,
            FurnitureType.KitchenCabinet, FurnitureType.FlatPackBox, FurnitureType.Dresser, FurnitureType.Bookcase];
        for (var i = 0; i < 140; i++)
        {
            var at = new Vector2(random.Range(-33f, 33f), random.Range(-33f, 33f));
            if (MathF.Abs(at.X) < 10f && MathF.Abs(at.Y) < 10f)
                continue;
            var type = random.Pick(remains);
            float[] tilts = [0f, 0f, MathF.PI / 2f, -MathF.PI / 2f, MathF.PI];
            b.Rest(type, at, 0f, random.Range(-MathF.PI, MathF.PI), random.Pick(tilts) + random.Range(-0.15f, 0.15f),
                random.Next(FurnitureCatalog.VariantCount(type)), random.Next(FurniturePalettes.TintCount));
        }
    }

    /// <summary>One column of a Village wall: tall Furniture stacked in courses, the top one sometimes leaning.</summary>
    private static void WallColumn(LandmarkBuilder b, Vector2 at, float heading, int courses, bool tiltTop, bool wardrobesOnly = false)
    {
        var random = b.Random;
        var elevation = 0f;
        for (var course = 0; course < courses; course++)
        {
            var (type, variant) = wardrobesOnly ? (FurnitureType.Wardrobe, 1) : WallMaterial(random);
            var top = course == courses - 1;
            var tilt = top && tiltTop ? random.Range(0.12f, 0.5f) * (random.Chance(0.5f) ? 1f : -1f) : 0f;
            b.Put(type, at + new Vector2(random.Range(-0.05f, 0.05f), random.Range(-0.05f, 0.05f)), elevation,
                heading + random.Range(-0.05f, 0.05f), tilt, variant, random.Next(FurniturePalettes.TintCount));
            elevation += FurnitureCatalog.Spec(type, variant).Height;
        }
    }

    private static (FurnitureType Type, int Variant) WallMaterial(SeededRandom random) => random.NextFloat() switch
    {
        < 0.55f => (FurnitureType.Wardrobe, 1),
        < 0.7f => (FurnitureType.Wardrobe, 0),
        < 0.8f => (FurnitureType.Bookcase, 0),
        < 0.9f => (FurnitureType.Fridge, 0),
        _ => (FurnitureType.Dresser, 0),
    };

    private static void Rubble(LandmarkBuilder b, Vector2 at)
    {
        var random = b.Random;
        var (type, variant) = WallMaterial(random);
        var tilt = random.Chance(0.7f) ? MathF.PI / 2f * (random.Chance(0.5f) ? 1f : -1f) + random.Range(-0.3f, 0.3f) : random.Range(-0.6f, 0.6f);
        b.Rest(type, at, random.Chance(0.25f) ? 0.6f : 0f, random.Range(-MathF.PI, MathF.PI), tilt, variant, random.Next(FurniturePalettes.TintCount));
    }

    /// <summary>A point on one side of a square around the frame's centre: side 0 north, 1 east, 2 south, 3 west.</summary>
    private static Vector2 Side(int side, float along, float distance) => side switch
    {
        0 => new Vector2(along, -distance),
        1 => new Vector2(distance, along),
        2 => new Vector2(-along, distance),
        _ => new Vector2(-distance, -along),
    };

    /// <summary>The heading of a piece on a side of a square whose front faces out (row 1) or in (row -1).</summary>
    private static float SideHeading(int side, int row) => side * MathF.PI / 2f + (row > 0 ? MathF.PI : 0f);

    // --- The rack canyon: collapsed warehouse racks in a winding maze ---

    private static void BuildRackCanyon(LandmarkBuilder b)
    {
        var random = b.Random;
        const int columns = 26, rows = 18;
        const float cell = 6f;
        var origin = new Vector2(-columns * cell / 2f, -rows * cell / 2f);
        var open = Maze(columns, rows, (_, _) => true, random);
        var entrance = random.Next(rows);
        var exit = random.Next(rows);
        var canyon = Path(columns, rows, open, entrance * columns, exit * columns + columns - 1);

        // Every wall of the maze: between two cells, or on the boundary (cell -1).
        var walls = new List<(Vector2 Mid, bool AlongX, bool Boundary, bool Canyon)>();
        for (var y = 0; y < rows; y++)
            for (var x = 0; x <= columns; x++)
            {
                var (west, east) = (x > 0 ? y * columns + x - 1 : -1, x < columns ? y * columns + x : -1);
                if (west >= 0 && east >= 0 && open.Contains((west, east)))
                    continue;
                if ((x == 0 && y == entrance) || (x == columns && y == exit))
                    continue;
                walls.Add((origin + new Vector2(x * cell, (y + 0.5f) * cell), false, west < 0 || east < 0,
                    canyon.Contains(west) || canyon.Contains(east)));
            }
        for (var y = 0; y <= rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var (north, south) = (y > 0 ? (y - 1) * columns + x : -1, y < rows ? y * columns + x : -1);
                if (north >= 0 && south >= 0 && open.Contains((north, south)))
                    continue;
                walls.Add((origin + new Vector2((x + 0.5f) * cell, y * cell), true, north < 0 || south < 0,
                    canyon.Contains(north) || canyon.Contains(south)));
            }

        // High lookouts on the canyon walls, spread along it.
        var canyonWalls = walls.Where(w => w.Canyon && !w.Boundary).ToList();
        var lookouts = Enumerable.Range(0, 3).Select(i => canyonWalls[(int)((i + 0.5f) / 3f * canyonWalls.Count)].Mid).ToHashSet();

        foreach (var (mid, alongX, boundary, onCanyon) in walls)
        {
            var heading = alongX ? 0f : MathF.PI / 2f;
            var along = alongX ? Vector2.UnitX : Vector2.UnitY;
            var normal = alongX ? Vector2.UnitY : Vector2.UnitX;
            var lookout = lookouts.Contains(mid);
            if (!boundary && !lookout && random.Chance(0.1f))
            {
                CollapsedWall(b, mid, heading, along);
                continue;
            }
            foreach (var offset in new[] { -1.4f, 1.4f })
            {
                var stack = lookout ? 5
                    : boundary ? 2 + random.Next(2)
                    : onCanyon ? 3 + random.Next(2)
                    : 1 + random.Next(3);
                RackStack(b, mid + along * offset, heading + (random.Chance(0.5f) ? MathF.PI : 0f), stack, tiltTop: !boundary && !lookout);
            }
            if (lookout)
                b.Lookouts.Add(new Vector3(mid, 5 * FurnitureCatalog.Spec(FurnitureType.PalletRack).Height));
            // Racks leaning on the wall from a passage: slopes to climb.
            if (!boundary && !lookout && random.Chance(0.14f))
            {
                var side = random.Chance(0.5f) ? 1f : -1f;
                var tilt = random.Range(0.35f, 0.6f);
                var reach = FurnitureCatalog.Spec(FurnitureType.PalletRack).Height * MathF.Sin(tilt);
                var front = -normal * side;
                b.Put(FurnitureType.PalletRack, mid + along * random.Range(-1.4f, 1.4f) + normal * side * (0.55f + reach - 0.3f), 0f,
                    MathF.Atan2(-front.X, front.Y), tilt);
            }
        }
    }

    /// <summary>A stack of pallet racks with boxes on their beams; the top rack of a tall stack may lean out.</summary>
    private static void RackStack(LandmarkBuilder b, Vector2 at, float heading, int count, bool tiltTop)
    {
        var random = b.Random;
        var height = FurnitureCatalog.Spec(FurnitureType.PalletRack).Height;
        for (var level = 0; level < count; level++)
        {
            var leaning = tiltTop && level == count - 1 && count >= 2 && random.Chance(0.3f);
            var tilt = leaning ? random.Range(0.15f, 0.5f) * (random.Chance(0.5f) ? 1f : -1f) : 0f;
            var rackHeading = heading + random.Range(-0.03f, 0.03f);
            b.Put(FurnitureType.PalletRack, at, level * height, rackHeading, tilt);
            if (leaning || level > 1)
                continue;
            foreach (var beam in FurnitureCatalog.PalletRackLevels)
                foreach (var side in new[] { -0.65f, 0.65f })
                    if (random.Chance(0.2f))
                        b.Put(FurnitureType.FlatPackBox, at + new Vector2(MathF.Cos(rackHeading), MathF.Sin(rackHeading)) * side,
                            level * height + beam, rackHeading + random.Range(-0.05f, 0.05f));
        }
    }

    /// <summary>A wall that came down: a rack lying across the gap, another half fallen, and spilled boxes.</summary>
    private static void CollapsedWall(LandmarkBuilder b, Vector2 mid, float heading, Vector2 along)
    {
        var random = b.Random;
        b.Rest(FurnitureType.PalletRack, mid + along * random.Range(-1f, 1f), 0f, heading + random.Range(-0.3f, 0.3f),
            MathF.PI / 2f * (random.Chance(0.5f) ? 1f : -1f));
        b.Rest(FurnitureType.PalletRack, mid + along * random.Range(-1.5f, 1.5f), 0f, heading + random.Range(-0.4f, 0.4f),
            random.Range(0.6f, 1.1f) * (random.Chance(0.5f) ? 1f : -1f));
        for (var i = random.Next(4) + 3; i > 0; i--)
            b.Rest(FurnitureType.FlatPackBox, mid + new Vector2(random.Range(-2.5f, 2.5f), random.Range(-2.5f, 2.5f)), 0f,
                random.Range(-MathF.PI, MathF.PI), random.Chance(0.3f) ? MathF.PI / 2f : 0f);
    }

    // --- The box fort: a cardboard castle with a maze inside ---

    private static void BuildBoxFort(LandmarkBuilder b)
    {
        var random = b.Random;
        const float box = 0.8f;
        const float wall = 32f;
        // Curtain walls with battlements and a gate in the south wall.
        for (var side = 0; side < 4; side++)
            for (var k = 0; k <= 80; k++)
            {
                var s = -wall + k * box;
                if (MathF.Abs(s) > wall - 2.4f || (side == 2 && MathF.Abs(s) < 1.7f))
                    continue;
                for (var layer = 0; layer < 6 + (k % 2 == 0 ? 1 : 0); layer++)
                    Box(b, Side(side, s, wall), layer * box);
            }
        // Corner towers.
        foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            BoxTower(b, corner * wall, 5, 18, door: false);
        // The keep.
        BoxTower(b, Vector2.Zero, 10, 30, door: true);

        // A maze of low box walls fills the courtyard; a few walls are left out, so it has loops.
        const int cells = 15;
        const float cellSize = 4f;
        var origin = new Vector2(-cells * cellSize / 2f);
        bool InKeep(int x, int y) => x is >= 6 and <= 8 && y is >= 6 and <= 8;
        var open = Maze(cells, cells, (x, y) => !InKeep(x, y), random);
        for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
            {
                if (InKeep(x, y))
                    continue;
                var here = y * cells + x;
                if (x + 1 < cells && !InKeep(x + 1, y) && !open.Contains((here, here + 1)) && !random.Chance(0.1f))
                    for (var i = 0; i < 5; i++)
                        for (var layer = 0; layer < 2; layer++)
                            Box(b, origin + new Vector2((x + 1) * cellSize, y * cellSize + (i + 0.5f) * box), layer * box);
                if (y + 1 < cells && !InKeep(x, y + 1) && !open.Contains((here, here + cells)) && !random.Chance(0.1f))
                    for (var i = 0; i < 5; i++)
                        for (var layer = 0; layer < 2; layer++)
                            Box(b, origin + new Vector2(x * cellSize + (i + 0.5f) * box, (y + 1) * cellSize), layer * box);
            }

        // Boxes that have tumbled down.
        for (var i = 0; i < 90; i++)
        {
            var at = Side(random.Next(4), random.Range(-36f, 36f), wall + random.Range(-3f, 6f));
            b.Rest(FurnitureType.CardboardBox, at, 0f, random.Range(-MathF.PI, MathF.PI), random.Range(-0.8f, 0.8f));
        }
    }

    /// <summary>A hollow square tower of boxes, so many wide and high, with battlements and maybe a door in its south side.</summary>
    private static void BoxTower(LandmarkBuilder b, Vector2 centre, int width, int layers, bool door)
    {
        const float box = 0.8f;
        for (var layer = 0; layer <= layers; layer++)
            for (var y = 0; y < width; y++)
                for (var x = 0; x < width; x++)
                {
                    if (x != 0 && y != 0 && x != width - 1 && y != width - 1)
                        continue;
                    if (layer == layers && (x + y) % 2 == 1)
                        continue;
                    if (door && y == width - 1 && Math.Abs(2 * x - (width - 1)) <= 1 && layer < 3)
                        continue;
                    Box(b, centre + (new Vector2(x, y) - new Vector2((width - 1) / 2f)) * box, layer * box);
                }
    }

    private static void Box(LandmarkBuilder b, Vector2 at, float elevation)
    {
        var random = b.Random;
        b.Put(FurnitureType.CardboardBox, at + new Vector2(random.Range(-0.03f, 0.03f), random.Range(-0.03f, 0.03f)), elevation,
            random.Range(-0.05f, 0.05f));
    }

    // --- The mattress mountain: a huge heap of mattresses with a lookout on top ---

    private static void BuildMattressMountain(LandmarkBuilder b)
    {
        var random = b.Random;
        const float radius = 34f, summit = 23.5f, top = 2.5f;
        static float Ground(float d) => d <= top ? summit : summit * (1f - MathF.Pow(MathF.Min(1f, (d - top) / (radius - top)), 1.4f));

        // Rings of mattresses down the slope from the summit, each lying on the slope, front downhill.
        for (var d = top; d < radius;)
        {
            var slope = MathF.Atan((Ground(d) - Ground(d + 0.5f)) / 0.5f);
            var count = (int)MathF.Ceiling(MathF.Tau * d / 1.15f);
            var phase = random.Range(0f, MathF.Tau);
            for (var k = 0; k < count; k++)
            {
                var angle = phase + (k + random.Range(-0.2f, 0.2f)) * MathF.Tau / count;
                var at = d + random.Range(-0.25f, 0.25f);
                var bed = random.Chance(0.02f);
                b.Put(bed ? FurnitureType.Bed : FurnitureType.Mattress, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * at,
                    Ground(at) + random.Range(-0.05f, 0.12f), angle - MathF.PI / 2f + random.Range(-0.2f, 0.2f),
                    slope + random.Range(-0.12f, 0.12f), tint: random.Next(FurniturePalettes.TintCount));
            }
            d += 1.6f * MathF.Cos(slope);
        }

        // The lookout: a flat pile of mattresses on the summit.
        var pile = summit;
        for (var i = 0; i < 5; i++)
        {
            b.Put(FurnitureType.Mattress, new Vector2(random.Range(-0.2f, 0.2f), random.Range(-0.2f, 0.2f)), pile,
                random.Range(-MathF.PI, MathF.PI), tint: random.Next(FurniturePalettes.TintCount));
            pile += FurnitureCatalog.Spec(FurnitureType.Mattress).Height;
        }
        b.Lookouts.Add(new Vector3(0f, 0f, pile));

        // Mattresses and beds slid down to its foot.
        for (var i = 0; i < 80; i++)
        {
            var angle = random.Range(0f, MathF.Tau);
            var type = random.Chance(0.15f) ? FurnitureType.Bed : FurnitureType.Mattress;
            b.Rest(type, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * random.Range(radius - 1f, radius + 3.5f), 0f,
                random.Range(-MathF.PI, MathF.PI), random.Chance(0.6f) ? 0f : random.Range(-0.7f, 0.7f), tint: random.Next(FurniturePalettes.TintCount));
        }
    }

    // --- The dead zone: towers of racks and showroom sets, abandoned in the dark ---

    private static void BuildDeadZone(LandmarkBuilder b)
    {
        var random = b.Random;
        const float spacing = 36f;
        // Towers of pallet racks on a grid, stacked up to five high: the dark shapes rising out of the black.
        for (var gy = -1; gy <= 1; gy++)
            for (var gx = -1; gx <= 1; gx++)
            {
                var centre = new Vector2(gx, gy) * spacing + new Vector2(random.Range(-2f, 2f), random.Range(-2f, 2f));
                var levels = gx == 0 && gy == 0 ? 5 : 4 + random.Next(2);
                var heading = random.Chance(0.5f) ? 0f : MathF.PI / 2f;
                var along = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                var across = new Vector2(-along.Y, along.X);
                foreach (var a in new[] { -1.4f, 1.4f })
                    foreach (var c in new[] { -0.56f, 0.56f })
                        RackStack(b, centre + along * a + across * c, heading + (c < 0f ? MathF.PI : 0f), levels, tiltTop: false);
            }

        // Showroom sets left standing in the dark, their Lamps never lit; some of their Furniture knocked over.
        var kind = random.Next(3);
        for (var gy = -1; gy <= 1; gy++)
            for (var gx = -1; gx <= 1; gx++)
            {
                if (gx == 0 && gy == 0)
                    continue;
                DarkSet(b, new Vector2(gx, gy) * spacing / 2f, random.Next(4) * MathF.PI / 2f + random.Range(-0.1f, 0.1f), kind++ % 3);
            }

        // Furniture strewn about the dark floor, kept clear of the towers.
        FurnitureType[] strewn = [FurnitureType.Chair, FurnitureType.Armchair, FurnitureType.FlatPackBox, FurnitureType.Nightstand,
            FurnitureType.CoffeeTable, FurnitureType.FloorLamp, FurnitureType.Bookcase, FurnitureType.Wardrobe];
        for (var placed = 0; placed < 90;)
        {
            var at = new Vector2(random.Range(-52f, 52f), random.Range(-52f, 52f));
            var nearest = new Vector2(MathF.Round(at.X / spacing), MathF.Round(at.Y / spacing)) * spacing;
            if (Vector2.Distance(at, nearest) < 7f)
                continue;
            var type = random.Pick(strewn);
            float[] tilts = [0f, MathF.PI / 2f, -MathF.PI / 2f];
            if (b.Rest(type, at, 0f, random.Range(-MathF.PI, MathF.PI), random.Pick(tilts) + random.Range(-0.1f, 0.1f),
                    random.Next(FurnitureCatalog.VariantCount(type)), random.Next(FurniturePalettes.TintCount)))
                placed++;
        }
    }

    /// <summary>One abandoned showroom set of the dead zone around a point: a living room, a bedroom or a dining room, each with Lamps.</summary>
    private static void DarkSet(LandmarkBuilder b, Vector2 centre, float heading, int kind)
    {
        var random = b.Random;
        var tint = random.Next(FurniturePalettes.TintCount);
        var turn = Matrix3x2.CreateRotation(heading);
        void Piece(FurnitureType type, float x, float y, float rotation, float elevation = 0f, int variant = 0)
        {
            var at = centre + Vector2.Transform(new Vector2(x, y), turn);
            if (elevation == 0f && random.Chance(0.15f))
                b.Rest(type, at + new Vector2(random.Range(-0.8f, 0.8f), random.Range(-0.8f, 0.8f)), 0f, heading + rotation + random.Range(-1f, 1f),
                    MathF.PI / 2f * (random.Chance(0.5f) ? 1f : -1f), variant, tint);
            else
                b.Put(type, at, elevation, heading + rotation + random.Range(-0.04f, 0.04f), 0f, variant, tint);
        }

        switch (kind)
        {
            case 0:
                Piece(FurnitureType.Rug, 0f, 0f, 0f);
                Piece(FurnitureType.Sofa, 0f, -2f, 0f, variant: 2);
                Piece(FurnitureType.Armchair, -2.2f, 0.2f, -MathF.PI / 2f);
                Piece(FurnitureType.Armchair, 2.2f, 0.2f, MathF.PI / 2f);
                Piece(FurnitureType.CoffeeTable, 0f, 0f, 0f);
                Piece(FurnitureType.FloorLamp, -1.9f, -2.3f, 0f);
                Piece(FurnitureType.FloorLamp, 1.9f, -2.3f, 0f);
                Piece(FurnitureType.Sideboard, 0f, 2.8f, MathF.PI);
                b.Put(FurnitureType.TableLamp, centre + Vector2.Transform(new Vector2(0.5f, 2.8f), turn),
                    FurnitureCatalog.SurfaceHeight(FurnitureType.Sideboard), heading);
                break;
            case 1:
                Piece(FurnitureType.Rug, 0f, 1.6f, 0f);
                Piece(FurnitureType.Bed, 0f, -0.6f, 0f);
                foreach (var side in new[] { -1.15f, 1.15f })
                {
                    Piece(FurnitureType.Nightstand, side, -1.45f, 0f);
                    b.Put(FurnitureType.TableLamp, centre + Vector2.Transform(new Vector2(side, -1.45f), turn),
                        FurnitureCatalog.SurfaceHeight(FurnitureType.Nightstand), heading);
                }
                Piece(FurnitureType.Wardrobe, 3f, 1.5f, MathF.PI / 2f, variant: 1);
                Piece(FurnitureType.FloorLamp, -2.6f, 1.8f, 0f);
                Piece(FurnitureType.Dresser, -3f, -0.5f, -MathF.PI / 2f);
                break;
            default:
                Piece(FurnitureType.Rug, 0f, 0f, 0f);
                Piece(FurnitureType.DiningTable, 0f, 0f, 0f, variant: 2);
                foreach (var x in new[] { -0.6f, 0.6f })
                {
                    Piece(FurnitureType.Chair, x, -0.8f, 0f);
                    Piece(FurnitureType.Chair, x, 0.8f, MathF.PI);
                }
                Piece(FurnitureType.Chair, -1.4f, 0f, -MathF.PI / 2f);
                Piece(FurnitureType.Chair, 1.4f, 0f, MathF.PI / 2f);
                Piece(FurnitureType.FloorLamp, -2.4f, -1.6f, 0f);
                Piece(FurnitureType.FloorLamp, 2.4f, 1.6f, 0f);
                Piece(FurnitureType.Sideboard, 0f, -2.6f, 0f);
                b.Put(FurnitureType.TableLamp, centre + Vector2.Transform(new Vector2(-0.5f, -2.6f), turn),
                    FurnitureCatalog.SurfaceHeight(FurnitureType.Sideboard), heading);
                break;
        }
    }

    // --- The clock wall: a long, high wall hung with thousands of clocks ---

    private static void BuildClockWall(LandmarkBuilder b)
    {
        var random = b.Random;
        const int columns = 75;
        const float column = 1.6f, half = columns * column / 2f;
        var wardrobe = FurnitureCatalog.Spec(FurnitureType.Wardrobe, 1);
        // The wall: two back-to-back rows of double wardrobes, stacked ten to twelve high, fronts out.
        for (var i = 0; i < columns; i++)
        {
            var x = -half + (i + 0.5f) * column;
            var courses = 10 + random.Next(3);
            foreach (var row in new[] { -1, 1 })
                for (var course = 0; course < courses; course++)
                    b.Put(FurnitureType.Wardrobe, new Vector2(x, row * wardrobe.Depth / 2f), course * wardrobe.Height,
                        row > 0 ? 0f : MathF.PI, variant: 1, tint: random.Next(FurniturePalettes.TintCount));
        }

        // The clocks: hung all over both faces up to the lowest course, of every size, each in its own spot.
        const float pitch = 1.5f, clockTop = 20f;
        foreach (var row in new[] { -1, 1 })
            for (var x = -half + pitch / 2f; x < half; x += pitch)
                for (var y = 0.3f; y + pitch <= clockTop; y += pitch)
                {
                    var variant = random.NextFloat() switch { < 0.4f => 0, < 0.65f => 3, < 0.9f => 1, _ => 2 };
                    if (variant == 0)
                    {
                        // Two small clocks share a spot, side by side.
                        foreach (var side in new[] { -1f, 1f })
                            HangClock(b, x + side * 0.4f, y + random.Range(0f, pitch - 0.6f), row, 0, wardrobe.Depth);
                        continue;
                    }
                    var size = FurnitureCatalog.Spec(FurnitureType.WallClock, variant).Width;
                    var slack = (pitch - size) / 2f;
                    HangClock(b, x + random.Range(-slack, slack), y + random.Range(0f, pitch - size), row, variant, wardrobe.Depth);
                }

        // Clocks and a few wardrobes fallen at the foot of the wall.
        for (var i = 0; i < 160; i++)
        {
            var side = random.Chance(0.5f) ? 1f : -1f;
            var at = new Vector2(random.Range(-half, half), side * random.Range(1.2f, 9f));
            b.Rest(FurnitureType.WallClock, at, 0f, random.Range(-MathF.PI, MathF.PI), MathF.PI / 2f * (random.Chance(0.7f) ? -1f : 1f),
                random.Next(FurnitureCatalog.VariantCount(FurnitureType.WallClock)));
        }
        for (var i = 0; i < 24; i++)
        {
            var side = random.Chance(0.5f) ? 1f : -1f;
            b.Rest(FurnitureType.Wardrobe, new Vector2(random.Range(-half, half), side * random.Range(2f, 12f)), 0f,
                random.Range(-0.4f, 0.4f) + (side > 0 ? 0f : MathF.PI), MathF.PI / 2f + random.Range(-0.1f, 0.1f), 1,
                random.Next(FurniturePalettes.TintCount));
        }
    }

    /// <summary>Hangs a clock on a face of the clock wall (row 1 south, -1 north), its face out, its bottom at a height.</summary>
    private static void HangClock(LandmarkBuilder b, float x, float bottom, int row, int variant, float wallHalfDepth)
    {
        var depth = FurnitureCatalog.Spec(FurnitureType.WallClock, variant).Depth;
        b.Put(FurnitureType.WallClock, new Vector2(x, row * (wallHalfDepth + depth / 2f + 0.005f)),
            bottom, row > 0 ? 0f : MathF.PI, variant: variant);
    }

    /// <summary>
    /// Gives every clock of the clock wall its own offset from the time left until Closing time, all different, and
    /// picks one clock on the wall to be the true clock, whose offset is zero.
    /// </summary>
    private static Landmark SetClocks(Landmark wall, IReadOnlyList<FurniturePiece> pieces, SeededRandom random)
    {
        var clocks = pieces.Where(p => p.Type == FurnitureType.WallClock).ToList();
        var hanging = clocks.Where(p => p.Tilt == 0f).ToList();
        var trueClock = random.Pick(hanging).Id;
        var used = new HashSet<long> { 0 };
        var offsets = new Dictionary<int, TimeSpan>();
        foreach (var clock in clocks)
        {
            if (clock.Id == trueClock)
            {
                offsets[clock.Id] = TimeSpan.Zero;
                continue;
            }
            long seconds;
            do
                seconds = 1 + random.Next(24 * 60 * 60 - 1);
            while (!used.Add(seconds));
            offsets[clock.Id] = TimeSpan.FromSeconds(seconds);
        }
        return wall with { TrueClockId = trueClock, ClockOffsets = offsets.AsReadOnly() };
    }

    // --- The fountain court: a fountain plaza around a tower of stone bowls ---

    private static void BuildFountainCourt(LandmarkBuilder b)
    {
        var random = b.Random;
        // The great fountain: stone bowls, from the widest up to the smallest, on columns, crowned with a slender column.
        var elevation = 0f;
        foreach (var bowl in new[] { 3, 2, 1, 1, 0, 0 })
        {
            b.Put(FurnitureType.FountainBowl, Vector2.Zero, elevation, 0f, variant: bowl);
            elevation += FurnitureCatalog.Spec(FurnitureType.FountainBowl, bowl).Height;
            var columnVariant = bowl == 0 && elevation > 19f ? 1 : 0;
            b.Put(FurnitureType.FountainColumn, Vector2.Zero, elevation, 0f, variant: columnVariant);
            elevation += FurnitureCatalog.Spec(FurnitureType.FountainColumn, columnVariant).Height;
        }

        // Its great basin: a ring of stone kerbs, with benches round it facing the fountain.
        Ring(b, FurnitureType.BasinKerb, 18f, 57, 0f);
        Ring(b, FurnitureType.HallBench, 21f, 24, MathF.PI / 48f);

        // Eight smaller fountains round it.
        for (var k = 0; k < 8; k++)
        {
            var angle = (k + 0.5f) * MathF.Tau / 8f;
            var at = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 28f;
            b.Put(FurnitureType.FountainBowl, at, 0f, angle, variant: 2);
            var e = FurnitureCatalog.Spec(FurnitureType.FountainBowl, 2).Height;
            b.Put(FurnitureType.FountainColumn, at, e, angle, variant: 1);
            b.Put(FurnitureType.FountainBowl, at, e + FurnitureCatalog.Spec(FurnitureType.FountainColumn, 1).Height, angle, variant: 0);
        }

        // A ring of planters, open where the paths in run along the axes.
        for (var k = 0; k < 40; k++)
        {
            var angle = (k + 0.5f) * MathF.Tau / 40f;
            if (MathF.Abs(MathF.Sin(2f * angle)) < 0.2f)
                continue;
            b.Put(FurnitureType.PlanterBox, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 34.5f, 0f, angle + MathF.PI / 2f,
                tint: random.Next(FurniturePalettes.TintCount));
        }

        // Four long reflecting pools at the sides, each with three small fountains.
        for (var side = 0; side < 4; side++)
        {
            var heading = side * MathF.PI / 2f;
            Pool(b, Side(side, 0f, 41f), heading, 15, 4f);
            foreach (var t in new[] { -9f, 0f, 9f })
                b.Put(FurnitureType.FountainBowl, Side(side, t, 41f), 0f, heading, variant: 0);
        }

        // Cafe tables under parasols between the pools, and big plants in the corners.
        for (var side = 0; side < 4; side++)
            foreach (var t in new[] { -28f, 28f })
            {
                var at = Side(side, t, 41f);
                var tint = random.Next(FurniturePalettes.TintCount);
                b.Put(FurnitureType.Parasol, at, 0f, random.Range(-MathF.PI, MathF.PI), tint: tint);
                b.Put(FurnitureType.GardenTable, at, 0f, random.Range(-MathF.PI, MathF.PI), variant: 1);
                for (var c = 0; c < 3; c++)
                {
                    var angle = random.Range(0f, MathF.Tau);
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    b.Put(FurnitureType.GardenChair, at + direction * 0.95f, 0f, MathF.Atan2(direction.X, -direction.Y), variant: 1);
                }
            }
        foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            for (var i = 0; i < 7; i++)
                b.Put(FurnitureType.LargePlant, corner * 43f + new Vector2(random.Range(-3.5f, 3.5f), random.Range(-3.5f, 3.5f)), 0f,
                    random.Range(-MathF.PI, MathF.PI), tint: random.Next(FurniturePalettes.TintCount));
    }

    /// <summary>Pieces in a ring round the frame's centre, their width along it and their fronts facing in.</summary>
    private static void Ring(LandmarkBuilder b, FurnitureType type, float radius, int count, float phase)
    {
        for (var k = 0; k < count; k++)
        {
            var angle = phase + k * MathF.Tau / count;
            b.Put(type, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, 0f, angle + MathF.PI / 2f);
        }
    }

    /// <summary>A long rectangular pool edged with stone kerbs: so many kerbs long and so wide inside.</summary>
    private static void Pool(LandmarkBuilder b, Vector2 centre, float heading, int length, float width)
    {
        var kerb = FurnitureCatalog.Spec(FurnitureType.BasinKerb);
        var turn = Matrix3x2.CreateRotation(heading);
        var halfLength = length * kerb.Width / 2f;
        for (var i = 0; i < length; i++)
            foreach (var row in new[] { -1, 1 })
                b.Put(FurnitureType.BasinKerb, centre + Vector2.Transform(new Vector2(-halfLength + (i + 0.5f) * kerb.Width, row * (width + kerb.Depth) / 2f), turn),
                    0f, heading + (row > 0 ? MathF.PI : 0f));
        var ends = (int)MathF.Ceiling(width / kerb.Width);
        foreach (var end in new[] { -1, 1 })
            for (var i = 0; i < ends; i++)
                b.Put(FurnitureType.BasinKerb, centre + Vector2.Transform(new Vector2(end * (halfLength - kerb.Depth / 2f), -width / 2f + (i + 0.5f) * width / ends), turn),
                    0f, heading + MathF.PI / 2f * end);
    }

    // --- Mazes ---

    /// <summary>
    /// A perfect maze over a grid of cells (those <paramref name="inside"/> it): the open passages
    /// between neighbouring cells, as (lower, higher) cell index pairs, cells row by row.
    /// </summary>
    private static HashSet<(int, int)> Maze(int columns, int rows, Func<int, int, bool> inside, SeededRandom random)
    {
        var open = new HashSet<(int, int)>();
        var visited = new bool[columns * rows];
        var start = Enumerable.Range(0, columns * rows).First(i => inside(i % columns, i / columns));
        var stack = new Stack<int>();
        stack.Push(start);
        visited[start] = true;
        var next = new List<int>(4);
        while (stack.Count > 0)
        {
            var here = stack.Peek();
            var (x, y) = (here % columns, here / columns);
            next.Clear();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                if (nx >= 0 && ny >= 0 && nx < columns && ny < rows && inside(nx, ny) && !visited[ny * columns + nx])
                    next.Add(ny * columns + nx);
            if (next.Count == 0)
            {
                stack.Pop();
                continue;
            }
            var there = random.Pick(next);
            visited[there] = true;
            open.Add((Math.Min(here, there), Math.Max(here, there)));
            stack.Push(there);
        }
        return open;
    }

    /// <summary>The cells on the way through a maze from one cell to another.</summary>
    private static HashSet<int> Path(int columns, int rows, HashSet<(int, int)> open, int from, int to)
    {
        var previous = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var here = queue.Dequeue();
            if (here == to)
                break;
            foreach (var there in new[] { here - 1, here + 1, here - columns, here + columns })
                if (there >= 0 && there < columns * rows && !previous.ContainsKey(there) && open.Contains((Math.Min(here, there), Math.Max(here, there))))
                {
                    previous[there] = here;
                    queue.Enqueue(there);
                }
        }
        var path = new HashSet<int> { to };
        for (var cell = to; cell != from; cell = previous[cell])
            path.Add(previous[cell]);
        return path;
    }
}

/// <summary>
/// Builds one Landmark in its own frame: local X along its length, the origin at its centre.
/// Pieces never sink into the floor, and a piece that would stick out of the Landmark's rectangle is left out.
/// </summary>
internal sealed class LandmarkBuilder(StoreSpace space, LandmarkSite site, SeededRandom random)
{
    private readonly List<FurniturePiece> _pieces = [];

    public SeededRandom Random { get; } = random;

    public IReadOnlyList<FurniturePiece> Pieces => _pieces;

    /// <summary>Lookouts in the Landmark's own frame until <see cref="Finish"/>... kept in Store coordinates.</summary>
    public LookoutList Lookouts { get; } = new(site);

    /// <summary>The top of the highest piece so far.</summary>
    public float Top { get; private set; }

    /// <summary>Places a piece with the centre of its base at a local position and elevation.</summary>
    public bool Put(FurnitureType type, Vector2 local, float elevation, float rotation, float tilt = 0f, int variant = 0, int tint = 0)
    {
        var turn = site.Quarters * MathF.PI / 2f;
        var piece = new FurniturePiece(0, type, site.Centre + Turn(local, site.Quarters), elevation,
            (float)Math.IEEERemainder(rotation + turn, 2.0 * Math.PI), variant, Tint: tint, LandmarkId: site.Id, Tilt: tilt);
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
        Top = MathF.Max(Top, corners.Max(c => c.Z));
        _pieces.Add(piece with { Position = space.Wrap(piece.Position) });
        return true;
    }

    /// <summary>Places a piece, however it is tipped over, so that its lowest point rests at a height and its bounds are centred on a local position.</summary>
    public bool Rest(FurnitureType type, Vector2 local, float bottom, float rotation, float tilt, int variant = 0, int tint = 0)
    {
        var corners = FurnitureCatalog.Corners(new FurniturePiece(0, type, Vector2.Zero, 0f, rotation, variant, Tilt: tilt));
        var centre = new Vector2(corners.Min(c => c.X) + corners.Max(c => c.X), corners.Min(c => c.Y) + corners.Max(c => c.Y)) / 2f;
        return Put(type, local - centre, bottom - corners.Min(c => c.Z), rotation, tilt, variant, tint);
    }

    internal static Vector2 Turn(Vector2 local, int quarters) => (quarters & 3) switch
    {
        0 => local,
        1 => new Vector2(-local.Y, local.X),
        2 => -local,
        _ => new Vector2(local.Y, -local.X),
    };

    /// <summary>The Landmark's lookouts, added in its own frame and kept in Store coordinates.</summary>
    internal sealed class LookoutList(LandmarkSite site) : List<Vector3>
    {
        public new void Add(Vector3 local)
        {
            var at = site.Centre + Turn(new Vector2(local.X, local.Y), site.Quarters);
            base.Add(new Vector3(at, local.Z));
        }
    }
}
