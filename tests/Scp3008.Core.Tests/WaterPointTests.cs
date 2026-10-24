using Scp3008.Core;
using Xunit.Abstractions;

namespace Scp3008.Core.Tests;

/// <summary>Acceptance tests of ticket 08p: Water points and the few running showroom taps.</summary>
public class WaterPointTests(ITestOutputHelper output)
{
    private static readonly int[] Seeds = [3008, 42, 7];

    private static WorldSnapshot Store(int seed, PillarLayout layout = PillarLayout.ThreeByThree, WaterConfig? water = null,
        bool poiShowcase = false, bool landmarkShowcase = false)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, PointOfInterestShowcase: poiShowcase,
            LandmarkShowcase: landmarkShowcase, Water: water));
        return sim.Snapshot;
    }

    private static IEnumerable<WaterPoint> Taps(WorldSnapshot s) => s.WaterPoints.Where(w => w.Kind == WaterPointKind.ShowroomTap);

    [Fact]
    public void Restroom_washbasins_are_listed_and_always_run()
    {
        var restrooms = Store(3008, poiShowcase: true);
        var expected = restrooms.Furniture.Count(p => p.Type == FurnitureType.VanityUnit);
        var basins = restrooms.WaterPoints.Where(w => w.Kind == WaterPointKind.RestroomWashbasin).ToList();
        Assert.True(expected >= 3);
        Assert.Equal(expected, basins.Count);
        Assert.All(basins, b => Assert.True(b.Running));
        Assert.All(basins, b => Assert.Equal(FurnitureType.VanityUnit, restrooms.Furniture.Single(p => p.Id == b.FurnitureId).Type));
    }

    [Fact]
    public void Every_restroom_in_a_store_has_its_washbasins_listed()
    {
        foreach (var seed in Seeds)
        {
            var s = Store(seed);
            foreach (var restroom in s.PointsOfInterest.Where(p => p.Type == PointOfInterestType.StoreRestroom))
            {
                var vanities = s.Furniture.Where(p => p.PointOfInterestId == restroom.Id && p.Type == FurnitureType.VanityUnit).Select(p => p.Id).ToHashSet();
                Assert.NotEmpty(vanities);
                var listed = s.WaterPoints.Where(w => w.Kind == WaterPointKind.RestroomWashbasin && vanities.Contains(w.FurnitureId)).Count();
                Assert.Equal(vanities.Count, listed);
            }
        }
    }

    [Fact]
    public void Fountain_court_and_camping_lake_are_listed_inside_their_landmark()
    {
        var s = Store(3008, landmarkShowcase: true);
        var fountain = Assert.Single(s.Landmarks, l => l.Type == LandmarkType.FountainCourt);
        var camp = Assert.Single(s.Landmarks, l => l.Type == LandmarkType.CampingGround);
        var water = Assert.Single(s.WaterPoints, w => w.Kind == WaterPointKind.FountainCourt);
        var lake = Assert.Single(s.WaterPoints, w => w.Kind == WaterPointKind.CampingLake);
        Assert.True(water.Running && lake.Running);
        Assert.True(fountain.Covers(s.Space, water.Position));
        Assert.True(camp.Covers(s.Space, lake.Position));
    }

    [Fact]
    public void Only_a_small_share_of_showroom_sinks_run()
    {
        foreach (var seed in Seeds)
        {
            var taps = Taps(Store(seed)).ToList();
            var running = taps.Count(t => t.Running);
            output.WriteLine($"seed {seed}: {running} of {taps.Count} showroom taps run");
            Assert.True(taps.Count > 20);
            Assert.InRange(running, 1, (int)Math.Ceiling(taps.Count * 0.05));
        }
    }

    [Fact]
    public void The_share_is_tunable()
    {
        Assert.DoesNotContain(Taps(Store(42, water: new WaterConfig { RunningTapShare = 0f })), t => t.Running);
        var all = Taps(Store(42, water: new WaterConfig { RunningTapShare = 1f })).ToList();
        Assert.All(all, t => Assert.True(t.Running));
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.TwoByTwo)]
    public void The_same_seed_picks_the_same_taps_on_every_layout(PillarLayout layout)
    {
        var first = Taps(Store(3008, layout)).Where(t => t.Running).Select(t => t.FurnitureId).ToList();
        var second = Taps(Store(3008, layout)).Where(t => t.Running).Select(t => t.FurnitureId).ToList();
        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, Taps(Store(42, layout)).Where(t => t.Running).Select(t => t.FurnitureId).ToList());
    }

    [Fact]
    public void Which_taps_run_does_not_change_as_time_advances()
    {
        using var sim = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 1));
        var before = sim.Snapshot.WaterPoints;
        sim.Advance(TimeSpan.FromHours(30));
        Assert.Equal(before, sim.Snapshot.WaterPoints);
    }
}
