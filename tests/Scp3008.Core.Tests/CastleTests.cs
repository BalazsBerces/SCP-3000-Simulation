using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class CastleTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    // A fed Village (its Furniture yard inside, so no yard barricade) with carts (a double-door wardrobe needs one), its first barricade built of bookcases.
    private static Village BuiltBarricade(Simulation sim, Vector2 at)
    {
        var members = new[] { sim.Scenario.PlaceVillager(at), sim.Scenario.PlaceVillager(at + new Vector2(1, 0)) };
        foreach (var m in members) sim.Scenario.EquipGear(m.Id, GearKind.ShoppingCart);
        var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        var plan = sim.Scenario.PlanBarricade(village.Id);
        Supply(sim, at + new Vector2(-22, -24), FurnitureType.Bookcase, plan.Slots.Count + 3);
        Run(sim, () => sim.Snapshot.Villages[village.Id].Walls[0].Complete, 60000);
        return sim.Snapshot.Villages[village.Id];
    }

    private static void Supply(Simulation sim, Vector2 from, FurnitureType type, int count, int variant = 0)
    {
        for (var i = 0; i < count; i++)
            sim.Scenario.PlaceFurniture(type, from + new Vector2(i % 12 * 2.2f, i / 12 * 2.5f), variant: variant, tint: i % 12);
    }

    private static void Run(Simulation sim, Func<bool> done, int ticks)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done(), "Construction did not finish in time: " + Describe(sim));
    }

    private static string Describe(Simulation sim)
    {
        return string.Join("; ", sim.Snapshot.Villages.Select(v =>
            $"walls {string.Join(",", v.Walls.Select(w => $"{w.Slots.Count(s => s.FurnitureId is not null)}/{w.Slots.Count}"))} castle " +
            string.Join(",", v.Castle.Select(c => $"{c.Part}:{string.Join("", c.Slots.Select(s => s.FurnitureId is null ? "_" : "#"))}")))) +
            " tasks " + string.Join(",", sim.Snapshot.Villagers.Select(v => $"{v.Role}:{v.FurnitureTaskId}"));
    }

    private static void Grow(Simulation sim, Village village)
    {
        for (var i = 0; i < 4; i++)
            sim.Scenario.PlaceBand([village.Position], knownMap: new KnownMap
                { Villages = [new KnownVillage(village.Id, village.Position, sim.Snapshot.Clock)] });
        Run(sim, () => sim.Snapshot.Villages[village.Id].HomeWalls().Count() == 2, 3000);
    }

    [Fact]
    public void A_Village_grows_from_a_barricade_to_a_curtain_wall_with_Towers_a_Gatehouse_a_Keep_and_a_Moat()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        Assert.False(village.Walls[0].Battlements);
        Assert.Empty(village.Castle);
        Grow(sim, village);

        var planned = sim.Snapshot.Villages[village.Id];
        var curtain = planned.HomeWalls().ElementAt(1);
        Assert.True(curtain.Battlements);
        // Battlements: tall and short units alternate along the curtain wall.
        Assert.All(curtain.Slots.Where((_, i) => i % 2 == 0), s => Assert.True(s.MinimumHeight >= 1.8f));
        Assert.All(curtain.Slots.Where((_, i) => i % 2 == 1), s => Assert.True(s.MaximumHeight <= 1.2f));
        Assert.Equal(2 * Castle.GateHalfWidth, sim.Snapshot.Space.Distance(curtain.GateStart, curtain.GateEnd), 3);
        Assert.Equal(4, planned.Castle.Count(c => c.Part == CastlePart.Tower));
        Assert.Single(planned.Castle, c => c.Part == CastlePart.Gatehouse);
        Assert.Single(planned.Castle, c => c.Part == CastlePart.Keep);
        Assert.Single(planned.Moats);
        Assert.False(planned.Moats[0].Water);
        Assert.NotNull(planned.Heraldry);

        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 4000);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 4000);
        var talls = curtain.Slots.Count(s => s.MinimumHeight >= 1.8f);
        Supply(sim, new(8, 120), FurnitureType.Bookcase, talls + 6);
        Supply(sim, new(8, 130), FurnitureType.KitchenCabinet, curtain.Slots.Count - talls + 2, variant: 1);
        Supply(sim, new(36, 120), FurnitureType.ShelfUnit, 8);
        Supply(sim, new(36, 128), FurnitureType.TableLamp, 4);
        Supply(sim, new(36, 132), FurnitureType.Wardrobe, 1, variant: 1);
        Supply(sim, new(36, 136), FurnitureType.Dresser, 12);
        var placed = new List<CastlePiecePlaced>();
        using var sub = sim.Subscribe(e => { if (e is CastlePiecePlaced p) placed.Add(p); });
        // Carts wear out over a hundred hauls; whoever is left empty-handed fetches a fresh one (the double-door wardrobe needs it).
        Run(sim, () =>
        {
            foreach (var v in sim.Snapshot.Villagers.Where(v => v.VillageId == village.Id && v.Hand is null))
                sim.Scenario.EquipGear(v.Id, GearKind.ShoppingCart);
            return sim.Snapshot.Villages[village.Id].Castle.All(c => c.Complete);
        }, 150000);

        var castle = sim.Snapshot.Villages[village.Id];
        Assert.True(castle.HomeWalls().All(w => w.Complete));
        Assert.Equal(castle.Castle.Sum(c => c.Slots.Count), placed.Count);
        FurniturePiece Piece(BlueprintSlot s) => sim.Snapshot.Furniture.Single(p => p.Id == s.FurnitureId);
        float Top(BlueprintSlot s) => Piece(s).Elevation + FurnitureCatalog.Spec(Piece(s)).Height;
        foreach (var structure in castle.Castle)
            for (var i = 0; i < structure.Slots.Count; i++)
            {
                var piece = Piece(structure.Slots[i]);
                Assert.True(Castle.Fits(structure.Part, i, piece));
                Assert.Equal(structure.Slots[i].Position, piece.Position);
                var below = Castle.Support(structure.Part, i);
                Assert.Equal(below.Count == 0 ? 0 : below.Max(b => Top(structure.Slots[b])), piece.Elevation, 3);
            }
        var gate = castle.Castle.Single(c => c.Part == CastlePart.Gatehouse);
        Assert.Equal(FurnitureType.Wardrobe, Piece(gate.Slots[2]).Type);
        Assert.Equal(1, Piece(gate.Slots[2]).Variant);
        Assert.True(Piece(gate.Slots[2]).Elevation >= 1.8f);
        Assert.All(castle.Castle.Where(c => c.Part == CastlePart.Tower), t => Assert.True(FurnitureCatalog.Spec(Piece(t.Slots[2])).IsLamp));

        // The passage under the Gatehouse stays open: a visitor walks from the centre out through the gate.
        var visitor = sim.Scenario.PlaceVillager(castle.Position);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var outside = gate.Slots[2].Position + Vector2.Normalize(sim.Snapshot.Space.Delta(castle.Position, gate.Slots[2].Position)) * 6;
        sim.Scenario.SendVillager(visitor.Id, outside);
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.True(sim.Snapshot.Space.Distance(outside, sim.Snapshot.Villagers.Single(v => v.Id == visitor.Id).Position) < 1);

        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(castle.Castle.SelectMany(c => c.Slots), loaded.Snapshot.Villages[village.Id].Castle.SelectMany(c => c.Slots));
        Assert.Equal(castle.Heraldry, loaded.Snapshot.Villages[village.Id].Heraldry);
    }

    [Fact]
    public void A_Moat_dug_near_the_fountain_court_fills_with_water()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        sim.Scenario.PlaceWaterPoint(village.Position + new Vector2(0, -20));
        Grow(sim, village);

        var moat = Assert.Single(sim.Snapshot.Villages[village.Id].Moats);
        Assert.True(moat.Water);
        Assert.Equal(1, moat.Ring);
    }

    [Fact]
    public void Each_Village_bears_distinct_Heraldry_from_the_fabrics_around_it()
    {
        using var sim = Sandbox();
        Village Founded(Vector2 at, int field, int charge)
        {
            for (var i = 0; i < 6; i++) sim.Scenario.PlaceFurniture(FurnitureType.PlushBear, at + new Vector2(8 + i * 0.6f, 6), tint: field);
            for (var i = 0; i < 4; i++) sim.Scenario.PlaceFurniture(FurnitureType.PlushBear, at + new Vector2(8 + i * 0.6f, 8), tint: charge);
            var members = new[] { sim.Scenario.PlaceVillager(at), sim.Scenario.PlaceVillager(at + new Vector2(1, 0)) };
            var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray());
            sim.Scenario.PlanBarricade(village.Id);
            return sim.Snapshot.Villages[village.Id];
        }

        var red = Founded(new(30, 104), 3, 7);
        var blue = Founded(new(30, 40), 9, 1);
        var twin = Founded(new(94, 72), 3, 7);

        Assert.Equal((3, 7), (red.Heraldry!.Field, red.Heraldry.Charge));
        Assert.Equal((9, 1), (blue.Heraldry!.Field, blue.Heraldry.Charge));
        // The same local fabrics: the same colours, divided differently.
        Assert.Equal((3, 7), (twin.Heraldry!.Field, twin.Heraldry.Charge));
        Assert.NotEqual(red.Heraldry, twin.Heraldry);
    }
}
