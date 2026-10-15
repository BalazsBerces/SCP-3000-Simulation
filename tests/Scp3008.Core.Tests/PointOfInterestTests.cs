using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PointOfInterestTests
{
    private static readonly int[] Seeds = [3008, 42, -1, 1, 2, 3, 4, 5];

    public static TheoryData<PillarLayout, int> LayoutsAndSeeds()
    {
        var data = new TheoryData<PillarLayout, int>();
        foreach (var layout in Enum.GetValues<PillarLayout>())
            foreach (var seed in new[] { 3008, 42, 7 })
                data.Add(layout, seed);
        return data;
    }

    private static WorldSnapshot Generate(int seed, PillarLayout layout = PillarLayout.ThreeByThree, bool showcase = false)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, LandmarkShowcase: showcase));
        return sim.Snapshot;
    }

    private static IEnumerable<FurniturePiece> PiecesOf(WorldSnapshot snapshot, PointOfInterest poi) =>
        snapshot.Furniture.Where(p => p.PointOfInterestId == poi.Id);

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.CornersLarge)]
    public void The_same_seed_and_pillar_layout_give_the_same_points_of_interest(PillarLayout layout)
    {
        var first = Generate(42, layout);
        using var again = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 9, PillarLayout: layout));
        again.Advance(TimeSpan.FromHours(7));
        var second = again.Snapshot;

        Assert.NotEmpty(first.PointsOfInterest);
        Assert.Equal(first.PointsOfInterest, second.PointsOfInterest);
        Assert.Equal(first.Furniture.Where(p => p.PointOfInterestId >= 0), second.Furniture.Where(p => p.PointOfInterestId >= 0));
    }

    [Fact]
    public void Another_seed_puts_them_elsewhere()
    {
        var places = Seeds.Select(s => string.Join(";", Generate(s).PointsOfInterest.Select(p => $"{p.Type}@{p.Centre}"))).Distinct();
        Assert.Equal(Seeds.Length, places.Count());
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Counts_per_sector_and_per_store_match_the_ranges_on_every_layout(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var pois = snapshot.PointsOfInterest;
        Assert.Equal(Enumerable.Range(0, pois.Count), pois.Select(p => p.Id));

        foreach (var sector in snapshot.Sectors)
        {
            int Count(PointOfInterestType type) => pois.Count(p => p.Type == type && p.SectorId == sector.Id);
            Assert.InRange(Count(PointOfInterestType.Restroom), 1, 3);
            Assert.InRange(Count(PointOfInterestType.SetDinnerTable), 2, 3);
            Assert.Equal(1, Count(PointOfInterestType.LostAndFoundDesk));
        }
        Assert.InRange(pois.Count(p => p.Type == PointOfInterestType.Canteen), 3, 4);
        Assert.InRange(pois.Count(p => p.Type == PointOfInterestType.ShoppingCartGraveyard), 1, 4);
        Assert.All(pois.Where(p => p.Type == PointOfInterestType.Canteen),
            p => Assert.Equal(DepartmentType.RestaurantAndFoodMarket, snapshot.Departments.At(p.Centre).Type));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_point_of_interest_keeps_to_its_sector_and_clear_of_landmarks_pillars_walkway_rings_and_each_other(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var space = snapshot.Space;
        foreach (var poi in snapshot.PointsOfInterest)
        {
            var sector = snapshot.Sectors[poi.SectorId];
            Assert.Equal(sector.Id, snapshot.SectorAt(poi.Centre).Id);
            var low = poi.Centre - poi.Size / 2f;
            var high = poi.Centre + poi.Size / 2f;
            Assert.True(low.X >= sector.Origin.X && low.Y >= sector.Origin.Y &&
                        high.X <= sector.Origin.X + sector.Size && high.Y <= sector.Origin.Y + sector.Size,
                $"{poi.Type} reaches out of Sector {sector.Name}.");
            foreach (var pillar in snapshot.Pillars)
            {
                var delta = space.Delta(pillar.Position, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= pillar.ClearHalfWidth + poi.Size.X / 2f ||
                            MathF.Abs(delta.Y) >= pillar.ClearHalfWidth + poi.Size.Y / 2f,
                    $"{poi.Type} covers Pillar {pillar.Coordinate} or its walkway ring.");
            }
            foreach (var landmark in snapshot.Landmarks)
            {
                var delta = space.Delta(landmark.Centre, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= (landmark.Size.X + poi.Size.X) / 2f ||
                            MathF.Abs(delta.Y) >= (landmark.Size.Y + poi.Size.Y) / 2f,
                    $"{poi.Type} overlaps the {landmark.Type}.");
            }
            foreach (var other in snapshot.PointsOfInterest.Where(o => o.Id > poi.Id))
            {
                var delta = space.Delta(other.Centre, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= (other.Size.X + poi.Size.X) / 2f ||
                            MathF.Abs(delta.Y) >= (other.Size.Y + poi.Size.Y) / 2f,
                    $"{poi.Type} overlaps another {other.Type}.");
            }
        }
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(5)]
    public void Each_point_of_interest_is_individual_furniture_inside_its_rectangle_that_no_display_enters(int seed)
    {
        var snapshot = Generate(seed);
        Assert.Equal(Enumerable.Range(0, snapshot.Furniture.Count), snapshot.Furniture.Select(p => p.Id));
        foreach (var poi in snapshot.PointsOfInterest)
        {
            var pieces = PiecesOf(snapshot, poi).ToList();
            Assert.True(pieces.Count >= 5, $"{poi.Type} has only {pieces.Count} pieces.");
            Assert.All(pieces, p =>
            {
                Assert.Equal(-1, p.DisplayId);
                Assert.Equal(-1, p.LandmarkId);
                foreach (var corner in FurnitureCatalog.Corners(p))
                {
                    var delta = snapshot.Space.Delta(poi.Centre, new Vector2(corner.X, corner.Y));
                    Assert.True(MathF.Abs(delta.X) <= poi.Size.X / 2f + 0.01f && MathF.Abs(delta.Y) <= poi.Size.Y / 2f + 0.01f,
                        $"A {p.Type} of the {poi.Type} sticks out of it.");
                }
            });
        }
        var intruders = snapshot.Furniture.Where(p => p.PointOfInterestId < 0 && snapshot.PointsOfInterest.Any(poi =>
        {
            var delta = snapshot.Space.Delta(poi.Centre, p.Position);
            return MathF.Abs(delta.X) < poi.Size.X / 2f && MathF.Abs(delta.Y) < poi.Size.Y / 2f;
        })).Take(5).ToList();
        Assert.True(intruders.Count == 0, "Other Furniture inside a Point of interest: " + string.Join("; ", intruders));
    }

    [Fact]
    public void Each_kind_is_made_of_the_furniture_it_is_about()
    {
        var snapshots = Seeds.Select(s => Generate(s)).ToList();
        HashSet<FurnitureType> Types(PointOfInterestType kind) => snapshots
            .SelectMany(s => s.PointsOfInterest.Where(p => p.Type == kind).SelectMany(p => PiecesOf(s, p)))
            .Select(p => p.Type).ToHashSet();

        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Toilet, FurnitureType.VanityUnit }, Types(PointOfInterestType.Restroom));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Counter, FurnitureType.MealPlate, FurnitureType.DiningTable, FurnitureType.Chair }, Types(PointOfInterestType.Canteen));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.DiningTable, FurnitureType.Chair, FurnitureType.MealPlate }, Types(PointOfInterestType.SetDinnerTable));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Desk, FurnitureType.FilingCabinet }, Types(PointOfInterestType.LostAndFoundDesk));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.ShoppingCart }, Types(PointOfInterestType.ShoppingCartGraveyard));
        // Meals are laid on tables or counters, not on the floor.
        Assert.All(snapshots.SelectMany(s => s.Furniture.Where(p => p.Type == FurnitureType.MealPlate)), p => Assert.True(p.Elevation > 0.5f));
    }

    [Fact]
    public void The_landmark_showcase_has_none()
    {
        Assert.Empty(Generate(3008, showcase: true).PointsOfInterest);
    }
}
