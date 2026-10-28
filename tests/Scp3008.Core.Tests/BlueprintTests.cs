using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class BlueprintTests
{
    [Theory]
    [InlineData(FurnitureType.Bookcase, 0)]
    [InlineData(FurnitureType.KitchenCabinet, 2)]
    public void Builders_complete_a_barricade_with_only_fitting_Furniture_one_piece_at_a_time(FurnitureType type, int variant)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));
        var members = new[] { sim.Scenario.PlaceVillager(new(30, 104)), sim.Scenario.PlaceVillager(new(31, 104)) };
        var village = sim.Scenario.PlaceVillage(new(30, 104), members.Select(v => v.Id).ToArray(), Personality.Builders);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 100);
        var plan = sim.Scenario.PlanBarricade(village.Id);
        var bad = sim.Scenario.PlaceFurniture(FurnitureType.TableLamp, new(24, 104));
        var oversized = sim.Scenario.PlaceFurniture(FurnitureType.ShelfUnit, new(20, 104));
        var weak = sim.Scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 110));
        var fragile = sim.Scenario.PlaceFurniture(FurnitureType.FloorMirror, new(20, 114));
        // Three spare pieces: the Storehouse takes shelving before the wall does.
        foreach (var i in Enumerable.Range(0, plan.Slots.Count + 3))
            sim.Scenario.PlaceFurniture(type, new(4 + i % 12 * 2, 80 + i / 12 * 3), variant: variant, tint: i % 12);
        var originals = sim.Snapshot.Furniture.ToDictionary(p => p.Id);
        var placed = new List<BlueprintPiecePlaced>();
        using var sub = sim.Subscribe(e => { if (e is BlueprintPiecePlaced p) placed.Add(p); });
        for (var i = 0; i < 60000 && !sim.Snapshot.Villages[0].Walls[0].Complete; i++)
        {
            sim.AdvanceTicks(1);
        }
        var wall = sim.Snapshot.Villages[0].Walls[0];
        Assert.True(wall.Complete, $"Placed {placed.Count}/{wall.Slots.Count}; builder {sim.Snapshot.Villagers.Single(v => v.Id == members[1].Id)}");
        Assert.Equal(wall.Slots.Count, placed.Count);
        Assert.Equal(placed.Count, placed.Select(p => p.At.Tick).Distinct().Count());
        Assert.All(wall.Slots, slot =>
        {
            var piece = sim.Snapshot.Furniture.Single(p => p.Id == slot.FurnitureId);
            Assert.True(slot.Fits(piece));
            Assert.Equal(slot.Position, piece.Position);
            Assert.NotEqual(bad.Id, piece.Id);
            Assert.DoesNotContain(piece.Id, new[] { oversized.Id, weak.Id, fragile.Id });
            Assert.Equal(type, piece.Type);
            Assert.Equal(originals[piece.Id] with { Position = piece.Position, Rotation = piece.Rotation }, piece);
        });
        var visitor = sim.Scenario.PlaceVillager(village.Position);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        sim.Scenario.SendVillager(visitor.Id, new(30, 94));
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(new Vector2(30, 94), sim.Snapshot.Villagers.Single(v => v.Id == visitor.Id).Position);
        for (var i = 0; i < 4; i++)
            sim.Scenario.PlaceBand([village.Position], knownMap: new KnownMap
                { Villages = [new KnownVillage(village.Id, village.Position, sim.Snapshot.Clock)] });
        // A builder busy gathering Furniture for the yard plans the next ring when it is free.
        for (var i = 0; i < 3000 && sim.Snapshot.Villages[0].HomeWalls().Count() < 2; i++) sim.AdvanceTicks(1);
        var grown = sim.Snapshot.Villages[0];
        var rings = grown.HomeWalls().ToArray();
        Assert.Equal(2, rings.Length);
        Assert.True(rings[0].Complete);
        Assert.Equal(wall.Outline, rings[1].Outline);
        Assert.True(sim.Snapshot.Space.Distance(grown.Position, rings[1].Vertices[0]) >
            sim.Snapshot.Space.Distance(grown.Position, wall.Vertices[0]));
    }

    [Fact]
    public void Partially_built_and_reserved_slots_survive_loading_and_continue_identically()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var a = sim.Scenario.PlaceVillager(new(30, 104));
        var b = sim.Scenario.PlaceVillager(new(31, 104));
        var village = sim.Scenario.PlaceVillage(a.Position, [a.Id, b.Id], Personality.Builders);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 100);
        sim.Scenario.PlanBarricade(village.Id);
        for (var i = 0; i < 5; i++) sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(20 + i * 2, 94), tint: i);
        for (var i = 0; i < 5000 && !(sim.Snapshot.Villages[0].Walls[0].Slots.Any(s => s.FurnitureId is not null) &&
            sim.Snapshot.FurnitureTasks.Any(t => t.Started)); i++) sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villages[0].Walls[0].Slots, s => s.FurnitureId is not null);
        Assert.Contains(sim.Snapshot.FurnitureTasks, t => t.Started);
        var frozen = sim.Snapshot;
        using var save = new MemoryStream();
        sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        var expected = new List<BlueprintPiecePlaced>(); var actual = new List<BlueprintPiecePlaced>();
        using var sub = sim.Subscribe(e => { if (e is BlueprintPiecePlaced p) expected.Add(p); });
        using var sub2 = loaded.Subscribe(e => { if (e is BlueprintPiecePlaced p) actual.Add(p); });
        sim.Advance(TimeSpan.FromHours(4)); loaded.Advance(TimeSpan.FromHours(4));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.Equal(sim.Snapshot.Furniture, loaded.Snapshot.Furniture);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(sim.Snapshot.Villages[0].Walls),
            System.Text.Json.JsonSerializer.Serialize(loaded.Snapshot.Villages[0].Walls));
        Assert.True(frozen.Villages[0].Walls[0].Slots.Count(s => s.FurnitureId is not null) <
            loaded.Snapshot.Villages[0].Walls[0].Slots.Count(s => s.FurnitureId is not null));
        var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        data["State"]!["Snapshot"]!["Villages"]![0]!["Walls"]![0]!["Slots"]![0]!["FurnitureId"] = 99999;
        using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(corrupt));
        using var cancellable = Simulation.Load(new MemoryStream(save.ToArray()));
        var reservation = Assert.Single(cancellable.Snapshot.Villages[0].Walls[0].Slots, s => s.ReservedFurnitureId is not null);
        var worker = Assert.Single(cancellable.Snapshot.FurnitureTasks).VillagerIds[0];
        cancellable.Scenario.SendVillager(worker, new(10, 104));
        Assert.DoesNotContain(cancellable.Snapshot.Villages[0].Walls[0].Slots, s => s.ReservedFurnitureId == reservation.ReservedFurnitureId);
        using var cancelledSave = new MemoryStream(); cancellable.Save(cancelledSave); cancelledSave.Position = 0;
        using var cancelled = Simulation.Load(cancelledSave);
        var builtId = cancellable.Snapshot.Villages[0].Walls[0].Slots.First(s => s.FurnitureId is not null).FurnitureId!.Value;
        Assert.Throws<InvalidOperationException>(() => cancellable.Scenario.DisassembleFurniture(builtId, b.Id));
        cancellable.Scenario.RemoveFurniture(p => p.Id == builtId);
        Assert.DoesNotContain(cancellable.Snapshot.Villages[0].Walls[0].Slots, s => s.FurnitureId == builtId);
        using var removedSave = new MemoryStream(); cancellable.Save(removedSave); removedSave.Position = 0;
        using var removed = Simulation.Load(removedSave);
    }

    [Fact]
    public void A_freeform_outline_incorporates_a_Pillar_face_and_wraps_across_a_Seam()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var pillar = Assert.Single(sim.Snapshot.Pillars);
        var v = sim.Scenario.PlaceVillager(pillar.Position + new Vector2(0, pillar.Width / 2 + 6));
        var village = sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        var wall = sim.Scenario.PlanBarricade(village.Id);
        Assert.Equal(WallOutline.Freeform, wall.Outline);
        Assert.Equal(pillar.Id, wall.IncorporatedPillarId);
        Assert.Equal(pillar.Width, sim.Snapshot.Space.Distance(wall.Vertices[0], wall.Vertices[1]));
        Assert.All(wall.Vertices.Take(2), p => Assert.Equal(pillar.Position.Y + pillar.Width / 2, p.Y));
        var other = sim.Scenario.PlaceVillager(new(1, 104));
        var seamVillage = sim.Scenario.PlaceVillage(other.Position, [other.Id], Personality.Builders);
        var seamWall = sim.Scenario.PlanBarricade(seamVillage.Id);
        Assert.Contains(seamWall.Slots, s => s.Position.X > 120);
        Assert.Contains(seamWall.Slots, s => s.Position.X < 10);
        Assert.Equal(seamWall.Vertices[0], seamWall.Vertices[^1]);
    }

    [Theory]
    [InlineData(Personality.Cautious, false, WallOutline.Round)]
    [InlineData(Personality.Builders, false, WallOutline.Square)]
    [InlineData(Personality.Bold, true, WallOutline.Freeform)]
    [InlineData(Personality.Cautious, true, WallOutline.Freeform)]
    public void Personality_and_site_choose_a_closed_outline(Personality personality, bool shelving, WallOutline expected)
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        var member = sim.Scenario.PlaceVillager(new(30, 104));
        var shelf = shelving ? sim.Scenario.PlaceFurniture(FurnitureType.ShelfUnit, new(30, 96)) : (FurniturePiece?)null;
        var village = sim.Scenario.PlaceVillage(member.Position, [member.Id], personality);
        var wall = sim.Scenario.PlanBarricade(village.Id);
        Assert.Equal(expected, wall.Outline);
        Assert.Equal(wall.Vertices[0], wall.Vertices[^1]);
        if (shelf is { } piece)
        {
            Assert.Equal(piece.Id, wall.IncorporatedFurnitureId);
            Assert.Equal(new Vector2(29, 96), wall.Vertices[0]);
            Assert.Equal(new Vector2(31, 96), wall.Vertices[1]);
        }
        // The site is inside the polygon, independently checked by ray crossing.
        var inside = false;
        for (var i = 0; i < wall.Vertices.Count - 1; i++)
        {
            var a = wall.Vertices[i]; var b = wall.Vertices[i + 1];
            if ((a.Y > village.Position.Y) != (b.Y > village.Position.Y) &&
                village.Position.X < (b.X - a.X) * (village.Position.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        Assert.True(inside);
    }
}
