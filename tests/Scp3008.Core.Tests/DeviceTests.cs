using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class DeviceTests
{
    private static readonly Vector2 At = new(30, 104);

    private static Simulation Sandbox(HordeNightConfig? hordeNights = null) => Simulation.Create(new WorldConfig(3008,
        PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }, HordeNights: hordeNights));

    // A Village of three, stocked for many nights; behind a complete wall ring when fortified.
    private static Village PlaceVillage(Simulation sim, Vector2 at, bool fortified = true, params Role[] roles)
    {
        var members = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(at + new Vector2(i * 0.8f - 1, -1))).ToArray();
        var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        for (var i = 0; i < roles.Length; i++) sim.Scenario.AssignRole(members[i].Id, roles[i]);
        if (fortified)
        {
            sim.Scenario.PlanBarricade(village.Id);
            sim.Scenario.CompleteWall(village.Id, 0);
        }
        return sim.Snapshot.Villages[village.Id];
    }

    private static void IntoClosingTime(Simulation sim)
    {
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_noise_lure_set_off_away_from_a_Village_draws_a_Horde_off_its_course(bool lure)
    {
        using var sim = Sandbox();
        var village = PlaceVillage(sim, At);
        // A Staff walks straight at the Village.
        var start = At + new Vector2(60, 0);
        sim.Scenario.PlaceStaff(start, [start, At]);
        var lurePosition = At + new Vector2(60, 40);
        if (lure)
        {
            var device = sim.Scenario.SetDevice(village.Id, DeviceKind.NoiseLure, lurePosition);
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(2));
            sim.Scenario.SetOffLure(device.Id);
        }
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        IntoClosingTime(sim);
        sim.Advance(TimeSpan.FromMinutes(45));

        var staff = Assert.Single(sim.Snapshot.Staff);
        var space = sim.Snapshot.Space;
        if (!lure)
        {
            Assert.Contains(events.OfType<SiegeBegan>(), s => s.VillageId == village.Id);
            return;
        }
        Assert.DoesNotContain(events, e => e is SiegeBegan);
        Assert.Contains(events.OfType<HordeLured>(), l => l.VillageId == village.Id);
        Assert.True(space.Distance(staff.Position, lurePosition) < 10f, $"The Staff is {space.Distance(staff.Position, lurePosition):0.0} m from the lure.");
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("noise lure"));
    }

    [Fact]
    public void An_alarm_wakes_the_guards_earlier_than_without_one()
    {
        long WokenAt(bool alarm)
        {
            using var sim = Sandbox();
            var village = PlaceVillage(sim, At, roles: [Role.Guard, Role.Guard]);
            var start = At + new Vector2(40, 0);
            var wire = At + new Vector2(32, 0);
            sim.Scenario.PlaceStaff(start, [start, wire, At]);
            if (alarm) sim.Scenario.SetDevice(village.Id, DeviceKind.Alarm, wire);
            var events = new List<DomainEvent>();
            using var _ = sim.Subscribe(events.Add);

            IntoClosingTime(sim);
            sim.Advance(TimeSpan.FromMinutes(30));

            var woken = Assert.Single(events.OfType<GuardsWoken>());
            Assert.Equal(2, woken.GuardIds.Count);
            Assert.All(woken.GuardIds, id => Assert.Equal(VillagerAction.StandingGuard, sim.Snapshot.Villagers.Single(v => v.Id == id).Action));
            Assert.Equal(alarm, events.OfType<AlarmRaised>().Any());
            if (alarm) Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("tin cans"));
            return woken.At.Tick;
        }

        Assert.True(WokenAt(alarm: true) < WokenAt(alarm: false));
    }

    [Fact]
    public void A_collapsing_Furniture_trap_triggered_under_a_Horde_damages_and_slows_it()
    {
        // The Store minute the Staff reaches the end of its way past the trap's place, and the Staff then.
        (int Minutes, Staff Staff, List<DomainEvent> Events) Run(bool trap)
        {
            using var sim = Sandbox();
            var village = PlaceVillage(sim, At, fortified: false);
            var start = At + new Vector2(-20, 30);
            var trapAt = start + new Vector2(5, 0);
            var end = start + new Vector2(20, 0);
            sim.Scenario.PlaceStaff(start, [start, trapAt, end]);
            if (trap)
            {
                var device = sim.Scenario.SetDevice(village.Id, DeviceKind.FurnitureTrap, trapAt);
                Assert.Equal(3, device.FurnitureIds.Count);
                // The stack stands until it is sprung.
                sim.Advance(TimeSpan.FromMinutes(1));
                Assert.All(device.FurnitureIds.Skip(1), id => Assert.True(sim.Snapshot.Furniture.Single(p => p.Id == id).Elevation > 0));
            }
            var events = new List<DomainEvent>();
            using var _ = sim.Subscribe(events.Add);
            IntoClosingTime(sim);
            var minutes = 0;
            while (sim.Snapshot.Space.Distance(sim.Snapshot.Staff.Single().Position, end) > 2f && minutes < 60)
            {
                sim.Advance(TimeSpan.FromMinutes(1));
                minutes++;
            }
            return (minutes, Assert.Single(sim.Snapshot.Staff), events);
        }

        var free = Run(trap: false);
        var trapped = Run(trap: true);

        var sprung = Assert.Single(trapped.Events.OfType<TrapSprung>());
        Assert.Equal(DeviceKind.FurnitureTrap, sprung.Kind);
        Assert.Equal([trapped.Staff.Id], sprung.StaffIds);
        Assert.Contains(trapped.Events, e => e is StructureCollapsed);
        Assert.True(trapped.Staff.Health < 1f, "The trap did not hurt the Staff.");
        Assert.Equal(1f, free.Staff.Health);
        Assert.True(free.Minutes < 60);
        Assert.True(trapped.Minutes >= free.Minutes + 8, $"The trap did not slow the Staff: {trapped.Minutes} min against {free.Minutes} min.");
    }

    [Fact]
    public void Remains_bait_draws_a_Horde_into_a_trap()
    {
        using var sim = Sandbox();
        var village = PlaceVillage(sim, At, fortified: false);
        var trap = sim.Scenario.SetDevice(village.Id, DeviceKind.Pit, At + new Vector2(30, 0));
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);
        // Remains lie by the Village in the morning; it carries them off onto its trap.
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(9));
        sim.Scenario.PlaceStaffRemains(At + new Vector2(4, 4), TimeSpan.FromHours(2));
        sim.Advance(TimeSpan.FromHours(3));
        var hauled = Assert.Single(events.OfType<StaffRemainsHauled>());
        var space = sim.Snapshot.Space;
        Assert.True(space.Distance(hauled.Position, trap.Position) <= 3f, $"The remains were left {space.Distance(hauled.Position, trap.Position):0.0} m from the trap.");

        // At Closing time a Staff patrolling away from the trap is drawn to the remains, into the pit.
        var patrol = trap.Position + new Vector2(0, 15);
        sim.Scenario.PlaceStaff(patrol, [patrol, patrol + new Vector2(10, 0)]);
        IntoClosingTime(sim);
        sim.Advance(TimeSpan.FromMinutes(40));

        var sprung = Assert.Single(events.OfType<TrapSprung>());
        Assert.True(sprung.Baited);
        Assert.Equal(trap.Id, sprung.DeviceId);
        Assert.Equal(sim.Snapshot.Staff.Single().Id, Assert.Single(sprung.StaffIds));
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("as bait"));
    }

    [Fact]
    public void Villages_with_late_know_how_set_more_devices_as_Horde_nights_grow_where_they_believe_the_Hordes_come_from()
    {
        using var sim = Sandbox(new HordeNightConfig { EveryNights = 2, CalmNights = 1 });
        var village = PlaceVillage(sim, At);
        sim.Scenario.TeachRecipe(village.Id, GearKind.Halberd);
        foreach (var material in new[] { MaterialKind.Metal, MaterialKind.Electrics, MaterialKind.WoodPanel })
            sim.Scenario.SetMaterials(village.Id, material, 50);
        var counts = new List<int>();
        for (var night = 0; night < 4; night++)
        {
            IntoClosingTime(sim);
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(9));
            sim.Advance(TimeSpan.FromHours(6));
            counts.Add(sim.Snapshot.Devices.Count(d => d.VillageId == village.Id));
        }

        Assert.Equal(0, counts[0]);
        Assert.True(counts[1] > 0 && counts[3] > counts[1], $"Devices by night: {string.Join(", ", counts)}.");
        var belief = sim.Snapshot.Villages[village.Id].HordeBelief;
        Assert.NotNull(belief);
        // The alarms lie on the side the Village believes the Hordes come from.
        var space = sim.Snapshot.Space;
        var alarm = sim.Snapshot.Devices.First(d => d.Kind == DeviceKind.Alarm);
        Assert.True(Vector2.Dot(space.Delta(At, alarm.Position), space.Delta(At, belief.Where)) > 0, $"Alarm at {alarm.Position}, Belief {belief.Where}.");
    }

    [Fact]
    public void Devices_and_Beliefs_survive_a_save_and_load()
    {
        using var sim = Sandbox();
        var village = PlaceVillage(sim, At);
        var lure = sim.Scenario.SetDevice(village.Id, DeviceKind.NoiseLure, At + new Vector2(50, 0), variant: 1);
        sim.Scenario.SetDevice(village.Id, DeviceKind.FurnitureTrap, At + new Vector2(0, 35));
        sim.Scenario.SetDevice(village.Id, DeviceKind.Alarm, At + new Vector2(-40, 0));
        sim.Scenario.SetOffLure(lure.Id);
        var start = At + new Vector2(30, 0);
        sim.Scenario.PlaceStaff(start, [start, At]);
        IntoClosingTime(sim);
        sim.Advance(TimeSpan.FromMinutes(5));

        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);

        Assert.Equal(sim.Snapshot.Devices.Count, loaded.Snapshot.Devices.Count);
        for (var i = 0; i < sim.Snapshot.Devices.Count; i++)
        {
            var (before, after) = (sim.Snapshot.Devices[i], loaded.Snapshot.Devices[i]);
            Assert.Equal(before with { FurnitureIds = [] }, after with { FurnitureIds = [] });
            Assert.Equal(before.FurnitureIds, after.FurnitureIds);
        }
        Assert.Equal(sim.Snapshot.Villages[village.Id].HordeBelief, loaded.Snapshot.Villages[village.Id].HordeBelief);
    }
}
