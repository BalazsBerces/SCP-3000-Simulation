using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class AllianceTests
{
    private static readonly Vector2 At = new(30, 104);

    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    private static Village Place(Simulation sim, float x, Role[] roles, bool stocked = true)
    {
        var members = roles.Select((_, i) => sim.Scenario.PlaceVillager(new(x + i * 0.8f - 1, 103))).ToArray();
        var village = sim.Scenario.PlaceVillage(new(x, 104), members.Select(v => v.Id).ToArray(), Personality.Cautious);
        for (var i = 0; i < roles.Length; i++) sim.Scenario.AssignRole(members[i].Id, roles[i]);
        if (stocked)
        {
            sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
            sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        }
        return sim.Snapshot.Villages[village.Id];
    }

    // A walled Village with a Keep under a Staff at Closing time; its ally stands further along the aisle.
    private static (Simulation Sim, Village Besieged, Village Ally) Siege(Role[] besiegedRoles, Role[] allyRoles)
    {
        var sim = Sandbox();
        var village = Place(sim, At.X, besiegedRoles);
        var ally = Place(sim, 52, allyRoles);
        sim.Scenario.PlanBarricade(village.Id);
        sim.Scenario.CompleteWall(village.Id, 0);
        sim.Scenario.BuildKeep(village.Id);
        sim.Scenario.FormAlliance(village.Id, ally.Id);
        var outside = At - new Vector2(18, 0);
        sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        return (sim, village, ally);
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int ticks = 12000)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done());
    }

    [Fact]
    public void An_ally_sends_guards_to_a_besieged_Village()
    {
        var (sim, village, ally) = Siege([Role.Scavenger, Role.Builder, Role.Cook], [Role.Guard, Role.Guard, Role.Guard, Role.Cook]);
        using var _ = sim;
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        RunUntil(sim, () => events.OfType<GuardsSent>().Any());
        var sent = Assert.Single(events.OfType<GuardsSent>());
        Assert.Equal((ally.Id, village.Id), (sent.FromVillageId, sent.ToVillageId));
        Assert.Equal(2, sent.GuardIds.Count);
        Assert.Equal(sent.GuardIds.Order(), sim.Snapshot.Villages[village.Id].Reinforcements.Select(r => r.VillagerId).Order());
        RunUntil(sim, () => sent.GuardIds.All(id => Vector2.Distance(sim.Snapshot.Villagers.Single(v => v.Id == id).Position, village.Position) < 12));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"defend its ally, Village {village.Id}"));
    }

    [Fact]
    public void A_Village_with_no_ally_gets_no_guards()
    {
        var (sim, village, ally) = Siege([Role.Scavenger, Role.Builder, Role.Cook], [Role.Guard, Role.Guard, Role.Guard, Role.Cook]);
        using var _ = sim;
        sim.Scenario.FormAlliance(ally.Id, village.Id); // already allied: no change
        var other = Place(sim, 110, [Role.Guard, Role.Guard, Role.Guard]);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        RunUntil(sim, () => events.OfType<GuardsSent>().Any());
        Assert.DoesNotContain(events.OfType<GuardsSent>(), e => e.FromVillageId == other.Id);
    }

    private static CaravanTraded Barter(bool allied)
    {
        using var sim = Sandbox();
        var a = Place(sim, 40, [Role.Trader, Role.Trader, Role.Cook]);
        var b = Place(sim, 100, [Role.Cook, Role.Cook, Role.Cook], stocked: false);
        if (allied) sim.Scenario.FormAlliance(a.Id, b.Id);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.Scenario.SendCaravan(a.Id, b.Id, new Dictionary<GoodsKind, int> { [GoodsKind.TinnedFood] = 10 });
        RunUntil(sim, () => events.OfType<CaravanTraded>().Any());
        return events.OfType<CaravanTraded>().First();
    }

    [Fact]
    public void Allies_get_better_barter_terms()
    {
        var friends = Barter(allied: true);
        var strangers = Barter(allied: false);
        Assert.True(strangers.Value > 0);
        Assert.True(friends.Value > strangers.Value);
    }

    [Fact]
    public void Allies_share_Recipes_and_Beliefs()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40, [Role.Cook, Role.Cook, Role.Cook]);
        var b = Place(sim, 100, [Role.Cook, Role.Cook, Role.Cook]);
        var recipe = Recipes.Workshop[0].Gear;
        sim.Scenario.TeachRecipe(a.Id, recipe);
        sim.AdvanceTicks(200);
        Assert.DoesNotContain(recipe, sim.Snapshot.Villages[b.Id].Recipes);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        sim.Scenario.FormAlliance(a.Id, b.Id);
        sim.AdvanceTicks(120);
        Assert.Contains(recipe, sim.Snapshot.Villages[b.Id].Recipes);
        Assert.Contains(events.OfType<AllianceShared>(), e => e.VillageId == b.Id && e.Recipes.Contains(recipe));
    }

    [Fact]
    public void Refugees_of_a_fallen_Village_are_taken_in_by_its_ally()
    {
        var (sim, village, ally) = Siege([Role.Scavenger, Role.Builder, Role.Cook], [Role.Scavenger, Role.Builder, Role.Cook]);
        using var _ = sim;
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        RunUntil(sim, () => events.OfType<VillageFell>().Any(), 30000);
        var fell = Assert.Single(events.OfType<VillageFell>());
        Assert.Null(fell.BandId);
        var taken = Assert.Single(events.OfType<RefugeesTakenIn>());
        Assert.Equal((village.Id, ally.Id), (taken.FallenVillageId, taken.AllyVillageId));
        var home = sim.Snapshot.Villages[ally.Id];
        Assert.All(fell.RefugeeIds, id => Assert.Contains(id, home.VillagerIds));
        Assert.Empty(sim.Snapshot.Bands);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("took in the Refugees"));
    }

    [Fact]
    public void Alliances_show_in_the_chronicle_and_survive_a_save_and_load()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40, [Role.Cook, Role.Cook, Role.Cook]);
        var b = Place(sim, 100, [Role.Cook, Role.Cook, Role.Cook]);
        sim.Scenario.FormAlliance(a.Id, b.Id);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"Village {a.Id} and Village {b.Id} swore"));
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal([b.Id], loaded.Snapshot.Villages[a.Id].Allies);
        Assert.Equal([a.Id], loaded.Snapshot.Villages[b.Id].Allies);
    }
}
