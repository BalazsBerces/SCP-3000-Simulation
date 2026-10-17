using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>Locked acceptance tests of ticket 08j: the realistic Restroom, built in the Point of interest showcase only.</summary>
public class RestroomTests
{
    private static WorldSnapshot Showcase(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        return sim.Snapshot;
    }

    private static (PointOfInterest Restroom, List<FurniturePiece> Pieces) RestroomOf(WorldSnapshot snapshot)
    {
        var restroom = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.StoreRestroom);
        return (restroom, snapshot.Furniture.Where(p => p.PointOfInterestId == restroom.Id).ToList());
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void The_showcase_has_one_10_4_by_6_metre_restroom_in_place_of_the_old_one(int seed)
    {
        var snapshot = Showcase(seed);

        var (restroom, _) = RestroomOf(snapshot);
        Assert.Equal(new Vector2(10.4f, 6.0f), restroom.Size);
        Assert.DoesNotContain(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.Restroom);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_new_restroom_has_tiled_surfaces_stalls_washbasins_mirror_and_dryers(int seed)
    {
        var (_, pieces) = RestroomOf(Showcase(seed));
        var types = pieces.Select(p => p.Type).ToHashSet();

        FurnitureType[] expected =
        [
            FurnitureType.TiledFloor,
            FurnitureType.TiledWall,
            FurnitureType.StallPartition,
            FurnitureType.StallDoor,
            FurnitureType.Toilet,
            FurnitureType.VanityUnit,
            FurnitureType.RestroomMirror,
            FurnitureType.HandDryer,
            FurnitureType.PaperTowelDispenser,
            FurnitureType.BabyChangingTable,
            FurnitureType.RestroomSign,
        ];
        Assert.All(expected, type => Assert.Contains(type, types));
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_restroom_shows_signs_of_wear_and_abandonment(int seed)
    {
        var (_, pieces) = RestroomOf(Showcase(seed));

        // Out-of-order caution sign
        Assert.Contains(pieces, p => p.Type == FurnitureType.OutOfOrderSign);
        // Stall door hanging off broken (tilted or rolled)
        Assert.Contains(pieces, p => p.Type == FurnitureType.StallDoor && (p.Tilt != 0f || p.Roll != 0f));
        // Discarded paper litter on the floor
        Assert.Contains(pieces, p => p.Type == FurnitureType.LitterPaper && p.Elevation == 0f);
    }

    [Fact]
    public void The_floor_plan_differs_between_seeds()
    {
        var plans = new HashSet<int>();
        var crackedMirrorFound = false;

        for (var seed = 1; seed <= 20; seed++)
        {
            var snapshot = Showcase(seed);
            var (restroom, pieces) = RestroomOf(snapshot);

            var partitions = pieces.Where(p => p.Type == FurnitureType.StallPartition).ToList();
            Assert.NotEmpty(partitions);
            var mean = partitions.Aggregate(Vector2.Zero, (sum, p) => sum + snapshot.Space.Delta(restroom.Centre, p.Position)) / partitions.Count;

            // Characterize plan by partition cluster location:
            // Plan 0: mean Y < -1.0 (North stalls)
            // Plan 1: mean X < -2.0 (West stalls)
            // Plan 2: split stalls (mean X near -3.3, mean Y near 0)
            if (mean.Y < -1.5f)
                plans.Add(0);
            else if (mean.X < -3.5f)
                plans.Add(1);
            else
                plans.Add(2);

            if (pieces.Any(p => p.Type == FurnitureType.RestroomMirror && p.Variant == 1))
                crackedMirrorFound = true;
        }

        Assert.True(plans.Count >= 2, $"Expected at least 2 distinct floor plans, got {plans.Count}.");
        Assert.True(crackedMirrorFound, "At least one floor plan should show a cracked mirror (variant 1).");
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void A_normal_store_keeps_its_old_restrooms_and_has_no_store_restroom_yet(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1));
        var snapshot = sim.Snapshot;

        Assert.DoesNotContain(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.StoreRestroom);
        Assert.InRange(snapshot.PointsOfInterest.Count(p => p.Type == PointOfInterestType.Restroom), 9, 27);
    }
}
