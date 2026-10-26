using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>Locked acceptance tests of ticket 08k: the realistic Lost and found service point, built in the Point of interest showcase only.</summary>
public class LostAndFoundTests
{
    private static WorldSnapshot Showcase(int seed)
    {
        return TestStores.Get(seed, pointOfInterestShowcase: true);
    }

    private static (PointOfInterest LostAndFound, List<FurniturePiece> Pieces) LostAndFoundOf(WorldSnapshot snapshot)
    {
        var laf = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.LostAndFound);
        return (laf, snapshot.Furniture.Where(p => p.PointOfInterestId == laf.Id).ToList());
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void The_showcase_has_one_6_5_by_5_metre_lost_and_found_(int seed)
    {
        var snapshot = Showcase(seed);

        var (laf, _) = LostAndFoundOf(snapshot);
        Assert.Equal(new Vector2(6.5f, 5.0f), laf.Size);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_new_lost_and_found_has_a_counter_labelled_boxes_on_shelves_coat_rack_and_notice_board(int seed)
    {
        var (_, pieces) = LostAndFoundOf(Showcase(seed));
        var types = pieces.Select(p => p.Type).ToHashSet();

        FurnitureType[] expected =
        [
            FurnitureType.ServiceCounter,
            FurnitureType.ServiceBell,
            FurnitureType.ShelfUnit,
            FurnitureType.LabelledBox,
            FurnitureType.CoatRack,
            FurnitureType.UnsortedCart,
            FurnitureType.NoticeBoard,
            FurnitureType.LostAndFoundSign,
        ];
        Assert.All(expected, type => Assert.Contains(type, types));

        // Labelled boxes on shelves (elevated off the floor)
        Assert.Contains(pieces, p => p.Type == FurnitureType.LabelledBox && p.Elevation > 0.4f);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_lost_and_found_shows_signs_of_abandonment(int seed)
    {
        var (_, pieces) = LostAndFoundOf(Showcase(seed));

        // Hand-written "back in 5 min" note left on the counter
        Assert.Contains(pieces, p => p.Type == FurnitureType.Note && p.Elevation >= 1.0f);
        // Service bell left on the counter
        Assert.Contains(pieces, p => p.Type == FurnitureType.ServiceBell && p.Elevation >= 1.0f);
        // Boxes spilled open (variant 1)
        Assert.Contains(pieces, p => p.Type == FurnitureType.LabelledBox && p.Variant == 1);
        // A toppled pile on the floor (tilted or rolled box)
        Assert.Contains(pieces, p => p.Type == FurnitureType.LabelledBox && (p.Tilt != 0f || p.Roll != 0f));
    }

    [Fact]
    public void The_floor_plan_differs_between_seeds()
    {
        var plans = new HashSet<int>();

        for (var seed = 1; seed <= 20; seed++)
        {
            var snapshot = Showcase(seed);
            var (laf, pieces) = LostAndFoundOf(snapshot);

            var counter = pieces.First(p => p.Type == FurnitureType.ServiceCounter);
            var delta = snapshot.Space.Delta(laf.Centre, counter.Position);

            // Plan 0: counter at y = 0.4, x near -0.2 (rot = 0)
            // Plan 1: counter at x = -0.8, y = 0.5 (rot = -Quarter)
            // Plan 2: counter at x = 0.0, y = 0.3 (rot = 0)
            if (counter.Rotation != 0f)
                plans.Add(1);
            else if (delta.X < -0.1f)
                plans.Add(0);
            else
                plans.Add(2);
        }

        Assert.True(plans.Count >= 2, $"Expected at least 2 distinct floor plans, got {plans.Count}.");
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void A_normal_store_has_one_lost_and_found_per_sector(int seed)
    {
        var snapshot = TestStores.Get(seed);

        Assert.All(snapshot.Sectors, sector =>
            Assert.Equal(1, snapshot.PointsOfInterest.Count(p => p.Type == PointOfInterestType.LostAndFound && p.SectorId == sector.Id)));
    }
}
