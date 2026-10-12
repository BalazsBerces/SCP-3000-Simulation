using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>The Pillar layout world setting: the 3 × 3 grid (default) or the 2 × 2 grid, with their Sectors.</summary>
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
    public void The_layout_is_part_of_the_world_state_and_a_world_rebuilt_from_it_is_the_same_store(PillarLayout layout)
    {
        using var original = Create(layout, seed: 42);
        original.Advance(TimeSpan.FromHours(3));
        var saved = original.Snapshot;
        Assert.Equal(layout, saved.PillarLayout);

        using var restored = Simulation.Create(new WorldConfig(saved.Seed, PillarLayout: saved.PillarLayout));
        Assert.Equal(layout, restored.Snapshot.PillarLayout);
        Assert.Equal(saved.Pillars, restored.Snapshot.Pillars);
        Assert.Equal(saved.Sectors, restored.Snapshot.Sectors);
        Assert.Equal(saved.Furniture, restored.Snapshot.Furniture);
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree, "3x3", 3)]
    [InlineData(PillarLayout.TwoByTwo, "2x2", 2)]
    public void Layouts_have_a_short_name_and_a_grid_size(PillarLayout layout, string name, int perSide)
    {
        Assert.Equal(name, layout.Name());
        Assert.Equal(perSide, layout.PillarsPerSide());
        Assert.True(PillarLayouts.TryParse(name, out var parsed));
        Assert.Equal(layout, parsed);
        Assert.Equal(layout, PillarLayouts.Parse(name));
        Assert.False(PillarLayouts.TryParse("4x4", out _));
        Assert.Throws<ArgumentException>(() => PillarLayouts.Parse("4x4"));
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
    public void No_furniture_stands_in_a_pillar_footprint_or_its_walkway_ring(PillarLayout layout, int seed)
    {
        using var sim = Create(layout, seed);
        var snapshot = sim.Snapshot;
        foreach (var pillar in snapshot.Pillars)
        {
            var clear = pillar.ClearHalfWidth;
            var nearby = snapshot.Furniture.Where(p => snapshot.Space.Distance(p.Position, pillar.Position) < clear + 10f).ToArray();
            // The Store goes on right up to the walkway.
            Assert.NotEmpty(nearby);
            foreach (var piece in nearby)
            {
                var spec = FurnitureCatalog.Spec(piece.Type);
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
        // The Sector corner shared by four Sectors is the farthest point.
        var sectorSize = snapshot.Sectors[0].Size;
        Assert.InRange(snapshot.Space.Distance(new Vector2(sectorSize), snapshot.NearestPillar(new Vector2(sectorSize)).Position), farthest - 0.5f, farthest + 0.5f);
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.TwoByTwo)]
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
