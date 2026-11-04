using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class CaravanTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    private static Village Place(Simulation sim, float x, float y = 104)
    {
        var people = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(new(x - 1 + i, y))).ToArray();
        return sim.Scenario.PlaceVillage(new(x, y), people.Select(p => p.Id).ToArray());
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int ticks = 8000)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done());
    }

    [Fact]
    public void A_Caravan_moves_goods_from_one_Village_s_stores_to_another_and_brings_payment_home()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SetStores(b.Id, GoodsKind.BottledWater, 30);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var caravan = sim.Scenario.SendCaravan(a.Id, b.Id, new Dictionary<GoodsKind, int> { [GoodsKind.TinnedFood] = 10 });
        Assert.Equal(20, sim.Snapshot.Villages[a.Id].Stores[GoodsKind.TinnedFood]);
        Assert.Equal(caravan.Id, Assert.Single(sim.Snapshot.Caravans).Id);
        RunUntil(sim, () => events.OfType<CaravanTraded>().Any());
        Assert.True(sim.Snapshot.Villages[b.Id].Stores[GoodsKind.TinnedFood] >= 10);
        RunUntil(sim, () => events.OfType<CaravanReturned>().Any());
        Assert.Empty(sim.Snapshot.Caravans);
        Assert.True(sim.Snapshot.Villages[a.Id].Stores.GetValueOrDefault(GoodsKind.BottledWater) > 0);
        Assert.Contains(sim.Snapshot.Chronicle, e => e.Contains("Caravan"));
    }

    [Fact]
    public void Barter_prices_reflect_each_Village_s_needs()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 30);
        Assert.True(sim.BarterPrice(b.Id, GoodsKind.TinnedFood) > sim.BarterPrice(a.Id, GoodsKind.TinnedFood));
        sim.Scenario.SetStores(b.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 0);
        Assert.True(sim.BarterPrice(a.Id, GoodsKind.TinnedFood) > sim.BarterPrice(b.Id, GoodsKind.TinnedFood));
    }

    [Fact]
    public void A_Village_can_barter_a_Recipe_and_part_of_its_Known_map()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        var recipe = Recipes.Workshop[0].Gear;
        sim.Scenario.TeachRecipe(a.Id, recipe);
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(46, 104));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 5);
        sim.AdvanceTicks(150);
        Assert.NotEmpty(sim.Snapshot.Villages[a.Id].KnownMap.Goods);
        Assert.Empty(sim.Snapshot.Villages[b.Id].KnownMap.Goods);
        Assert.DoesNotContain(recipe, sim.Snapshot.Villages[b.Id].Recipes);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.Scenario.SendCaravan(a.Id, b.Id, recipes: [recipe], shareMapWithin: 30);
        RunUntil(sim, () => events.OfType<CaravanTraded>().Any());
        Assert.Contains(recipe, sim.Snapshot.Villages[b.Id].Recipes);
        Assert.NotEmpty(sim.Snapshot.Villages[b.Id].KnownMap.Goods);
    }

    [Fact]
    public void A_Caravan_travelling_at_Closing_time_can_be_hit_by_a_Horde()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.Scenario.SendCaravan(a.Id, b.Id, new Dictionary<GoodsKind, int> { [GoodsKind.TinnedFood] = 10 });
        sim.Scenario.PlaceStaff(new(55, 104), [new(55, 104), new(56, 104)]);
        RunUntil(sim, () => events.OfType<CaravanAmbushed>().Any(), 3000);
        Assert.Empty(sim.Snapshot.Caravans);
        Assert.Equal(0, sim.Snapshot.Villages[b.Id].Stores.GetValueOrDefault(GoodsKind.TinnedFood));
        Assert.Contains(sim.Snapshot.Chronicle, e => e.Contains("ambushed a Caravan"));
    }

    [Fact]
    public void Caravan_routes_avoid_the_dead_zone_at_the_cost_of_a_detour()
    {
        foreach (var seed in Enumerable.Range(10, 40))
        {
            if (TestStores.Get(seed).Landmarks.FirstOrDefault(l => l.Type == LandmarkType.DeadZone) is not { } zone) continue;
            using var sim = Simulation.Create(new WorldConfig(seed, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off));
            var reach = zone.Size.X / 2 + 12;
            var a = Place(sim, zone.Centre.X - reach, zone.Centre.Y);
            var b = Place(sim, zone.Centre.X + reach, zone.Centre.Y);
            var route = sim.Scenario.SendCaravan(a.Id, b.Id).Route;
            var space = sim.Snapshot.Space;
            var points = new[] { a.Position }.Concat(route).ToArray();
            Assert.True(route.Count >= 2);
            for (var i = 0; i + 1 < points.Length; i++)
                for (var t = 0f; t <= 1f; t += 0.02f)
                    Assert.False(zone.Covers(space, points[i] + space.Delta(points[i], points[i + 1]) * t));
            var length = points.Zip(points.Skip(1), space.Distance).Sum();
            Assert.True(length > space.Distance(a.Position, b.Position));
            return;
        }
        Assert.Fail("No seed gives a dead zone.");
    }

    [Fact]
    public void A_Caravan_survives_a_save_and_load()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 30);
        sim.Scenario.SendCaravan(a.Id, b.Id, new Dictionary<GoodsKind, int> { [GoodsKind.TinnedFood] = 10 });
        sim.AdvanceTicks(20);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(10, Assert.Single(loaded.Snapshot.Caravans).Cargo.Goods[GoodsKind.TinnedFood]);
    }
}
