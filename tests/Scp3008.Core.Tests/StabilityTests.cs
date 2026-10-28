using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StabilityTests
{
    private static float Bottom(FurniturePiece piece) => FurnitureCatalog.Corners(piece).Min(c => c.Z);

    [Fact]
    public void Overloading_a_structure_collapses_it_and_the_pieces_fall()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var table = sim.Scenario.PlaceFurniture(FurnitureType.CoffeeTable, new(20, 104));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        var wardrobe = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(20, 104),
            elevation: FurnitureCatalog.Spec(FurnitureType.CoffeeTable).Height);
        sim.AdvanceTicks(1);

        var collapse = Assert.Single(events.OfType<StructureCollapsed>());
        Assert.Equal([table.Id], collapse.Broken);
        var fall = Assert.Single(collapse.Fallen);
        Assert.Equal(wardrobe.Id, fall.From.Id);
        var landed = sim.Snapshot.Furniture.Single(p => p.Id == wardrobe.Id);
        Assert.Equal(fall.To, landed);
        Assert.InRange(Bottom(landed), -0.01f, 0.01f);
        Assert.Equal(MathF.PI / 2, MathF.Abs(landed.Tilt), 3);
        Assert.InRange(landed.Damage, 0.01f, 1f);
        Assert.Equal(1f, sim.Snapshot.Furniture.Single(p => p.Id == table.Id).Damage);
    }

    [Fact]
    public void A_structure_within_its_limits_stays_standing()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var table = sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(20, 104));
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.TableLamp, new(20, 104), elevation: FurnitureCatalog.Spec(table).Height);
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20.5f, 104), elevation: FurnitureCatalog.Spec(table).Height);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Advance(TimeSpan.FromHours(1));

        Assert.Empty(events.OfType<StructureCollapsed>());
        Assert.Contains(table, sim.Snapshot.Furniture);
        Assert.Contains(lamp, sim.Snapshot.Furniture);
        Assert.Contains(chair, sim.Snapshot.Furniture);
    }

    [Fact]
    public void Damage_lowers_what_a_piece_can_carry()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var table = sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(20, 104));
        var chair = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new(20, 104), elevation: FurnitureCatalog.Spec(table).Height);
        sim.AdvanceTicks(1);
        Assert.Contains(chair, sim.Snapshot.Furniture);

        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Scenario.DamageFurniture(table.Id, 0.9f);
        sim.AdvanceTicks(1);
        var collapse = Assert.Single(events.OfType<StructureCollapsed>());
        Assert.Equal([table.Id], collapse.Broken);
        Assert.Equal(chair.Id, Assert.Single(collapse.Fallen).From.Id);
    }

    [Fact]
    public void Pieces_left_with_nothing_under_them_fall_and_hung_pieces_stay()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var table = sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(20, 104));
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.TableLamp, new(20, 104), elevation: FurnitureCatalog.Spec(table).Height);
        // Hung on nothing in particular, as a wall clock hangs: it stays where it was put.
        var hung = sim.Scenario.PlaceFurniture(FurnitureType.TableLamp, new(24, 104), elevation: 2f);
        sim.AdvanceTicks(1);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Scenario.RemoveFurniture(p => p.Id == table.Id);
        sim.AdvanceTicks(1);

        var collapse = Assert.Single(events.OfType<StructureCollapsed>());
        Assert.Empty(collapse.Broken);
        Assert.Equal(lamp.Id, Assert.Single(collapse.Fallen).From.Id);
        Assert.InRange(Bottom(sim.Snapshot.Furniture.Single(p => p.Id == lamp.Id)), -0.01f, 0.01f);
        Assert.Contains(hung, sim.Snapshot.Furniture);
    }

    [Fact]
    public void Taking_pieces_from_the_rack_canyon_can_collapse_it()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, LandmarkShowcase: true));
        var canyon = sim.Snapshot.Landmarks.Single(l => l.Type == LandmarkType.RackCanyon);
        var racks = sim.Snapshot.Furniture.Where(p => p.LandmarkId == canyon.Id && p.Type == FurnitureType.PalletRack).ToList();
        // The tallest stack: a rack on the floor with the most racks standing on top of it.
        var bottom = racks.Where(p => p.Elevation < 0.05f)
            .MaxBy(b => racks.Count(r => r.Elevation > 0.05f && sim.Snapshot.Space.Distance(r.Position, b.Position) < 0.1f))!;
        var above = racks.Where(r => r.Elevation > 0.05f && sim.Snapshot.Space.Distance(r.Position, bottom.Position) < 0.1f).ToList();
        Assert.NotEmpty(above);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Scenario.RemoveFurniture(p => p.Id == bottom.Id);
        sim.AdvanceTicks(1);

        var collapse = Assert.Single(events.OfType<StructureCollapsed>());
        Assert.Superset(above.Select(r => r.Id).ToHashSet(), collapse.Fallen.Select(f => f.From.Id).ToHashSet());
        Assert.All(above, r => Assert.InRange(Bottom(sim.Snapshot.Furniture.Single(p => p.Id == r.Id)), -0.01f, 0.01f));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("rack canyon") && line.Contains("support was taken away"));
    }

    [Fact]
    public void A_generated_Store_stands_when_a_piece_in_it_moves()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, LandmarkShowcase: true));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        // A piece placed in every Landmark sets off a check of the structures around it.
        foreach (var landmark in sim.Snapshot.Landmarks)
            sim.Scenario.PlaceFurniture(FurnitureType.TableLamp, landmark.Centre, elevation: 20f);
        sim.AdvanceTicks(1);
        Assert.Empty(events.OfType<StructureCollapsed>());
    }

    [Fact]
    public void Damage_survives_save_and_load()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var table = sim.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(20, 104));
        sim.Scenario.DamageFurniture(table.Id, 0.4f);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(0.4f, loaded.Snapshot.Furniture.Single(p => p.Id == table.Id).Damage);
    }
}
