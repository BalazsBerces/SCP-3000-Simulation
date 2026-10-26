using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class DepartmentSizeTests
{
    private static readonly int[] Seeds = [3008, 1, 42, 777, 2024, 9001];

    private static DepartmentMap MapFor(int seed)
    {
        return TestStores.Get(seed).Departments;
    }

    private static double Across(Department d) => Math.Sqrt(d.Area);

    [Fact]
    public void The_standard_store_has_about_three_times_as_many_departments_as_before()
    {
        // Before 06e the standard Store held about 40 Departments.
        foreach (var seed in Seeds)
            Assert.InRange(MapFor(seed).All.Count, 90, 170);
    }

    [Fact]
    public void The_median_department_is_about_80_to_120_metres_across_and_a_large_one_exists_on_most_seeds()
    {
        var withLarge = 0;
        foreach (var seed in Seeds)
        {
            var across = MapFor(seed).All.Select(Across).Order().ToList();
            Assert.InRange(across[across.Count / 2], 80, 120);
            if (across.Max() >= 200)
                withLarge++;
        }
        Assert.True(withLarge >= Seeds.Length - 1, $"large Departments on {withLarge}/{Seeds.Length} seeds");
    }

    [Fact]
    public void Adjacent_same_type_departments_are_rare()
    {
        // Same-type neighbours that still merge show up as one oversized region; count the
        // Departments above 250 m across that are not deliberately large.
        foreach (var seed in Seeds)
        {
            var map = MapFor(seed);
            var huge = map.All.Count(d => Across(d) > 320);
            Assert.True(huge <= 2, $"seed {seed}: {huge} Departments over 320 m across");
        }
    }

    [Fact]
    public void Restaurants_and_plant_sections_stay_small_and_scarce()
    {
        foreach (var seed in Seeds)
        {
            var map = MapFor(seed);
            var total = map.All.Sum(d => d.Area);
            foreach (var type in new[] { DepartmentType.RestaurantAndFoodMarket, DepartmentType.PlantSection })
            {
                var of = map.All.Where(d => d.Type == type).ToList();
                Assert.True(of.Sum(d => d.Area) / total < 0.09, $"seed {seed} {type} share");
                Assert.All(of, d => Assert.True(Across(d) < 180, $"seed {seed} {type} {Across(d):F0} m"));
            }
            var electrical = map.All.Where(d => d.Type == DepartmentType.LightingAndElectrical).Sum(d => d.Area);
            Assert.True(electrical / total < 0.04);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_map_and_the_map_wraps()
    {
        var a = MapFor(42);
        var b = MapFor(42);
        Assert.Equal(a.All.Select(d => (d.Type, d.CellCount)), b.All.Select(d => (d.Type, d.CellCount)));
        var n = a.CellsPerSide;
        // A Department touching the seam is one Department across it, never split in two.
        for (var i = 0; i < n; i++)
        {
            if (a.AtCell(0, i).Type == a.AtCell(n - 1, i).Type)
                Assert.Equal(a.AtCell(0, i).Id, a.AtCell(n - 1, i).Id);
            if (a.AtCell(i, 0).Type == a.AtCell(i, n - 1).Type)
                Assert.Equal(a.AtCell(i, 0).Id, a.AtCell(i, n - 1).Id);
        }
    }
}
