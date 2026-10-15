using Scp3008.Core;
using Xunit.Abstractions;

namespace Scp3008.Core.Tests;

public class FurnitureRebalanceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Worlds =>
        from seed in new[] { 3008, 42, 1, -1, 2026 }
        from layout in PillarLayouts.All
        select new object[] { seed, layout };

    [Fact]
    public void The_benchmark_store_has_more_armchairs_sofas_and_tables_than_before_the_rebalance()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromSeconds(1));
        var pieces = sim.Snapshot.Furniture;
        // Measured at the ticket's fixed point, not recomputed from generation internals.
        Assert.True(pieces.Count(p => p.Type == FurnitureType.Armchair) > 3_240);
        Assert.True(pieces.Count(p => p.Type == FurnitureType.Sofa) > 2_632);
        var tables = new[] { FurnitureType.CoffeeTable, FurnitureType.DiningTable, FurnitureType.MeetingTable,
            FurnitureType.GardenTable, FurnitureType.KidsTable, FurnitureType.DisplayTable, FurnitureType.PlantTable };
        Assert.True(pieces.Count(p => tables.Contains(p.Type)) > 17_915, "Table availability must also increase, including lost electrical Display tables.");
        Assert.InRange(pieces.Count, 410_806 * 0.9, 410_806 * 1.1);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Lamps_are_occasional_except_in_electrical_departments_on_both_store_sizes(int seed, PillarLayout layout)
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: seed, PillarLayout: layout, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromSeconds(1));
        var snapshot = sim.Snapshot;
        var lamps = snapshot.Furniture.Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p).IsLamp).ToList();
        var share = lamps.Count / (double)snapshot.Furniture.Count;
        output.WriteLine($"{seed} / {layout.Name()}: {snapshot.Furniture.Count} Furniture, {lamps.Count} Lamps, {share:P4} Lamp pieces");
        Assert.InRange(share, 0.03, 0.04);
        Assert.Equal(new HashSet<FurnitureType> { FurnitureType.TableLamp, FurnitureType.FloorLamp, FurnitureType.DeskLamp, FurnitureType.ArcLamp },
            lamps.Select(p => p.Type).ToHashSet());
        var density = snapshot.Departments.All.GroupBy(d => d.Type).ToDictionary(g => g.Key,
            g => lamps.Count(p => snapshot.Departments.At(p.Position).Type == g.Key) / g.Sum(d => (double)d.Area));
        Assert.Equal(DepartmentType.LightingAndElectrical, density.MaxBy(d => d.Value).Key);
        Assert.Contains(lamps, p => snapshot.Departments.At(p.Position).Type == DepartmentType.Showrooms);
        Assert.Contains(lamps, p => snapshot.Departments.At(p.Position).Type == DepartmentType.Workspaces);
        Assert.All(lamps, p => Assert.Contains(p.Type, ThemeCatalog.Spec(snapshot.Displays[p.DisplayId].Theme).Types));
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Electrical_departments_are_a_few_small_regions_covering_two_to_three_percent(int seed, PillarLayout layout)
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: seed, PillarLayout: layout, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromSeconds(1));
        var snapshot = sim.Snapshot;
        var electrical = snapshot.Departments.All.Where(d => d.Type == DepartmentType.LightingAndElectrical).ToList();
        var share = electrical.Sum(d => (double)d.Area) / (snapshot.Space.Size * snapshot.Space.Size);
        output.WriteLine($"{seed} / {layout.Name()}: {share:P4} electrical floor, {electrical.Count} regions");
        Assert.InRange(share, 0.02, 0.03);
        Assert.InRange(electrical.Count, 2, layout.Space() == StoreSpace.Standard ? 8 : 16);
        Assert.All(electrical, d => Assert.InRange(d.Area / (double)(snapshot.Space.Size * snapshot.Space.Size), 0.0002, 0.025));
    }
}
