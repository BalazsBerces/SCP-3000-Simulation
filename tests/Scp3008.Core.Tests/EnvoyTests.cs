using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class EnvoyTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    private static Village Place(Simulation sim, float x, float y = 104, Personality personality = Personality.Cautious, int people = 3)
    {
        var ids = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(new(x - 1 + i, y))).Select(p => p.Id).ToArray();
        var village = sim.Scenario.PlaceVillage(new(x, y), ids, personality);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        return village;
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int ticks = 8000)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done());
    }

    [Fact]
    public void A_Village_learns_of_another_only_after_one_of_its_people_meets_it()
    {
        using var sim = Sandbox();
        var a = Place(sim, 20);
        var b = Place(sim, 100);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.AdvanceTicks(300);
        Assert.DoesNotContain(sim.Snapshot.Villages[a.Id].KnownMap.Villages, v => v.VillageId == b.Id);
        Assert.Empty(events.OfType<FirstContact>());

        sim.Scenario.SendVillager(a.VillagerIds[0], new(98, 104));
        RunUntil(sim, () => events.OfType<FirstContact>().Any(e => e.VillageId == a.Id));
        Assert.Contains(sim.Snapshot.Villages[a.Id].KnownMap.Villages, v => v.VillageId == b.Id);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"Village {a.Id} learned that Village {b.Id}"));
    }

    [Fact]
    public void An_Envoy_carries_an_offer_to_another_Village_and_returns_with_an_answer()
    {
        using var sim = Sandbox();
        var a = Place(sim, 30);
        var b = Place(sim, 90);
        var c = Place(sim, 90, 30, Personality.Isolationists);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var envoy = sim.Scenario.SendEnvoy(a.Id, b.Id, EnvoyOffer.Trade);
        sim.Scenario.SendEnvoy(a.Id, c.Id, EnvoyOffer.Trade);
        Assert.Equal(2, sim.Snapshot.Envoys.Count);
        RunUntil(sim, () => events.OfType<EnvoyReturned>().Count() == 2);
        Assert.Empty(sim.Snapshot.Envoys);
        Assert.True(events.OfType<EnvoyReturned>().Single(e => e.EnvoyId == envoy.Id).Accepted);
        Assert.False(events.OfType<EnvoyReturned>().Single(e => e.ToVillageId == c.Id).Accepted);
        // Both ends learned of each other on the way.
        Assert.Contains(sim.Snapshot.Villages[b.Id].KnownMap.Villages, v => v.VillageId == a.Id);
        Assert.Contains(sim.Snapshot.Villages[a.Id].KnownMap.Villages, v => v.VillageId == b.Id);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("Envoy"));
    }

    [Fact]
    public void A_besieged_Village_s_lamp_signal_is_seen_by_a_Village_in_line_of_sight_which_sends_help()
    {
        using var sim = Sandbox();
        var at = new Vector2(30, 104);
        var a = Place(sim, at.X, at.Y, people: 3);
        var guards = a.VillagerIds;
        sim.Scenario.PlanBarricade(a.Id);
        sim.Scenario.CompleteWall(a.Id, 0);
        sim.Scenario.BuildTower(a.Id);
        var b = Place(sim, 70);
        var outside = at + new Vector2(18, 0);
        sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        for (var i = 0; i < 600 && !events.OfType<LampSignalSeen>().Any(); i++) sim.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(a.Id, Assert.Single(events.OfType<LampSignalSent>()).VillageId);
        var seen = Assert.Single(events.OfType<LampSignalSeen>());
        Assert.Equal((a.Id, b.Id, true), (seen.VillageId, seen.SeenByVillageId, seen.SendingHelp));
        Assert.Contains(sim.Snapshot.Envoys, e => e.FromVillageId == b.Id && e.ToVillageId == a.Id && e.Offer == EnvoyOffer.Help);
        Assert.Single(sim.Snapshot.Signals);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("lamp signal"));
    }

    [Fact]
    public void A_Pillar_mark_warns_passing_Villagers_of_a_danger()
    {
        using var sim = Sandbox();
        var a = Place(sim, 20);
        var pillar = sim.Snapshot.Pillars[0];
        var traveller = sim.Scenario.PlaceVillager(pillar.Position + new Vector2(pillar.Width / 2 + 2, 0));
        var far = sim.Scenario.PlaceVillager(new(100, 100));
        var danger = pillar.Position + new Vector2(0, 15);
        sim.Scenario.PaintMark(a.Id, pillar.Id, MarkKind.Warning, 77, danger);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.AdvanceTicks(40);
        Assert.Equal(traveller.Id, Assert.Single(events.OfType<PillarMarkRead>()).VillagerId);
        Assert.Contains(sim.Snapshot.Villagers.Single(v => v.Id == traveller.Id).KnownMap.Dangers, d => d.StaffId == 77 && d.Position == danger);
        Assert.Empty(sim.Snapshot.Villagers.Single(v => v.Id == far.Id).KnownMap.Dangers);
    }

    [Fact]
    public void Envoys_marks_and_contacts_survive_a_save_and_load()
    {
        using var sim = Sandbox();
        var a = Place(sim, 30);
        var b = Place(sim, 90);
        sim.Scenario.SendEnvoy(a.Id, b.Id, EnvoyOffer.Alliance);
        sim.Scenario.PaintMark(a.Id, 0, MarkKind.Border);
        sim.AdvanceTicks(20);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(sim.Snapshot.Envoys, loaded.Snapshot.Envoys);
        Assert.Equal(sim.Snapshot.Marks, loaded.Snapshot.Marks);
        Assert.Equal(sim.Snapshot.Contacts, loaded.Snapshot.Contacts);
        Assert.Single(loaded.Snapshot.Envoys);
    }
}
