using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>Locked acceptance tests of ticket 08j: the realistic Restroom, built in the Point of interest showcase only.</summary>
public class RestroomTests
{
    private static WorldSnapshot Showcase(int seed)
    {
        return TestStores.Get(seed, pointOfInterestShowcase: true);
    }

    private static (PointOfInterest Restroom, List<FurniturePiece> Pieces) RestroomOf(WorldSnapshot snapshot)
    {
        var restroom = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.StoreRestroom);
        return (restroom, snapshot.Furniture.Where(p => p.PointOfInterestId == restroom.Id).ToList());
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void The_showcase_has_one_10_4_by_6_metre_restroom(int seed)
    {
        var snapshot = Showcase(seed);

        var (restroom, _) = RestroomOf(snapshot);
        Assert.Equal(new Vector2(10.4f, 6.0f), restroom.Size);
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

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_walls_close_the_room_except_for_one_doorway(int seed)
    {
        var (restroom, pieces) = RestroomOf(Showcase(seed));
        // Each wall's span along its side, in metres from the room's centre; walls turned a quarter lie along y.
        var spans = pieces.Where(p => p.Type == FurnitureType.TiledWall).Select(p =>
        {
            var length = FurnitureCatalog.Spec(p).Width;
            var along = MathF.Abs(MathF.Sin(p.Rotation)) > 0.5f
                ? p.Position.Y - restroom.Centre.Y : p.Position.X - restroom.Centre.X;
            var side = MathF.Abs(MathF.Sin(p.Rotation)) > 0.5f
                ? (p.Position.X > restroom.Centre.X ? "east" : "west")
                : (p.Position.Y > restroom.Centre.Y ? "south" : "north");
            return (side, from: along - length / 2f, to: along + length / 2f);
        }).ToList();

        float Gaps(string side, float start, float end)
        {
            var gap = 0f;
            var at = start;
            foreach (var (_, from, to) in spans.Where(w => w.side == side).OrderBy(w => w.from))
            {
                Assert.True(from >= at - 0.01f - 0f || to <= at + 0.01f, $"{side} walls overlap at {from:0.00}");
                gap += MathF.Max(0f, from - at);
                at = MathF.Max(at, to);
            }
            return gap + MathF.Max(0f, end - at);
        }

        // Front and back walls reach the corners, the side walls run between them.
        var gaps = new[] { Gaps("north", -5.2f, 5.2f), Gaps("south", -5.2f, 5.2f), Gaps("west", -2.85f, 2.85f), Gaps("east", -2.85f, 2.85f) };
        var open = gaps.Where(g => g > 0.01f).ToList();
        Assert.True(open.Count <= 1, $"Gaps in more than one wall: {string.Join(", ", gaps.Select(g => g.ToString("0.00")))}");
        Assert.All(open, g => Assert.InRange(g, 0.9f, 2.7f));
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
    public void A_normal_store_has_one_to_three_restrooms_per_sector(int seed)
    {
        var snapshot = TestStores.Get(seed);

        Assert.All(snapshot.Sectors, sector =>
            Assert.InRange(snapshot.PointsOfInterest.Count(p => p.Type == PointOfInterestType.StoreRestroom && p.SectorId == sector.Id), 1, 3));
    }
}
