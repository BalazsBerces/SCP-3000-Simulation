using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>Locked acceptance tests of ticket 08i: the Cafeteria, built in the Point of interest showcase only.</summary>
public class CafeteriaTests
{
    private static WorldSnapshot Showcase(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        return sim.Snapshot;
    }

    private static (PointOfInterest Cafeteria, List<FurniturePiece> Pieces) CafeteriaOf(WorldSnapshot snapshot)
    {
        var cafeteria = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.Cafeteria);
        return (cafeteria, snapshot.Furniture.Where(p => p.PointOfInterestId == cafeteria.Id).ToList());
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void The_showcase_has_one_24_by_20_metre_cafeteria_in_place_of_the_canteen(int seed)
    {
        var snapshot = Showcase(seed);

        var (cafeteria, _) = CafeteriaOf(snapshot);
        Assert.Equal(new Vector2(24f, 20f), cafeteria.Size);
        Assert.DoesNotContain(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.Canteen);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_cafeteria_has_a_serving_line_and_a_dining_zone(int seed)
    {
        var (_, pieces) = CafeteriaOf(Showcase(seed));
        var types = pieces.Select(p => p.Type).ToHashSet();

        FurnitureType[] expected =
        [
            FurnitureType.HotFoodCounter, FurnitureType.DessertCounter, FurnitureType.TrayRail, FurnitureType.TrayStation,
            FurnitureType.CoffeeMachine, FurnitureType.Till, FurnitureType.TrayTrolley, FurnitureType.HighChair,
            FurnitureType.DiningTable, FurnitureType.Chair, FurnitureType.Tray,
        ];
        Assert.All(expected, type => Assert.Contains(type, types));
        Assert.True(pieces.Count(p => p.Type == FurnitureType.DiningTable) >= 30, "A large dining hall seats many: at least 30 tables.");
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_hot_food_counters_hold_meatballs_and_the_dessert_counter_almond_cake(int seed)
    {
        var snapshot = Showcase(seed);
        var (_, pieces) = CafeteriaOf(snapshot);
        var byId = snapshot.Furniture.ToDictionary(p => p.Id);
        var counters = pieces.Where(p => p.Type is FurnitureType.HotFoodCounter or FurnitureType.DessertCounter).Select(p => p.Id).Order();

        Assert.Equal(counters, snapshot.CounterMeals.Select(m => m.CounterId).Order());
        Assert.All(snapshot.CounterMeals, meal => Assert.Equal(
            byId[meal.CounterId].Type == FurnitureType.HotFoodCounter ? MealKind.Meatballs : MealKind.AlmondCake, meal.Kind));
        Assert.Contains(snapshot.CounterMeals, m => m.Kind == MealKind.Meatballs);
        Assert.Contains(snapshot.CounterMeals, m => m.Kind == MealKind.AlmondCake);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(7)]
    public void The_cafeteria_looks_abandoned_mid_meal(int seed)
    {
        var snapshot = Showcase(seed);
        var (_, pieces) = CafeteriaOf(snapshot);
        var tables = pieces.Where(p => p.Type == FurnitureType.DiningTable).ToList();

        Assert.Contains(pieces, p => p.Type == FurnitureType.Chair && p.Tilt != 0f);
        Assert.Contains(pieces, p => p.Type == FurnitureType.MealPlate && p.Elevation > 0.7f
            && tables.Any(t => snapshot.Space.Delta(t.Position, p.Position).Length() < 1.2f));
        Assert.Contains(pieces, p => p.Type == FurnitureType.Spill && p.Elevation == 0f);
        Assert.Contains(pieces, p => p.Type == FurnitureType.Note);
    }

    [Fact]
    public void The_floor_plan_differs_between_seeds()
    {
        var plans = new HashSet<(int, int)>();
        for (var seed = 1; seed <= 20; seed++)
        {
            var snapshot = Showcase(seed);
            var (cafeteria, pieces) = CafeteriaOf(snapshot);
            var hot = pieces.Where(p => p.Type == FurnitureType.HotFoodCounter).ToList();
            Assert.NotEmpty(hot);
            var mean = hot.Aggregate(Vector2.Zero, (sum, p) => sum + snapshot.Space.Delta(cafeteria.Centre, p.Position)) / hot.Count;
            plans.Add(((int)MathF.Round(mean.X), (int)MathF.Round(mean.Y)));
        }

        Assert.True(plans.Count >= 2, "Every seed put the serving line in the same place.");
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    public void A_normal_store_keeps_its_canteens_and_has_no_cafeteria_yet(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1));
        var snapshot = sim.Snapshot;

        Assert.DoesNotContain(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.Cafeteria);
        Assert.InRange(snapshot.PointsOfInterest.Count(p => p.Type == PointOfInterestType.Canteen), 3, 4);
        Assert.Empty(snapshot.CounterMeals);
    }
}
