using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StoreGenerationTests
{
    private static WorldSnapshot Generate(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: seed, RuntimeRngSeed: 1));
        return sim.Snapshot;
    }

    private static DepartmentType[] DepartmentTypesByCell(DepartmentMap map)
    {
        var types = new DepartmentType[map.CellsPerSide * map.CellsPerSide];
        for (var y = 0; y < map.CellsPerSide; y++)
            for (var x = 0; x < map.CellsPerSide; x++)
                types[y * map.CellsPerSide + x] = map.AtCell(x, y).Type;
        return types;
    }

    [Fact]
    public void The_store_is_split_into_departments_of_all_eleven_types()
    {
        var departments = Generate(3008).Departments;

        Assert.Equal(Enum.GetValues<DepartmentType>().ToHashSet(), departments.All.Select(d => d.Type).ToHashSet());
        Assert.Equal(11, Enum.GetValues<DepartmentType>().Length);
    }

    [Fact]
    public void The_department_cells_cover_the_whole_store_floor()
    {
        var snapshot = Generate(3008);
        var map = snapshot.Departments;

        Assert.Equal(snapshot.Space.Size, map.CellSize * map.CellsPerSide);
        Assert.Equal(map.CellsPerSide * map.CellsPerSide, map.All.Sum(d => d.CellCount));
        Assert.All(map.All, d => Assert.Equal(d.CellCount * map.CellSize * map.CellSize, d.Area));
    }

    [Fact]
    public void Every_department_holds_its_anchor_and_is_found_by_position()
    {
        var map = Generate(3008).Departments;

        Assert.All(map.All, d => Assert.Same(d, map.At(d.Anchor)));
        Assert.All(map.All, d => Assert.Same(d, map.At(d.Anchor + new Vector2(map.CellSize * map.CellsPerSide, -map.CellSize * map.CellsPerSide))));
    }

    [Fact]
    public void Neighbouring_cells_of_the_same_type_belong_to_the_same_department()
    {
        var map = Generate(3008).Departments;

        for (var y = 0; y < map.CellsPerSide; y++)
            for (var x = 0; x < map.CellsPerSide; x++)
            {
                var here = map.AtCell(x, y);
                foreach (var neighbour in new[] { map.AtCell(x + 1, y), map.AtCell(x, y + 1) })
                    if (neighbour.Type == here.Type)
                        Assert.Same(here, neighbour);
            }
    }

    [Fact]
    public void The_same_seed_yields_the_same_department_layout()
    {
        var first = Generate(42).Departments;
        var second = Generate(42).Departments;

        Assert.Equal(DepartmentTypesByCell(first), DepartmentTypesByCell(second));
        Assert.Equal(first.All, second.All);
    }

    [Fact]
    public void Different_seeds_yield_different_department_layouts()
    {
        var first = DepartmentTypesByCell(Generate(42).Departments);
        var second = DepartmentTypesByCell(Generate(43).Departments);

        var differing = first.Zip(second).Count(pair => pair.First != pair.Second);
        Assert.True(differing > first.Length / 4, $"Only {differing} of {first.Length} cells differ.");
    }

    [Fact]
    public void The_store_is_filled_with_furniture_pieces_that_each_have_a_position_rotation_and_type()
    {
        var snapshot = Generate(3008);
        var furniture = snapshot.Furniture;

        Assert.True(furniture.Count > 10_000, $"Only {furniture.Count} pieces of Furniture.");
        Assert.Equal(furniture.Count, furniture.Select(p => p.Id).Distinct().Count());
        Assert.All(furniture, piece =>
        {
            Assert.True(Enum.IsDefined(piece.Type));
            Assert.Equal(snapshot.Space.Wrap(piece.Position), piece.Position);
            Assert.InRange(piece.Rotation, -MathF.PI, MathF.PI);
            Assert.True(piece.Elevation >= 0f);
        });
    }

    [Fact]
    public void Furniture_stands_at_free_positions_and_rotations_not_on_a_grid()
    {
        var furniture = Generate(3008).Furniture;

        var offAxis = furniture.Count(p => MathF.Abs(MathF.Sin(2f * p.Rotation)) > 0.05f);
        Assert.True(offAxis > furniture.Count / 100, $"Only {offAxis} pieces are turned off the grid axes.");
    }

    [Fact]
    public void Every_department_holds_furniture()
    {
        var snapshot = Generate(3008);
        var furnished = snapshot.Furniture.Select(p => snapshot.Departments.At(p.Position).Id).ToHashSet();

        Assert.All(snapshot.Departments.All, d => Assert.Contains(d.Id, furnished));
    }

    [Fact]
    public void Each_department_type_has_its_own_furniture_set()
    {
        var snapshot = Generate(3008);
        var byDepartmentType = snapshot.Furniture.GroupBy(p => snapshot.Departments.At(p.Position).Type).ToList();
        var sets = byDepartmentType.ToDictionary(g => g.Key, g => g.Select(p => p.Type).ToHashSet());
        var mostCommon = byDepartmentType.ToDictionary(g => g.Key, g => g.GroupBy(p => p.Type).MaxBy(t => t.Count())!.Key);

        Assert.Equal(11, sets.Count);
        foreach (var (type, set) in sets)
            foreach (var (otherType, otherSet) in sets.Where(o => o.Key != type))
                Assert.False(set.SetEquals(otherSet), $"{type} and {otherType} have the same Furniture set.");
        Assert.Equal(11, mostCommon.Values.Distinct().Count());
    }

    [Fact]
    public void Each_department_type_has_its_own_shelving_or_display_furniture()
    {
        var snapshot = Generate(3008);
        var typesIn = snapshot.Furniture
            .GroupBy(p => snapshot.Departments.At(p.Position).Type)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Type).ToHashSet());

        Assert.Contains(FurnitureType.PalletRack, typesIn[DepartmentType.SelfServeWarehouse]);
        Assert.Contains(FurnitureType.Sofa, typesIn[DepartmentType.Showrooms]);
        Assert.Contains(FurnitureType.ShelfUnit, typesIn[DepartmentType.LightingAndElectrical]);
        Assert.Contains(FurnitureType.KitchenCabinet, typesIn[DepartmentType.BathroomsAndKitchens]);
        Assert.Contains(FurnitureType.GroceryShelf, typesIn[DepartmentType.RestaurantAndFoodMarket]);
        Assert.Contains(FurnitureType.PlantTable, typesIn[DepartmentType.PlantSection]);
        Assert.Contains(FurnitureType.ToyShelf, typesIn[DepartmentType.KidsArea]);
        Assert.DoesNotContain(FurnitureType.PalletRack, typesIn[DepartmentType.Showrooms]);
        Assert.DoesNotContain(FurnitureType.Bathtub, typesIn[DepartmentType.PlantSection]);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(-1)]
    public void Furniture_on_the_floor_never_overlaps(int seed)
    {
        var snapshot = Generate(seed);
        var onFloor = snapshot.Furniture.Where(p => p.Elevation == 0f && !FurnitureCatalog.Spec(p.Type).IsFloorCovering).ToList();

        var overlaps = FootprintPairs(snapshot.Space, onFloor, onFloor).Take(5).ToList();

        Assert.True(overlaps.Count == 0, "Overlapping: " + string.Join("; ", overlaps.Select(o => $"{o.A} / {o.B}")));
    }

    [Fact]
    public void Raised_furniture_rests_on_a_piece_below_it()
    {
        var snapshot = Generate(3008);
        var raised = snapshot.Furniture.Where(p => p.Elevation > 0f).ToList();

        var supported = FootprintPairs(snapshot.Space, raised, snapshot.Furniture)
            .Where(pair => pair.B.Elevation < pair.A.Elevation
                && pair.B.Elevation + FurnitureCatalog.Spec(pair.B).Height >= pair.A.Elevation - 0.01f)
            .Select(pair => pair.A.Id)
            .ToHashSet();

        Assert.NotEmpty(raised);
        Assert.All(raised, piece => Assert.Contains(piece.Id, supported));
    }

    [Fact]
    public void The_same_seed_yields_the_same_furniture_layout()
    {
        var first = Generate(42).Furniture;
        var second = Generate(42).Furniture;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_yield_different_furniture_layouts()
    {
        var first = Generate(42).Furniture.Select(p => (p.Type, p.Position)).ToHashSet();
        var second = Generate(43).Furniture.Select(p => (p.Type, p.Position)).ToHashSet();

        var shared = first.Count(second.Contains);
        Assert.True(shared < first.Count / 2, $"{shared} of {first.Count} pieces are identical.");
    }

    [Theory]
    [InlineData(DepartmentType.Workspaces, "Desk,OfficeChair,FilingCabinet,MeetingTable,DeskLamp")]
    [InlineData(DepartmentType.TextilesAndDecor, "CushionBin,BlanketStack,CurtainRack,FloorMirror,FrameDisplay")]
    [InlineData(DepartmentType.Outdoor, "GardenTable,GardenChair,SunLounger,Parasol,Grill")]
    [InlineData(DepartmentType.DiningAndHallway, "DisplayCabinet,Sideboard,ShoeCabinet,CoatRack,HallBench")]
    [InlineData(DepartmentType.Showrooms, "ArcLamp,TvBench,Dresser,Nightstand")]
    [InlineData(DepartmentType.BathroomsAndKitchens, "WashingMachine,Stove")]
    [InlineData(DepartmentType.SelfServeWarehouse, "BoxCart")]
    [InlineData(DepartmentType.RestaurantAndFoodMarket, "BarStool")]
    [InlineData(DepartmentType.PlantSection, "PlanterBox")]
    [InlineData(DepartmentType.KidsArea, "ToyChest")]
    [InlineData(DepartmentType.LightingAndElectrical, "DeskLamp,ArcLamp")]
    public void Second_wave_furniture_is_generated_in_its_department(DepartmentType department, string names)
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromSeconds(1));
        var snapshot = sim.Snapshot;
        var generated = snapshot.Furniture.Where(p => snapshot.Departments.At(p.Position).Type == department)
            .Select(p => p.Type.ToString()).ToHashSet();
        foreach (var name in names.Split(','))
            Assert.Contains(name, generated);
    }

    [Fact]
    public void Second_wave_preserves_the_benchmark_furniture_and_lamp_budget()
    {
        var snapshot = Generate(3008);
        // Measured on main before ticket 03a, with the unchanged 3x3 benchmark layout.
        Assert.InRange(snapshot.Furniture.Count, 365_682, 446_944);
        Assert.InRange(snapshot.Furniture.Count(p => FurnitureCatalog.Spec(p.Type).IsLamp), 28_581, 34_933);
    }

    [Fact]
    public void New_departments_cover_roughly_a_quarter_of_the_store()
    {
        var shares = new List<float>();
        foreach (var seed in new[] { 3008, 42, -1, 17, 2026 })
        {
            var snapshot = Generate(seed);
            var area = snapshot.Departments.All.Where(d => d.Type is DepartmentType.Workspaces
                or DepartmentType.TextilesAndDecor or DepartmentType.Outdoor or DepartmentType.DiningAndHallway).Sum(d => d.Area);
            shares.Add(area / (snapshot.Space.Size * snapshot.Space.Size));
        }
        Assert.InRange(shares.Average(), 0.20f, 0.32f);
    }

    /// <summary>Pairs of different pieces, one from each list, whose footprints overlap.</summary>
    private static IEnumerable<(FurniturePiece A, FurniturePiece B)> FootprintPairs(
        StoreSpace space, IReadOnlyList<FurniturePiece> pieces, IReadOnlyList<FurniturePiece> others)
    {
        // The largest footprint (a pallet rack) is under 3 m across, so overlapping pieces lie in neighbouring 4 m buckets.
        const float bucket = 4f;
        var bucketsPerSide = (int)(space.Size / bucket);
        var grid = others
            .GroupBy(p => ((int)(p.Position.X / bucket), (int)(p.Position.Y / bucket)))
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var a in pieces)
        {
            var (bx, by) = ((int)(a.Position.X / bucket), (int)(a.Position.Y / bucket));
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var key = ((bx + dx + bucketsPerSide) % bucketsPerSide, (by + dy + bucketsPerSide) % bucketsPerSide);
                    if (!grid.TryGetValue(key, out var candidates))
                        continue;
                    foreach (var b in candidates)
                        if (b.Id != a.Id && FootprintsOverlap(space, a, b))
                            yield return (a, b);
                }
        }
    }

    /// <summary>Separating-axis test on the two rotated footprint rectangles, shrunk by 1 cm so touching is allowed.</summary>
    private static bool FootprintsOverlap(StoreSpace space, FurniturePiece a, FurniturePiece b)
    {
        var (specA, specB) = (FurnitureCatalog.Spec(a), FurnitureCatalog.Spec(b));
        var offset = space.Delta(a.Position, b.Position);
        var reach = (specA.Width + specA.Depth + specB.Width + specB.Depth) / 2f;
        if (offset.LengthSquared() > reach * reach)
            return false;
        var cornersA = Corners(a, Vector2.Zero);
        var cornersB = Corners(b, offset);
        foreach (var polygon in new[] { cornersA, cornersB })
            for (var i = 0; i < 2; i++)
            {
                var edge = polygon[i + 1] - polygon[i];
                var axis = new Vector2(-edge.Y, edge.X);
                var (minA, maxA) = Project(cornersA, axis);
                var (minB, maxB) = Project(cornersB, axis);
                if (maxA <= minB || maxB <= minA)
                    return false;
            }
        return true;
    }

    private static (float Min, float Max) Project(Vector2[] corners, Vector2 axis)
    {
        var (min, max) = (float.MaxValue, float.MinValue);
        foreach (var corner in corners)
        {
            var d = Vector2.Dot(corner, axis);
            (min, max) = (MathF.Min(min, d), MathF.Max(max, d));
        }
        return (min, max);
    }

    private static Vector2[] Corners(FurniturePiece piece, Vector2 centre)
    {
        var spec = FurnitureCatalog.Spec(piece);
        var along = new Vector2(MathF.Cos(piece.Rotation), MathF.Sin(piece.Rotation));
        var across = new Vector2(-along.Y, along.X);
        var halfWidth = along * (spec.Width / 2f - 0.01f);
        var halfDepth = across * (spec.Depth / 2f - 0.01f);
        return [centre + halfWidth + halfDepth, centre + halfWidth - halfDepth, centre - halfWidth - halfDepth, centre - halfWidth + halfDepth];
    }
}
