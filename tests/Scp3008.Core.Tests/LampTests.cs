using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class LampTests
{
    [Fact]
    public void Display_lamps_keep_their_light_radius_when_the_store_closes_and_reopens()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.ClearFogBanks();
        var lamps = sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp).ToArray();
        Assert.NotEmpty(lamps);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.AdvanceTo(TimeSpan.FromHours(23));
        Assert.Equal(0.0, sim.Snapshot.StoreLightLevel);
        Assert.Single(events.OfType<ClosingTimeBegan>());
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp));
        Assert.All(lamps, p => Assert.Equal(p.Type == FurnitureType.TableLamp ? 3f : 5f,
            FurnitureCatalog.Spec(p.Type).LightRadius));

        sim.AdvanceTo(TimeSpan.FromHours(8));
        Assert.Equal(1.0, sim.Snapshot.StoreLightLevel);
        Assert.Single(events.OfType<StoreReopened>());
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp));
    }

    private static readonly FurnitureType[] LampTypes = [FurnitureType.TableLamp, FurnitureType.FloorLamp];

    [Fact]
    public void Lamps_give_light_and_other_furniture_does_not()
    {
        foreach (var type in Enum.GetValues<FurnitureType>())
        {
            var spec = FurnitureCatalog.Spec(type);
            Assert.Equal(LampTypes.Contains(type), spec.IsLamp);
            Assert.Equal(spec.IsLamp, spec.LightRadius > 0f);
        }
        Assert.True(FurnitureCatalog.Spec(FurnitureType.FloorLamp).LightRadius > FurnitureCatalog.Spec(FurnitureType.TableLamp).LightRadius);
    }

    [Fact]
    public void The_lighting_departments_are_full_of_lamps()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        var snapshot = sim.Snapshot;

        var lampsByDepartment = snapshot.Furniture
            .Where(p => FurnitureCatalog.Spec(p.Type).IsLamp)
            .GroupBy(p => snapshot.Departments.At(p.Position).Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var lightingArea = snapshot.Departments.All.Where(d => d.Type == DepartmentType.LightingAndElectrical).Sum(d => d.Area);
        Assert.True(lampsByDepartment[DepartmentType.LightingAndElectrical] / lightingArea > 0.01f,
            "expected more than one Lamp per 100 m² in Lighting & electrical");
        Assert.Equal(lampsByDepartment.Keys.Max(k => lampsByDepartment[k]), lampsByDepartment[DepartmentType.LightingAndElectrical]);
    }
}
