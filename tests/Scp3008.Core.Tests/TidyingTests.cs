using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class TidyingTests
{
    private static Simulation Arena()
    {
        var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17,
            Restock: new RestockConfig { FurnitureHours = 0 }));
        return sim;
    }

    private static Vector2 PositionOf(Simulation sim, int id) => sim.Snapshot.Furniture.Single(p => p.Id == id).Position;

    // A Staff standing at a place all night, and the night coming on.
    private static void StaffWatchAt(Simulation sim, Vector2 at)
    {
        sim.Scenario.PlaceStaff(at, [at, at + new Vector2(0.5f, 0)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
    }

    private static List<FurnitureTidied> Night(Simulation sim)
    {
        var tidied = new List<FurnitureTidied>();
        using var sub = sim.Subscribe(e => { if (e is FurnitureTidied t) tidied.Add(t); });
        sim.Advance(TimeSpan.FromMinutes(30));
        return tidied;
    }

    private static bool Back(Simulation sim, FurniturePiece piece) => sim.Snapshot.Space.Distance(PositionOf(sim, piece.Id), piece.Position) < 0.01f;

    [Fact]
    public void A_piece_moved_a_short_distance_and_left_unguarded_is_returned_overnight()
    {
        using var sim = Arena();
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(40, 100));
        var far = sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(100, 60));
        sim.Scenario.MoveFurniture(piece.Id, new(43, 101), 0);
        sim.Scenario.MoveFurniture(far.Id, new(103, 61), 0);
        // Not by day.
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        sim.Scenario.PlaceStaff(new(41, 100), [new(41, 100), new(42, 100)]);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.False(Back(sim, piece));

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var tidied = Night(sim);
        Assert.True(Back(sim, piece));
        Assert.Equal(piece.Id, Assert.Single(tidied).FurnitureId);
        // A piece no Staff passes stays where it was left.
        Assert.False(Back(sim, far));
    }

    private static Village BuiltVillage(Simulation sim, Vector2 at, Personality personality, bool supply)
    {
        var members = Enumerable.Range(0, 4).Select(i => sim.Scenario.PlaceVillager(at + new Vector2(i, 0))).ToArray();
        foreach (var m in members) sim.Scenario.EquipGear(m.Id, GearKind.ShoppingCart);
        var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), personality);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        var plan = sim.Scenario.PlanBarricade(village.Id);
        for (var i = 0; i < (supply ? plan.Slots.Count + 3 : 0); i++)
            sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, at + new Vector2(-22 + i % 12 * 2.2f, -24 + i / 12 * 2.5f), tint: i % 12);
        return village;
    }

    private static void RunUntil(Simulation sim, Func<bool> done)
    {
        for (var i = 0; i < 60000 && !done(); i++)
        {
            sim.AdvanceTicks(1);
            // Builders work by day: keep the Staff, who would take them, away.
            if (sim.Snapshot.Phase == DayPhase.ClosingTime) sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(8));
        }
        Assert.True(done(), "Construction did not finish in time: " + string.Join("; ", sim.Snapshot.Villages.Select(v => string.Join(",", v.Walls.Select(w => $"{w.Slots.Count(x => x.FurnitureId is not null)}/{w.Slots.Count}")) + " yard " + v.FurnitureYard?.Position + " villagers " + v.VillagerIds.Count)) + " day " + sim.Snapshot.Clock.Day + " widths " + string.Join(",", sim.Snapshot.Villages.SelectMany(v => v.Walls.SelectMany(w => w.Slots.Select(x => x.Width.ToString("0.0")))).Distinct()));
    }

    [Fact]
    public void Furniture_built_into_a_Village_or_lying_inside_its_walls_is_not_Tidied()
    {
        using var sim = Arena();
        var at = new Vector2(30, 104);
        var village = BuiltVillage(sim, at, Personality.Cautious, true);
        RunUntil(sim, () => sim.Snapshot.Villages[village.Id].Walls[0].Complete);
        var wall = sim.Snapshot.Villages[village.Id].Walls[0];
        var built = wall.Slots.Select(s => s.FurnitureId!.Value).ToDictionary(id => id, id => PositionOf(sim, id));
        // Guards hold off the Staff, who would otherwise besiege the wall and break pieces out of it.
        foreach (var member in sim.Snapshot.Villages[village.Id].VillagerIds) sim.Scenario.AssignRole(member, Role.Guard);
        // The pieces were gathered from where they lay, so each has a place to go back to.
        var inside = sim.Scenario.PlaceFurniture(FurnitureType.Chair, at + new Vector2(2, 2));
        sim.Scenario.MoveFurniture(inside.Id, at + new Vector2(3, 2), 0);
        var outside = sim.Scenario.PlaceFurniture(FurnitureType.Chair, at + new Vector2(8, 0));
        sim.Scenario.MoveFurniture(outside.Id, at + new Vector2(9, 1), 0);

        // The Staff stands just outside the wall, close to all of it on that side.
        StaffWatchAt(sim, at + new Vector2(7.5f, 0));
        var tidied = Night(sim);

        Assert.True(Back(sim, outside), "The control piece lying outside was not Tidied.");
        Assert.DoesNotContain(tidied, t => built.ContainsKey(t.FurnitureId) || t.FurnitureId == inside.Id);
        Assert.All(built, p => Assert.Equal(p.Value, PositionOf(sim, p.Key)));
        Assert.Equal(at + new Vector2(3, 2), PositionOf(sim, inside.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unbarricaded_Furniture_yard_outside_the_walls_loses_pieces_but_one_with_its_own_barricade_does_not(bool barricaded)
    {
        using var sim = Arena();
        var at = new Vector2(30, 104);
        var village = BuiltVillage(sim, at, Personality.Bold, barricaded);
        var yard = sim.Scenario.PlanFurnitureYard(village.Id);
        Assert.False(yard.Inside);
        if (barricaded) sim.Scenario.CompleteWall(village.Id, yard.WallIndex!.Value);
        var left = sim.Scenario.PlaceFurniture(FurnitureType.Chair, yard.Position + new Vector2(0.5f, 0));
        sim.Scenario.MoveFurniture(left.Id, yard.Position + new Vector2(0.8f, 0.5f), 0);
        StaffWatchAt(sim, yard.Position + new Vector2(0, 4));
        Night(sim);
        Assert.Equal(!barricaded, Back(sim, left));
    }

    [Fact]
    public void A_Band_quick_barricade_can_be_Tidied()
    {
        using var sim = Arena();
        var at = new Vector2(40, 100);
        for (var i = 0; i < 8; i++)
            sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, at + new Vector2(-6 + i * 1.5f, 5));
        var band = sim.Scenario.PlaceBand([at, at + new Vector2(1, 0), at + new Vector2(0, 1)], personality: Personality.Cautious);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(21.48));
        sim.Advance(TimeSpan.FromHours(1));
        var ring = sim.Snapshot.Bands.Single(b => b.Id == band.Id).Barricade;
        Assert.Equal(8, ring.Count);
        // While the Band shelters behind it the barricade is guarded; once the Staff have taken the Band, they tidy it away.
        foreach (var id in band.VillagerIds)
            for (var hit = 0; hit < 3 && sim.Snapshot.Villagers.Any(v => v.Id == id); hit++) sim.Scenario.StaffHitsVillager(id);
        Assert.Empty(sim.Snapshot.Villagers);
        var watch = PositionOf(sim, ring[0]) + new Vector2(3, 0);
        StaffWatchAt(sim, watch);
        var tidied = Night(sim);
        Assert.NotEmpty(tidied);
        Assert.All(tidied, t => Assert.Contains(t.FurnitureId, ring));
    }

    [Fact]
    public void A_displaced_piece_is_still_Tidied_after_saving_and_loading()
    {
        using var sim = Arena();
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(40, 100));
        sim.Scenario.MoveFurniture(piece.Id, new(43, 101), 0);
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        StaffWatchAt(loaded, new(41, 100));
        Night(loaded);
        Assert.True(Back(loaded, piece));
    }
}
