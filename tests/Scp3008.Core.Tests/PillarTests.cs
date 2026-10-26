using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PillarTests
{
    private static readonly float[] GridLines = [224f, 672f, 1120f];

    [Theory]
    [InlineData(3008)]
    [InlineData(-1)]
    [InlineData(42)]
    public void Nine_identical_pillars_stand_on_the_3_x_3_grid_marked_by_their_grid_coordinate(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, StartingBands: false, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromMinutes(1));
        var snapshot = sim.Snapshot;

        Assert.Equal(9, snapshot.Pillars.Count);
        Assert.Equal(Enumerable.Range(0, 9), snapshot.Pillars.Select(p => p.Id));
        Assert.Equal(["A-1", "B-1", "C-1", "A-2", "B-2", "C-2", "A-3", "B-3", "C-3"], snapshot.Pillars.Select(p => p.Coordinate));
        foreach (var pillar in snapshot.Pillars)
        {
            var column = pillar.Coordinate[0] - 'A';
            var row = pillar.Coordinate[2] - '1';
            Assert.Equal(new Vector2(GridLines[column], GridLines[row]), pillar.Position);
            Assert.Equal(32f, pillar.Width);
        }
        Assert.Single(snapshot.Pillars.Select(p => p.Height).Distinct());
        Assert.Equal(new Vector2(snapshot.Space.Size / 2f), snapshot.Pillars.Single(p => p.Coordinate == "B-2").Position);
    }

    [Fact]
    public void Pillars_are_the_same_for_every_store_seed_runtime_seed_and_time()
    {
        using var first = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1));
        using var second = Simulation.Create(new WorldConfig(42, StartingBands: false, RuntimeRngSeed: 2));
        first.Scenario.ClearFogBanks();
        second.Advance(TimeSpan.FromHours(12));

        Assert.Equal(first.Snapshot.Pillars, second.Snapshot.Pillars);
    }

    [Fact]
    public void Pillars_rise_beyond_any_camera_height()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1));
        // The Observer camera stays within a few hundred metres of the floor.
        Assert.All(sim.Snapshot.Pillars, p => Assert.True(p.Height >= 1000f));
    }

    [Fact]
    public void Pillars_are_448_m_apart_in_both_directions_including_across_the_seams_and_clear_of_them()
    {
        var snapshot = TestStores.Get(3008);
        var space = snapshot.Space;
        var byCoordinate = snapshot.Pillars.ToDictionary(p => p.Coordinate);
        foreach (var pillar in snapshot.Pillars)
        {
            var column = pillar.Coordinate[0] - 'A';
            var row = pillar.Coordinate[2] - '1';
            // The next Pillar east and south, wrapping from C to A and from 3 to 1 across a Seam.
            var east = byCoordinate[$"{(char)('A' + (column + 1) % 3)}-{row + 1}"];
            var south = byCoordinate[$"{(char)('A' + column)}-{(row + 1) % 3 + 1}"];
            Assert.Equal(new Vector2(448f, 0f), space.Delta(pillar.Position, east.Position));
            Assert.Equal(new Vector2(0f, 448f), space.Delta(pillar.Position, south.Position));

            // No Pillar or its walkway ring touches a Seam.
            var clearance = pillar.ClearHalfWidth;
            Assert.InRange(pillar.Position.X, clearance, space.Size - clearance);
            Assert.InRange(pillar.Position.Y, clearance, space.Size - clearance);
        }
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(-1)]
    [InlineData(42)]
    public void No_furniture_stands_in_a_pillar_footprint_or_its_walkway_ring(int seed)
    {
        var snapshot = TestStores.Get(seed);
        foreach (var pillar in snapshot.Pillars)
        {
            var clear = pillar.ClearHalfWidth;
            Assert.Equal(pillar.Width / 2f + 3f, clear);
            var nearby = snapshot.Furniture.Where(p => snapshot.Space.Distance(p.Position, pillar.Position) < clear + 10f).ToArray();
            // The Store goes on right up to the walkway.
            Assert.NotEmpty(nearby);
            foreach (var piece in nearby)
            {
                // Conservative axis-aligned bounds of each rotated Furniture footprint.
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
    public void Pillars_remain_inert_landmarks_through_closing_time_and_reopening()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1,
            Arrivals: new ArrivalConfig { IntervalHours = 0 }));
        sim.Scenario.ClearFogBanks();
        sim.Scenario.SetFogBankFormation(false);
        var pillars = sim.Snapshot.Pillars.ToArray();
        var sectors = sim.Snapshot.Sectors.ToArray();
        var lamps = sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp).ToArray();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.AdvanceTo(TimeSpan.FromHours(23));
        Assert.Equal(0, sim.Snapshot.StoreLightLevel);
        Assert.Equal(pillars, sim.Snapshot.Pillars);
        Assert.Equal(sectors, sim.Snapshot.Sectors);
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp));
        Assert.Single(events.OfType<ClosingTimeBegan>());
        sim.AdvanceTo(TimeSpan.FromHours(8));
        Assert.Equal(pillars, sim.Snapshot.Pillars);
        Assert.Equal(sectors, sim.Snapshot.Sectors);
        Assert.Single(events.OfType<StoreReopened>());
    }
}
