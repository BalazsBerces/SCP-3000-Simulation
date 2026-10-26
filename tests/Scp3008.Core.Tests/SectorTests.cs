using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class SectorTests
{
    [Fact]
    public void The_store_is_divided_into_nine_equal_sectors_centred_on_and_named_after_their_pillars()
    {
        var snapshot = TestStores.Get(3008);

        Assert.Equal(9, snapshot.Sectors.Count);
        Assert.Equal(snapshot.Pillars.Select(p => p.Coordinate), snapshot.Sectors.Select(s => s.Name));
        foreach (var (sector, pillar) in snapshot.Sectors.Zip(snapshot.Pillars))
        {
            Assert.Equal(448f, sector.Size);
            Assert.Equal(pillar.Position, sector.Centre);
            Assert.Contains(sector.Origin.X, new[] { 0f, 448f, 896f });
            Assert.Contains(sector.Origin.Y, new[] { 0f, 448f, 896f });
            // Sector boundaries coincide with Chunk boundaries.
            Assert.Equal(0f, sector.Origin.X % snapshot.Space.ChunkSize);
            Assert.Equal(0f, sector.Origin.Y % snapshot.Space.ChunkSize);
        }
        Assert.Equal(snapshot.Space.Size * snapshot.Space.Size, snapshot.Sectors.Sum(s => s.Size * s.Size));
    }

    [Fact]
    public void Every_position_inside_a_sector_is_in_that_sector()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        var snapshot = sim.Snapshot;
        foreach (var sector in snapshot.Sectors)
        {
            Assert.Equal(sector, snapshot.SectorAt(sector.Centre));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(1f, 1f)));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(sector.Size - 1f)));
            Assert.Equal(sector, snapshot.SectorAt(sector.Origin + new Vector2(sector.Size - 1f, 1f)));
            Assert.Equal(sector, sim.SectorAt(sector.Centre));
        }
    }

    [Theory]
    // A boundary belongs to the Sector east or south of it.
    [InlineData(0f, 0f, "A-1")]
    [InlineData(448f, 0f, "B-1")]
    [InlineData(447.9f, 0f, "A-1")]
    [InlineData(896f, 448f, "C-2")]
    [InlineData(895.9f, 447.9f, "B-1")]
    [InlineData(448f, 896f, "B-3")]
    [InlineData(1343.9f, 1343.9f, "C-3")]
    public void Positions_on_sector_boundaries_belong_to_one_sector(float x, float y, string expected)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        Assert.Equal(expected, sim.SectorAt(new Vector2(x, y)).Name);
    }

    [Theory]
    // Unwrapped positions across a Seam are in the Sector on the far side.
    [InlineData(-1f, -1f, "C-3")]
    [InlineData(-0.5f, 500f, "C-2")]
    [InlineData(1344f, 1344f, "A-1")]
    [InlineData(1354f, 5f, "A-1")]
    [InlineData(700f, -10f, "B-3")]
    [InlineData(-1344f + 700f, 2688f + 10f, "B-1")]
    public void Positions_across_a_seam_are_in_the_wrapped_sector(float x, float y, string expected)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        Assert.Equal(expected, sim.SectorAt(new Vector2(x, y)).Name);
        Assert.Equal(sim.SectorAt(new Vector2(x, y)), sim.Snapshot.SectorAt(sim.Snapshot.Space.Wrap(new Vector2(x, y))));
    }

    [Fact]
    public void Sectors_are_the_same_for_every_store_seed()
    {
        using var first = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        using var second = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 2));
        Assert.Equal(first.Snapshot.Sectors, second.Snapshot.Sectors);
    }
}
