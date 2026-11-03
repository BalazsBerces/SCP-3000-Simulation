using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class SiegeTests
{
    private static readonly Vector2 At = new(30, 104);

    // A Village with a complete wall ring, its members given the Roles, and one Staff waiting outside as night comes on.
    private static (Simulation Sim, Village Village) Besieged(Role[] roles, bool keep)
    {
        var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17,
            Restock: new RestockConfig { FurnitureHours = 0 }));
        var members = roles.Select((_, i) => sim.Scenario.PlaceVillager(At + new Vector2(i * 0.8f - 1, -1))).ToArray();
        var village = sim.Scenario.PlaceVillage(At, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        for (var i = 0; i < roles.Length; i++) sim.Scenario.AssignRole(members[i].Id, roles[i]);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        sim.Scenario.PlanBarricade(village.Id);
        sim.Scenario.CompleteWall(village.Id, 0);
        if (keep) sim.Scenario.BuildKeep(village.Id);
        var outside = At + new Vector2(18, 0);
        sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        return (sim, sim.Snapshot.Villages[village.Id]);
    }

    // Steps the night until the event comes, at most a few Store hours.
    private static T Until<T>(Simulation sim, List<DomainEvent> events) where T : DomainEvent
    {
        for (var i = 0; i < 600 && !events.OfType<T>().Any(); i++) sim.Advance(TimeSpan.FromSeconds(30));
        return Assert.Single(events.OfType<T>());
    }

    private static float DamageOf(Simulation sim, int? id) => sim.Snapshot.Furniture.Single(p => p.Id == id).Damage;

    [Fact]
    public void A_Horde_reaching_a_Village_damages_its_walls_starting_at_the_weakest_piece()
    {
        var (sim, village) = Besieged([Role.Scavenger, Role.Builder, Role.Cook], keep: false);
        using var _ = sim;
        // One piece already took a blow: it is the weakest.
        var weakest = village.Walls[0].Slots.Count / 2;
        sim.Scenario.StrikeWall(village.Id, 0, weakest);
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);

        Assert.Equal(village.Id, Until<SiegeBegan>(sim, events).VillageId);
        var breach = Until<WallBreached>(sim, events);
        Assert.Equal((village.Id, 0, weakest), (breach.VillageId, breach.Ring, breach.Slot));
        Assert.Equal(1f, DamageOf(sim, breach.FurnitureId));
        Assert.All(village.Walls[0].Slots.Where((_, i) => i != weakest), s => Assert.Equal(0f, DamageOf(sim, s.FurnitureId)));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"besieged Village {village.Id}") || line.Contains($"walls of Village {village.Id}"));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains($"broke through the wall of Village {village.Id}"));
    }

    [Fact]
    public void A_Guard_holds_off_the_Staff_and_takes_the_blows_instead_of_the_wall()
    {
        var (sim, village) = Besieged([Role.Guard, Role.Scavenger], keep: false);
        using var _ = sim;
        var guard = village.VillagerIds[0];
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);

        Assert.Equal(guard, Until<VillagerWounded>(sim, events).VillagerId);
        Assert.Empty(events.OfType<WallBreached>());
        Assert.All(village.Walls[0].Slots, s => Assert.Equal(0f, DamageOf(sim, s.FurnitureId)));
    }

    [Fact]
    public void A_Village_with_no_defenders_and_a_breached_Keep_falls_and_its_survivors_become_Refugees()
    {
        var (sim, village) = Besieged([Role.Scavenger, Role.Builder, Role.Cook], keep: true);
        var events = new List<DomainEvent>();
        var sub = sim.Subscribe(events.Add);
        Until<WallBreached>(sim, events);
        Assert.Empty(events.OfType<VillageFell>());

        // The Siege goes on after a save and load in the middle of it.
        using var save = new MemoryStream();
        sim.Save(save);
        sub.Dispose();
        sim.Dispose();
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        events.Clear();
        using var _ = loaded.Subscribe(events.Add);

        var keepBreach = Until<KeepBreached>(loaded, events);
        var fell = Until<VillageFell>(loaded, events);
        Assert.Equal(village.Id, keepBreach.VillageId);
        Assert.Equal(village.Id, fell.VillageId);
        Assert.Equal(village.VillagerIds.Order(), fell.RefugeeIds.Order());

        AssertFallen(loaded.Snapshot);
        Assert.Contains(loaded.Snapshot.Chronicle, line => line.Contains($"Keep of Village {village.Id} was breached"));
        Assert.Contains(loaded.Snapshot.Chronicle, line => line.Contains($"Village {village.Id}") && line.Contains("fled as Refugees"));

        // The fall and the Refugees survive a save and load.
        using var again = new MemoryStream();
        loaded.Save(again);
        again.Position = 0;
        using var reloaded = Simulation.Load(again);
        AssertFallen(reloaded.Snapshot);

        void AssertFallen(WorldSnapshot snapshot)
        {
            var fallen = snapshot.Villages[village.Id];
            Assert.True(fallen.Fallen);
            Assert.Empty(fallen.VillagerIds);
            var refugees = Assert.Single(snapshot.Bands, b => b.FallenVillageId == village.Id);
            Assert.Equal(village.VillagerIds.Order(), refugees.VillagerIds.Order());
            Assert.All(refugees.VillagerIds, id => Assert.Null(snapshot.Villagers.Single(v => v.Id == id).VillageId));
        }
    }
}
