using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>The Pillar layout world setting: the 3 × 3 grid (default), the 2 × 2 grid or the 3 × 3 large grid, with their Sectors and Stores.</summary>
public class PillarLayoutTests
{
    private static Simulation Create(PillarLayout layout, int seed = 3008) =>
        Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout));

    [Fact]
    public void A_world_uses_the_3_x_3_layout_unless_told_otherwise()
    {
        using var sim = Simulation.Create(new WorldConfig(3008));
        Assert.Equal(PillarLayout.ThreeByThree, new WorldConfig(3008).PillarLayout);
        Assert.Equal(PillarLayout.ThreeByThree, sim.Snapshot.PillarLayout);
        Assert.Equal(9, sim.Snapshot.Pillars.Count);
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.TwoByTwo)]
    [InlineData(PillarLayout.ThreeByThreeLarge)]
    [InlineData(PillarLayout.CornersLarge)]
    [InlineData(PillarLayout.DiamondLarge)]
    public void The_layout_is_part_of_the_world_state_and_a_world_rebuilt_from_it_is_the_same_store(PillarLayout layout)
    {
        using var original = Create(layout, seed: 42);
        original.Advance(TimeSpan.FromHours(3));
        var saved = original.Snapshot;
        Assert.Equal(layout, saved.PillarLayout);

        using var restored = Simulation.Create(new WorldConfig(saved.Seed, PillarLayout: saved.PillarLayout));
        Assert.Equal(layout, restored.Snapshot.PillarLayout);
        Assert.Equal(saved.Space, restored.Snapshot.Space);
        Assert.Equal(saved.Departments.All, restored.Snapshot.Departments.All);
        Assert.Equal(saved.Pillars, restored.Snapshot.Pillars);
        Assert.Equal(saved.Sectors, restored.Snapshot.Sectors);
        Assert.Equal(saved.Furniture, restored.Snapshot.Furniture);
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree, "3x3", 3, 9, 1344f, 448f)]
    [InlineData(PillarLayout.TwoByTwo, "2x2", 2, 4, 1344f, 672f)]
    [InlineData(PillarLayout.ThreeByThreeLarge, "3x3L", 3, 9, 1920f, 640f)]
    [InlineData(PillarLayout.CornersLarge, "3x3L-corners", 3, 4, 1920f, 640f)]
    [InlineData(PillarLayout.DiamondLarge, "3x3L-diamond", 3, 4, 1920f, 640f)]
    public void Layouts_have_a_short_name_a_grid_a_pillar_count_and_a_store_size(PillarLayout layout, string name, int perSide, int pillars, float storeSize, float sectorSize)
    {
        Assert.Equal(name, layout.Name());
        Assert.Equal(perSide, layout.SectorsPerSide());
        Assert.Equal(pillars, layout.PillarCount());
        Assert.Equal(storeSize, layout.Space().Size);
        Assert.Equal(sectorSize, layout.SectorSize());
        Assert.True(PillarLayouts.TryParse(name, out var parsed));
        Assert.Equal(layout, parsed);
        Assert.Equal(layout, PillarLayouts.Parse(name));
        Assert.Equal(layout, PillarLayouts.Parse(name.ToLowerInvariant()));
        Assert.False(PillarLayouts.TryParse("4x4", out _));
        Assert.Throws<ArgumentException>(() => PillarLayouts.Parse("4x4"));
    }

    [Theory]
    [InlineData(PillarLayout.CornersLarge, new[] { "A-1", "C-1", "A-3", "C-3" }, new[] { "B-1", "A-2", "B-2", "C-2", "B-3" })]
    [InlineData(PillarLayout.DiamondLarge, new[] { "B-1", "A-2", "C-2", "B-3" }, new[] { "A-1", "C-1", "B-2", "A-3", "C-3" })]
    public void The_four_pillar_large_layouts_leave_the_other_sectors_of_the_3_x_3_large_grid_empty(PillarLayout layout, string[] withPillar, string[] empty)
    {
        using var sim = Create(layout);
        using var full = Create(PillarLayout.ThreeByThreeLarge);
        var snapshot = sim.Snapshot;

        Assert.Equal(withPillar, snapshot.Pillars.Select(p => p.Coordinate));
        Assert.Equal(Enumerable.Range(0, 4), snapshot.Pillars.Select(p => p.Id));
        // The same nine Sectors as 3 × 3 large, and the Pillars stand where 3 × 3 large has them.
        Assert.Equal(full.Snapshot.Sectors, snapshot.Sectors);
        foreach (var pillar in snapshot.Pillars)
        {
            Assert.Equal(full.Snapshot.Pillars.Single(p => p.Coordinate == pillar.Coordinate).Position, pillar.Position);
            Assert.Equal(pillar.Coordinate, snapshot.SectorAt(pillar.Position).Name);
        }
        // An empty Sector keeps its grid name, and Furniture stands at its centre.
        foreach (var name in empty)
        {
            var sector = snapshot.Sectors.Single(s => s.Name == name);
            Assert.DoesNotContain(snapshot.Pillars, p => p.Coordinate == name);
            Assert.Contains(snapshot.Furniture, p => snapshot.Space.Distance(p.Position, sector.Centre) < 24f);
            Assert.Equal(sector.Centre, snapshot.OutsidePillars(sector.Centre));
        }
        // The seed gives the same Departments as on 3 × 3 large.
        Assert.Equal(full.Snapshot.Departments.All, snapshot.Departments.All);
    }

    [Theory]
    // Corners: across the Seams the four stand as a 640 m square.
    [InlineData(PillarLayout.CornersLarge, new[] { 640f, 640f, 905f })]
    // Diamond: the Pillar north and the Pillar south (across the Seam) are the same one, 640 m away both ways.
    [InlineData(PillarLayout.DiamondLarge, new[] { 640f, 905f, 905f })]
    public void Every_pillar_of_a_four_pillar_large_layout_sees_the_others_at_the_same_distances(PillarLayout layout, float[] expected)
    {
        using var sim = Create(layout);
        var snapshot = sim.Snapshot;
        foreach (var pillar in snapshot.Pillars)
        {
            var distances = snapshot.Pillars.Where(p => p != pillar).Select(p => MathF.Round(snapshot.Space.Distance(pillar.Position, p.Position))).Order();
            Assert.Equal(expected, distances);
        }
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.TwoByTwo)]
    [InlineData(PillarLayout.ThreeByThreeLarge)]
    [InlineData(PillarLayout.CornersLarge)]
    [InlineData(PillarLayout.DiamondLarge)]
    public void The_world_stands_in_the_store_of_its_layout(PillarLayout layout)
    {
        using var sim = Create(layout);
        var snapshot = sim.Snapshot;
        Assert.Equal(layout.Space(), snapshot.Space);
        Assert.Equal(snapshot.Space.Size, snapshot.Departments.CellSize * snapshot.Departments.CellsPerSide);
        Assert.All(snapshot.Furniture, p => Assert.Equal(p.Position, snapshot.Space.Wrap(p.Position)));
    }

    [Fact]
    public void The_3_x_3_large_store_is_1920_m_of_whole_64_m_chunks()
    {
        Assert.Equal(64f, StoreSpace.Large.ChunkSize);
        Assert.Equal(30, StoreSpace.Large.ChunksPerSide);
        Assert.Equal(1920f, StoreSpace.Large.Size);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(-1)]
    public void Nine_pillars_stand_640_m_apart_on_the_3_x_3_large_grid(int seed)
    {
        using var sim = Create(PillarLayout.ThreeByThreeLarge, seed);
        var snapshot = sim.Snapshot;

        Assert.Equal(["A-1", "B-1", "C-1", "A-2", "B-2", "C-2", "A-3", "B-3", "C-3"], snapshot.Pillars.Select(p => p.Coordinate));
        float[] gridLines = [320f, 960f, 1600f];
        foreach (var pillar in snapshot.Pillars)
        {
            var column = pillar.Coordinate[0] - 'A';
            var row = pillar.Coordinate[2] - '1';
            Assert.Equal(new Vector2(gridLines[column], gridLines[row]), pillar.Position);
            Assert.Equal(32f, pillar.Width);
            // Pillar centres lie on the 16 m aisle grid, like in the other layouts.
            Assert.Equal(0f, pillar.Position.X % 16f);
            Assert.Equal(0f, pillar.Position.Y % 16f);
        }
        Assert.Equal(new Vector2(snapshot.Space.Size / 2f), snapshot.Pillars.Single(p => p.Coordinate == "B-2").Position);
        foreach (var pillar in snapshot.Pillars)
            foreach (var other in snapshot.Pillars.Where(p => p != pillar))
                Assert.Contains(MathF.Round(snapshot.Space.Distance(pillar.Position, other.Position)), new[] { 640f, 905f });
    }

    [Fact]
    public void The_3_x_3_large_sectors_are_nine_640_m_squares_on_chunk_boundaries()
    {
        using var sim = Create(PillarLayout.ThreeByThreeLarge);
        var snapshot = sim.Snapshot;

        Assert.Equal(9, snapshot.Sectors.Count);
        Assert.Equal(snapshot.Pillars.Select(p => p.Coordinate), snapshot.Sectors.Select(s => s.Name));
        foreach (var (sector, pillar) in snapshot.Sectors.Zip(snapshot.Pillars))
        {
            Assert.Equal(640f, sector.Size);
            Assert.Equal(pillar.Position, sector.Centre);
            Assert.Equal(0f, sector.Origin.X % snapshot.Space.ChunkSize);
            Assert.Equal(0f, sector.Origin.Y % snapshot.Space.ChunkSize);
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(1f, 1f)));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(sector.Size - 1f)));
        }
        Assert.Equal(snapshot.Space.Size * snapshot.Space.Size, snapshot.Sectors.Sum(s => s.Size * s.Size));
        Assert.Equal("C-3", sim.SectorAt(new Vector2(-1f, -1f)).Name);
        Assert.Equal("A-1", sim.SectorAt(new Vector2(1920f, 1920f)).Name);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void Departments_keep_their_size_in_the_larger_store(int seed)
    {
        using var standard = Create(PillarLayout.ThreeByThree, seed);
        using var large = Create(PillarLayout.ThreeByThreeLarge, seed);

        var standardMean = standard.Snapshot.Departments.All.Average(d => d.Area);
        var largeMean = large.Snapshot.Departments.All.Average(d => d.Area);
        Assert.InRange(largeMean / standardMean, 0.75, 1.33);
        // So the larger Store has about twice as many Departments, every type among them.
        Assert.InRange((double)large.Snapshot.Departments.All.Count / standard.Snapshot.Departments.All.Count, 1.5, 2.7);
        Assert.Equal(Enum.GetValues<DepartmentType>().Order(), large.Snapshot.Departments.All.Select(d => d.Type).Distinct().Order());
    }

    [Fact]
    public void The_larger_store_is_as_foggy_as_the_standard_one()
    {
        static double MeanCoverage(PillarLayout layout)
        {
            using var sim = Create(layout);
            var area = (double)sim.Snapshot.Space.Size * sim.Snapshot.Space.Size;
            var samples = new List<double>();
            for (var hour = 0; hour < 24 * 6; hour++)
            {
                sim.Advance(TimeSpan.FromHours(1));
                samples.Add(sim.Snapshot.FogBanks.Sum(b => Math.PI * b.Radius * b.Radius) / area);
            }
            return samples.Average();
        }

        var standard = MeanCoverage(PillarLayout.ThreeByThree);
        var large = MeanCoverage(PillarLayout.ThreeByThreeLarge);
        Assert.InRange(large / standard, 0.7, 1.4);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(-1)]
    public void Four_identical_pillars_stand_on_the_2_x_2_grid_marked_by_their_grid_coordinate(int seed)
    {
        using var sim = Create(PillarLayout.TwoByTwo, seed);
        var snapshot = sim.Snapshot;

        Assert.Equal(["A-1", "B-1", "A-2", "B-2"], snapshot.Pillars.Select(p => p.Coordinate));
        Assert.Equal(Enumerable.Range(0, 4), snapshot.Pillars.Select(p => p.Id));
        float[] gridLines = [336f, 1008f];
        foreach (var pillar in snapshot.Pillars)
        {
            var column = pillar.Coordinate[0] - 'A';
            var row = pillar.Coordinate[2] - '1';
            Assert.Equal(new Vector2(gridLines[column], gridLines[row]), pillar.Position);
            Assert.Equal(32f, pillar.Width);
            // No Pillar or its walkway ring touches a Seam.
            Assert.InRange(pillar.Position.X, pillar.ClearHalfWidth, snapshot.Space.Size - pillar.ClearHalfWidth);
            Assert.InRange(pillar.Position.Y, pillar.ClearHalfWidth, snapshot.Space.Size - pillar.ClearHalfWidth);
        }
        Assert.Single(snapshot.Pillars.Select(p => p.Height).Distinct());
        Assert.True(snapshot.Pillars[0].Height >= 1000f);
    }

    [Fact]
    public void Only_the_layout_moves_the_pillars_not_the_seed()
    {
        using var first = Create(PillarLayout.TwoByTwo, 3008);
        using var second = Create(PillarLayout.TwoByTwo, 42);
        using var other = Create(PillarLayout.ThreeByThree, 3008);
        Assert.Equal(first.Snapshot.Pillars, second.Snapshot.Pillars);
        Assert.Equal(first.Snapshot.Sectors, second.Snapshot.Sectors);
        Assert.NotEqual(first.Snapshot.Pillars, other.Snapshot.Pillars);
        // The seed still makes the same Departments in both layouts.
        Assert.Equal(first.Snapshot.Departments.All, other.Snapshot.Departments.All);
    }

    [Fact]
    public void The_2_x_2_sectors_are_four_equal_672_m_squares_centred_on_and_named_after_their_pillars()
    {
        using var sim = Create(PillarLayout.TwoByTwo);
        var snapshot = sim.Snapshot;

        Assert.Equal(4, snapshot.Sectors.Count);
        Assert.Equal(snapshot.Pillars.Select(p => p.Coordinate), snapshot.Sectors.Select(s => s.Name));
        foreach (var (sector, pillar) in snapshot.Sectors.Zip(snapshot.Pillars))
        {
            Assert.Equal(672f, sector.Size);
            Assert.Equal(pillar.Position, sector.Centre);
            Assert.Contains(sector.Origin.X, new[] { 0f, 672f });
            Assert.Contains(sector.Origin.Y, new[] { 0f, 672f });
            // Boundaries follow the 16 m aisle grid, though not the Chunk grid (672 m is 10.5 Chunks).
            Assert.Equal(0f, sector.Origin.X % 16f);
            Assert.Equal(0f, sector.Origin.Y % 16f);
        }
        Assert.Equal(snapshot.Space.Size * snapshot.Space.Size, snapshot.Sectors.Sum(s => s.Size * s.Size));
    }

    [Fact]
    public void Every_position_inside_a_2_x_2_sector_is_in_that_sector()
    {
        using var sim = Create(PillarLayout.TwoByTwo);
        var snapshot = sim.Snapshot;
        foreach (var sector in snapshot.Sectors)
        {
            Assert.Equal(sector, snapshot.SectorAt(sector.Centre));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(1f, 1f)));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(sector.Size - 1f)));
            Assert.Equal(sector, sim.SectorAt(sector.Origin + new Vector2(1f, sector.Size - 1f)));
        }
    }

    [Theory]
    // A boundary belongs to the Sector east or south of it.
    [InlineData(0f, 0f, "A-1")]
    [InlineData(672f, 0f, "B-1")]
    [InlineData(671.9f, 0f, "A-1")]
    [InlineData(0f, 672f, "A-2")]
    [InlineData(672f, 672f, "B-2")]
    [InlineData(671.9f, 671.9f, "A-1")]
    [InlineData(1343.9f, 1343.9f, "B-2")]
    // Unwrapped positions across a Seam are in the Sector on the far side.
    [InlineData(-1f, -1f, "B-2")]
    [InlineData(-0.5f, 300f, "B-1")]
    [InlineData(1344f, 1344f, "A-1")]
    [InlineData(1354f, 700f, "A-2")]
    [InlineData(300f, -10f, "A-2")]
    [InlineData(-1344f + 700f, 2688f + 10f, "B-1")]
    public void The_2_x_2_sector_query_works_on_boundaries_and_across_a_seam(float x, float y, string expected)
    {
        using var sim = Create(PillarLayout.TwoByTwo);
        Assert.Equal(expected, sim.SectorAt(new Vector2(x, y)).Name);
        Assert.Equal(expected, sim.Snapshot.SectorAt(new Vector2(x, y)).Name);
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree, 3008)]
    [InlineData(PillarLayout.TwoByTwo, 3008)]
    [InlineData(PillarLayout.TwoByTwo, -1)]
    [InlineData(PillarLayout.TwoByTwo, 42)]
    [InlineData(PillarLayout.ThreeByThreeLarge, 3008)]
    [InlineData(PillarLayout.CornersLarge, 3008)]
    [InlineData(PillarLayout.DiamondLarge, 42)]
    public void No_furniture_stands_in_a_pillar_footprint_or_its_walkway_ring(PillarLayout layout, int seed)
    {
        using var sim = Create(layout, seed);
        var snapshot = sim.Snapshot;
        foreach (var pillar in snapshot.Pillars)
        {
            var clear = pillar.ClearHalfWidth;
            // The abandoned scaffold (a Landmark) is the one exception: it climbs its Pillar's walkway ring.
            var scaffold = snapshot.Landmarks.Single(l => l.Type == LandmarkType.AbandonedScaffold);
            var nearby = snapshot.Furniture.Where(p => snapshot.Space.Distance(p.Position, pillar.Position) < clear + 10f)
                .Where(p => p.LandmarkId != scaffold.Id || scaffold.PillarId != pillar.Id).ToArray();
            // The Store goes on right up to the walkway.
            Assert.NotEmpty(nearby);
            foreach (var piece in nearby)
            {
                var spec = FurnitureCatalog.Spec(piece);
                var delta = snapshot.Space.Delta(pillar.Position, piece.Position);
                var c = MathF.Abs(MathF.Cos(piece.Rotation));
                var s = MathF.Abs(MathF.Sin(piece.Rotation));
                const float touching = 0.02f;
                Assert.True(MathF.Abs(delta.X) >= clear + (c * spec.Width + s * spec.Depth) / 2f - touching ||
                    MathF.Abs(delta.Y) >= clear + (s * spec.Width + c * spec.Depth) / 2f - touching,
                    $"Furniture {piece.Id} ({piece.Type}) intrudes on Pillar {pillar.Coordinate} or its walkway.");
            }
        }
    }

    [Fact]
    public void In_2_x_2_the_east_and_west_neighbour_of_a_pillar_is_the_same_pillar_672_m_away_both_ways()
    {
        using var sim = Create(PillarLayout.TwoByTwo);
        var snapshot = sim.Snapshot;
        var space = snapshot.Space;
        var byCoordinate = snapshot.Pillars.ToDictionary(p => p.Coordinate);
        var a1 = byCoordinate["A-1"];

        // Looking east and west from A-1, the next Pillar is B-1 both times, across the Seam to the west.
        Assert.Equal(byCoordinate["B-1"], snapshot.NearestPillar(a1.Position + new Vector2(400f, 0f)));
        Assert.Equal(byCoordinate["B-1"], snapshot.NearestPillar(a1.Position - new Vector2(400f, 0f)));
        Assert.Equal(byCoordinate["A-2"], snapshot.NearestPillar(a1.Position + new Vector2(0f, 400f)));
        Assert.Equal(byCoordinate["A-2"], snapshot.NearestPillar(a1.Position - new Vector2(0f, 400f)));
        Assert.Equal(672f, space.Distance(a1.Position, byCoordinate["B-1"].Position));
        Assert.Equal(672f, MathF.Abs(space.Delta(a1.Position, byCoordinate["B-1"].Position).X));
        Assert.Equal(672f, MathF.Abs(space.Delta(byCoordinate["B-1"].Position, a1.Position + new Vector2(space.Size, 0f)).X));
        foreach (var pillar in snapshot.Pillars)
            foreach (var other in snapshot.Pillars.Where(p => p != pillar))
                Assert.Contains(MathF.Round(space.Distance(pillar.Position, other.Position)), new[] { 672f, 950f });
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree, 316.8f)]
    [InlineData(PillarLayout.TwoByTwo, 475.2f)]
    [InlineData(PillarLayout.ThreeByThreeLarge, 452.5f)]
    [InlineData(PillarLayout.CornersLarge, 905.1f)]
    [InlineData(PillarLayout.DiamondLarge, 1011.9f)]
    public void No_place_in_the_store_is_farther_from_its_nearest_pillar_than_the_layout_allows(PillarLayout layout, float farthest)
    {
        using var sim = Create(layout);
        var snapshot = sim.Snapshot;
        var worst = 0f;
        // A 4 m grid over the whole Store, plus unwrapped positions beyond the Seams.
        for (var y = -8f; y < snapshot.Space.Size + 8f; y += 4f)
            for (var x = -8f; x < snapshot.Space.Size + 8f; x += 4f)
            {
                var position = new Vector2(x, y);
                var nearest = snapshot.NearestPillar(position);
                var distance = snapshot.Space.Distance(position, nearest.Position);
                Assert.All(snapshot.Pillars, p => Assert.True(snapshot.Space.Distance(position, p.Position) >= distance));
                worst = MathF.Max(worst, distance);
            }
        Assert.InRange(worst, farthest - 0.5f, farthest + 0.5f);
        // With a Pillar in every Sector, the Sector corner shared by four Sectors is the farthest point.
        if (snapshot.Pillars.Count == snapshot.Sectors.Count)
        {
            var sectorSize = snapshot.Sectors[0].Size;
            Assert.InRange(snapshot.Space.Distance(new Vector2(sectorSize), snapshot.NearestPillar(new Vector2(sectorSize)).Position), farthest - 0.5f, farthest + 0.5f);
        }
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.TwoByTwo)]
    [InlineData(PillarLayout.ThreeByThreeLarge)]
    [InlineData(PillarLayout.CornersLarge)]
    [InlineData(PillarLayout.DiamondLarge)]
    public void A_position_inside_a_pillar_is_moved_just_outside_it_and_any_other_position_stays(PillarLayout layout)
    {
        using var sim = Create(layout);
        var snapshot = sim.Snapshot;
        foreach (var pillar in snapshot.Pillars)
        {
            var half = pillar.Width / 2f;
            foreach (var offset in new Vector2[] { new(0f, 0f), new(5f, -14f), new(-15.9f, 2f), new(10f, 15f), new(-15f, -15.5f) })
            {
                var inside = pillar.Position + offset;
                var outside = snapshot.OutsidePillars(inside);
                var delta = snapshot.Space.Delta(pillar.Position, outside);
                // Just outside the footprint, on the walkway, not inside the Pillar.
                Assert.True(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) > half);
                Assert.True(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) <= pillar.ClearHalfWidth);
                Assert.True(snapshot.Space.Distance(inside, outside) <= half + Pillar.WalkwayWidth);
                // Moved straight out through the nearest face.
                Assert.True(MathF.Abs(delta.X - offset.X) < 0.01f || MathF.Abs(delta.Y - offset.Y) < 0.01f);
            }
            // Unwrapped positions keep their image of the Store.
            var unwrapped = pillar.Position + new Vector2(snapshot.Space.Size, -snapshot.Space.Size) + new Vector2(3f, 1f);
            var moved = snapshot.OutsidePillars(unwrapped);
            Assert.True(Vector2.Distance(unwrapped, moved) < pillar.Width);
            var walkway = pillar.Position + new Vector2(0f, half + 1f);
            Assert.Equal(walkway, snapshot.OutsidePillars(walkway));
        }
        Assert.Equal(new Vector2(5f, 5f), snapshot.OutsidePillars(new Vector2(5f, 5f)));
    }
}
