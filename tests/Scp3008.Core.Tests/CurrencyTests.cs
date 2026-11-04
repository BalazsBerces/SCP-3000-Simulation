using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class CurrencyTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    // Four fed Villages close enough to see each other, each holding the given Batteries.
    private static Village[] Region(Simulation sim, int batteries)
    {
        var villages = new[] { 30f, 50f, 70f, 90f }.Select(x =>
        {
            var people = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(new(x - 1 + i, 104))).ToArray();
            var village = sim.Scenario.PlaceVillage(new(x, 104), people.Select(p => p.Id).ToArray());
            sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 40);
            sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 40);
            sim.Scenario.SetStores(village.Id, GoodsKind.Batteries, batteries);
            return village;
        }).ToArray();
        RunUntil(sim, () => sim.Snapshot.Villages.All(v => v.KnownMap.Villages.Count >= 2));
        return villages;
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int ticks = 8000)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done());
    }

    private static CaravanTraded Trade(Simulation sim, List<DomainEvent> events, int from, int to, GoodsKind kind, int amount)
    {
        sim.Scenario.SetStores(from, kind, amount);
        var caravan = sim.Scenario.SendCaravan(from, to, new Dictionary<GoodsKind, int> { [kind] = amount });
        RunUntil(sim, () => events.OfType<CaravanReturned>().Any(r => r.CaravanId == caravan.Id));
        return events.OfType<CaravanTraded>().Single(t => t.CaravanId == caravan.Id);
    }

    [Fact]
    public void A_durable_kind_wanted_by_all_Villages_is_taken_in_trade_beyond_need()
    {
        using var sim = Sandbox();
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var villages = Region(sim, batteries: 2);
        var (a, b) = (villages[0], villages[1]);
        RunUntil(sim, () => sim.Snapshot.Villages[a.Id].Currency == GoodsKind.Batteries);
        Assert.Contains(events.OfType<CurrencyChanged>(), c => c.VillageId == a.Id && c.From is null && c.To == GoodsKind.Batteries);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"Village {a.Id} began to take batteries as currency") && line.Contains("Sector"));
        var traded = Trade(sim, events, b.Id, a.Id, GoodsKind.Batteries, 8);
        // Need is 4 Batteries; with 2 held, barter alone would take only 2 of the 8.
        Assert.True(sim.Snapshot.Villages[a.Id].Stores[GoodsKind.Batteries] >= 9);
        Assert.True(traded.Value >= 8);
    }

    [Fact]
    public void Where_no_kind_fits_no_currency_appears_and_trade_stays_barter()
    {
        using var sim = Sandbox();
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var villages = Region(sim, batteries: 10);
        // Fresh food is wanted by every other Village, but it does not keep.
        sim.Scenario.SetStores(villages[0].Id, GoodsKind.FreshFood, 2);
        sim.AdvanceTicks(2000);
        Assert.Empty(events.OfType<CurrencyChanged>());
        Assert.All(sim.Snapshot.Villages, v => Assert.Null(v.Currency));
        var traded = Trade(sim, events, villages[1].Id, villages[0].Id, GoodsKind.Batteries, 5);
        Assert.Equal(0, traded.Value);
        Assert.True(sim.Snapshot.Villages[villages[1].Id].Stores[GoodsKind.Batteries] >= 4);
    }

    [Fact]
    public void A_flood_of_the_currency_kind_lowers_what_it_buys()
    {
        using var sim = Sandbox();
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var v = Region(sim, batteries: 1);
        RunUntil(sim, () => sim.Snapshot.Villages[v[0].Id].Currency == GoodsKind.Batteries);
        var before = Trade(sim, events, v[1].Id, v[0].Id, GoodsKind.Batteries, 3).Value / 3;
        // A large find: the other Villages' Batteries pile up, so fewer want them.
        sim.Scenario.SetStores(v[2].Id, GoodsKind.Batteries, 3);
        sim.Scenario.SetStores(v[3].Id, GoodsKind.Batteries, 3);
        var after = Trade(sim, events, v[1].Id, v[0].Id, GoodsKind.Batteries, 3).Value / 3;
        Assert.True(after < before, $"{after} < {before}");
    }

    [Fact]
    public void The_currency_survives_a_save_and_load()
    {
        using var sim = Sandbox();
        var v = Region(sim, batteries: 2);
        RunUntil(sim, () => sim.Snapshot.Villages[v[0].Id].Currency == GoodsKind.Batteries);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(GoodsKind.Batteries, loaded.Snapshot.Villages[v[0].Id].Currency);
    }
}
