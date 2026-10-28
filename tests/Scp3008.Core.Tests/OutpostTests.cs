using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class OutpostTests
{
    [Theory]
    [InlineData(20, 76)]
    [InlineData(120, 48)]
    public void Depletion_sends_a_large_Village_outward_and_Outpost_Goods_reach_home(float homeX, float sourceX)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));
        var people = Enumerable.Range(0, 8).Select(i => sim.Scenario.PlaceVillager(new(homeX - 4 + i, 104))).ToArray();
        var village = sim.Scenario.PlaceVillage(new(homeX, 104), people.Select(p => p.Id).ToArray());
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 200);
        var local = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(homeX + 6, 104));
        sim.Scenario.SetStock(local.Id, GoodsKind.TinnedFood, 4);
        var distant = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(sourceX, 104));
        sim.Scenario.SetStock(distant.Id, GoodsKind.TinnedFood, 80);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var shortTrip = false;
        for (var i = 0; i < 3000 && sim.Snapshot.Stock.Any(g => g.FurnitureId == local.Id); i++)
        {
            sim.AdvanceTicks(1);
            shortTrip |= sim.Snapshot.Villagers.Any(p => p.Action == VillagerAction.Scavenging &&
                p.Destination is { } target && sim.Snapshot.Space.Distance(village.Position, target) < 48);
        }
        Assert.True(shortTrip);
        Assert.DoesNotContain(sim.Snapshot.Stock, g => g.FurnitureId == local.Id);
        Assert.Empty(sim.Snapshot.Villages[0].Outposts);
        sim.Scenario.SendVillager(people[0].Id, new(sourceX - 1, 104));
        for (var i = 0; i < 15000 && sim.Snapshot.Villages[0].Outposts.Count == 0; i++) sim.AdvanceTicks(1);
        var post = Assert.Single(sim.Snapshot.Villages[0].Outposts);
        Assert.True(sim.Snapshot.Space.Distance(village.Position, post.Position) > 48);
        Assert.DoesNotContain(sim.Snapshot.Stock, g => g.FurnitureId == local.Id);
        Assert.NotEmpty(post.VillagerIds);
        for (var i = 0; i < 3000 && !sim.Snapshot.Villagers.Any(p => p.Action == VillagerAction.Scavenging &&
            p.Destination is { } target && sim.Snapshot.Space.Distance(village.Position, target) > 48); i++) sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villagers, p => p.VillageId == village.Id && p.Action == VillagerAction.Scavenging && p.Destination is { } target &&
            sim.Snapshot.Space.Distance(village.Position, target) > 48);
        for (var i = 0; i < 15000 && !events.OfType<GoodsDelivered>().Any(e => post.VillagerIds.Contains(e.VillagerId)); i++) sim.AdvanceTicks(1);
        Assert.Contains(events.OfType<GoodsDelivered>(), e => post.VillagerIds.Contains(e.VillagerId) && e.VillageId == village.Id);
        Assert.Contains(sim.Snapshot.Chronicle, s => s.Contains("Outpost"));
    }

    [Fact]
    public void Outpost_barricade_is_built_and_save_load_continues_deliveries_and_abandonment()
    {
        using var sim = KnownSourceVillage();
        sim.AdvanceTicks(101);
        var post = Assert.Single(sim.Snapshot.Villages[0].Outposts);
        for (var i = 0; i < 70; i++) sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(90 + i % 8 * 2, 80 + i / 8 * 3));
        var placed = new List<BlueprintPiecePlaced>();
        using var sub = sim.Subscribe(e => { if (e is BlueprintPiecePlaced p) placed.Add(p); });
        for (var i = 0; i < 60000 && !sim.Snapshot.Villages[0].Walls[post.WallIndex].Complete; i++) sim.AdvanceTicks(1);
        Assert.True(sim.Snapshot.Villages[0].Walls[post.WallIndex].Complete, $"Built {placed.Count}; {string.Join("; ", sim.Snapshot.Villagers.Select(p => $"{p.Id}:{p.Role}:{p.Action}:{p.Position}"))}");
        Assert.Contains(placed, e => e.Ring == post.WallIndex);
        using var save = new MemoryStream();
        sim.Save(save);
        using var loaded = Simulation.Load(new MemoryStream(save.ToArray()));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(sim.Snapshot.Villages),
            System.Text.Json.JsonSerializer.Serialize(loaded.Snapshot.Villages));
        var expected = new List<DomainEvent>(); var actual = new List<DomainEvent>();
        using var first = sim.Subscribe(expected.Add); using var second = loaded.Subscribe(actual.Add);
        sim.Advance(TimeSpan.FromHours(24)); loaded.Advance(TimeSpan.FromHours(24));
        Assert.Contains(actual.OfType<GoodsDelivered>(), e => post.VillagerIds.Contains(e.VillagerId));
        foreach (var world in new[] { sim, loaded })
        {
            world.Scenario.SetStock(world.Snapshot.Furniture.Single(p => p.Type == FurnitureType.GroceryShelf && p.Position.X == 76).Id, GoodsKind.TinnedFood, 0);
            world.Advance(TimeSpan.FromHours(12));
        }
        Assert.True(loaded.Snapshot.Villages[0].Outposts[0].Abandoned);
        Assert.Contains(actual, e => e is OutpostAbandoned);
        Assert.Equal(expected, actual);
        Assert.Equal(sim.Snapshot.Chronicle, loaded.Snapshot.Chronicle);
        var frozen = post;
        Assert.False(frozen.Abandoned);
        using var abandonedSave = new MemoryStream(); loaded.Save(abandonedSave);
        using var abandonedLoad = Simulation.Load(new MemoryStream(abandonedSave.ToArray()));
        Assert.True(abandonedLoad.Snapshot.Villages[0].Outposts[0].Abandoned);
        var data = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        data["State"]!["Snapshot"]!["Villages"]![0]!["Outposts"]![0]!["WallIndex"] = 999;
        using var corrupt = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(corrupt));
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(8, 100, true)]
    [InlineData(8, 0, false)]
    public void Small_Villages_rich_surroundings_or_unknown_distant_sources_do_not_send_Outposts(int population, int localFood, bool known)
    {
        using var sim = KnownSourceVillage(population, localFood, known);
        sim.AdvanceTicks(151);
        Assert.Empty(sim.Snapshot.Villages[0].Outposts);
    }

    private static Simulation KnownSourceVillage(int population = 8, int localFood = 0, bool known = true)
    {
        var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            RuntimeRngSeed: 17, Restock: new RestockConfig { GoodsPerStoreHour = 0, FurnitureHours = 0 }));
        var people = Enumerable.Range(0, population).Select(i => sim.Scenario.PlaceVillager(new(16 + i, 104))).ToArray();
        var village = sim.Scenario.PlaceVillage(new(20, 104), people.Select(p => p.Id).ToArray());
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 2000);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 2000);
        var local = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(26, 104));
        sim.Scenario.SetStock(local.Id, GoodsKind.TinnedFood, localFood);
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(76, 104));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 2000);
        sim.Scenario.PlaceBand([village.Position], knownMap: new KnownMap
        {
            Villages = [new(village.Id, village.Position, sim.Snapshot.Clock)],
            Goods = known ? [new(shelf.Position, new StockedGoods(shelf.Id, GoodsKind.TinnedFood, 2000), sim.Snapshot.Clock)] : [],
        });
        return sim;
    }
}
