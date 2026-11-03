using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StaffKillTests
{
    private static readonly Vector2 At = new(30, 104);

    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17,
        StartingBands: false, Arrivals: ArrivalConfig.Off, Restock: new RestockConfig { FurnitureHours = 0 }));

    private static Staff PlaceStaff(Simulation sim, Vector2 at) => sim.Scenario.PlaceStaff(at, [at, at + new Vector2(0.5f, 0)]);

    // Villagers standing round the Staff, each with the Gear given.
    private static int[] Fighters(Simulation sim, Vector2 staff, int count, params GearKind[] gear) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var v = sim.Scenario.PlaceVillager(staff + new Vector2(MathF.Cos(i * 1.6f), MathF.Sin(i * 1.6f)) * 1.5f);
            foreach (var kind in gear) sim.Scenario.EquipGear(v.Id, kind);
            return v.Id;
        }).ToArray();

    private static Village PlaceVillage(Simulation sim, int people)
    {
        var members = Enumerable.Range(0, people).Select(i => sim.Scenario.PlaceVillager(At + new Vector2(i * 0.8f - 1, -1)).Id).ToArray();
        var village = sim.Scenario.PlaceVillage(At, members, Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        return sim.Snapshot.Villages[village.Id];
    }

    private static void Step(Simulation sim, Func<bool> done, TimeSpan most, TimeSpan step)
    {
        for (var t = TimeSpan.Zero; t < most && !done(); t += step) sim.Advance(step);
    }

    [Fact]
    public void A_lone_Villager_with_a_spear_only_drives_a_Staff_back()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var staff = PlaceStaff(sim, At);
        var spear = Fighters(sim, At, 1, GearKind.Spear);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Scenario.FightStaff(staff.Id, spear);
        sim.Advance(TimeSpan.FromMinutes(30));

        Assert.NotEmpty(events.OfType<StaffDrivenBack>());
        Assert.Empty(events.OfType<StaffKilled>());
        Assert.Equal(1f, Assert.Single(sim.Snapshot.Staff).Health);
        Assert.Empty(sim.Snapshot.StaffRemains);
    }

    [Fact]
    public void A_group_with_metal_armour_and_Halberds_kills_a_Staff_at_Closing_time_with_noise_and_wounds()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var staff = PlaceStaff(sim, At);
        var group = Fighters(sim, At, 4, GearKind.MetalArmour, GearKind.Halberd);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Scenario.FightStaff(staff.Id, group);
        Step(sim, () => events.OfType<StaffKilled>().Any(), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));

        var killed = Assert.Single(events.OfType<StaffKilled>());
        Assert.Equal(staff.Id, killed.StaffId);
        Assert.Empty(sim.Snapshot.Staff);
        Assert.Empty(sim.Snapshot.StaffFights);
        var remains = Assert.Single(sim.Snapshot.StaffRemains);
        Assert.Equal(killed.Remains.Position, remains.Position);
        Assert.Equal(0f, remains.Pull);
        // The fight is loud, and the Staff hits back.
        Assert.True(events.OfType<NoiseEmitted>().Count(n => n.FurnitureId < 0) >= 2);
        Assert.NotEmpty(events.OfType<VillagerWounded>());
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("killed a Staff") || line.Contains("A Staff fell for good"));
    }

    [Fact]
    public void The_same_group_cannot_kill_a_dormant_Staff_while_the_Store_is_open()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var staff = PlaceStaff(sim, At);
        var group = Fighters(sim, At, 4, GearKind.MetalArmour, GearKind.Halberd);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Scenario.FightStaff(staff.Id, group);
        sim.Advance(TimeSpan.FromMinutes(90));

        Assert.Empty(events.OfType<StaffKilled>());
        Assert.Empty(events.OfType<VillagerWounded>());
        var standing = Assert.Single(sim.Snapshot.Staff);
        Assert.Equal(1f, standing.Health);
        Assert.False(standing.Hostile);
        Assert.Empty(sim.Snapshot.StaffRemains);
        Assert.Empty(sim.Snapshot.StaffFights);
    }

    [Fact]
    public void Remains_near_a_Village_draw_a_Horde_after_a_few_hours_more_strongly_over_time()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        PlaceVillage(sim, 2);
        var remains = sim.Scenario.PlaceStaffRemains(At + new Vector2(3, 0));
        var post = At + new Vector2(40, 0);
        var staff = PlaceStaff(sim, post);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);
        float Pull() => sim.Snapshot.StaffRemains.Single().Pull;
        float Away() => sim.Snapshot.Space.Distance(sim.Snapshot.Staff.Single().Position, post);

        sim.Advance(TimeSpan.FromHours(2.5));
        Assert.Equal(0f, Pull());
        Assert.True(Away() < 3, "The Staff keeps to its post while the remains are fresh.");

        sim.Advance(TimeSpan.FromHours(1));
        var early = Pull();
        sim.Advance(TimeSpan.FromHours(1));
        var later = Pull();
        Assert.True(early > 0 && later > early, $"The pull grows: {early} then {later}.");

        Step(sim, () => events.OfType<StaffRemainsCarriedOff>().Any(), TimeSpan.FromHours(3), TimeSpan.FromMinutes(2));
        var carried = Assert.Single(events.OfType<StaffRemainsCarriedOff>());
        Assert.Equal((remains.Id, staff.Id), (carried.RemainsId, carried.StaffId));
        Assert.True(carried.At.TimeOfDay >= TimeSpan.FromHours(1), "It was drawn only once the remains were a few hours old.");
    }

    [Fact]
    public void Staff_carry_remains_off_at_Closing_time_but_not_while_the_Store_is_open()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var staff = PlaceStaff(sim, At);
        sim.Scenario.PlaceStaffRemains(At + new Vector2(10, 0), TimeSpan.FromHours(8));
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Advance(TimeSpan.FromHours(9.5));
        Assert.Single(sim.Snapshot.StaffRemains);

        Step(sim, () => events.OfType<StaffRemainsCarriedOff>().Any(), TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));
        var carried = Assert.Single(events.OfType<StaffRemainsCarriedOff>());
        Assert.Equal(DayPhase.ClosingTime, carried.At.Phase);
        Assert.Equal(staff.Id, carried.StaffId);
        Assert.Empty(sim.Snapshot.StaffRemains);
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("carried them off"));
    }

    [Fact]
    public void A_Village_hauls_remains_away_and_the_Horde_they_draw_stays_away_from_it()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
        var village = PlaceVillage(sim, 3);
        sim.Scenario.PlaceStaffRemains(At + new Vector2(4, 0));
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        Step(sim, () => events.OfType<StaffRemainsHauled>().Any(), TimeSpan.FromHours(3), TimeSpan.FromMinutes(5));
        var hauled = Assert.Single(events.OfType<StaffRemainsHauled>());
        Assert.Equal(village.Id, hauled.VillageId);
        var left = Assert.Single(sim.Snapshot.StaffRemains);
        Assert.Null(left.CarrierId);
        Assert.True(sim.Snapshot.Space.Distance(left.Position, At) >= 50, $"Left {sim.Snapshot.Space.Distance(left.Position, At)} m away.");
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("hauled the Staff remains away"));

        // At Closing time a Horde nearer the Village than the remains goes for the remains.
        var post = At + new Vector2(25, 26);
        PlaceStaff(sim, post);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        var nearest = float.MaxValue;
        for (var i = 0; i < 180 && !events.OfType<StaffRemainsCarriedOff>().Any(); i++)
        {
            sim.Advance(TimeSpan.FromMinutes(1));
            nearest = Math.Min(nearest, sim.Snapshot.Space.Distance(sim.Snapshot.Staff.Single().Position, At));
        }
        var carried = Assert.Single(events.OfType<StaffRemainsCarriedOff>());
        Assert.True(sim.Snapshot.Space.Distance(carried.Position, At) >= 50);
        Assert.True(nearest > 25, $"The Staff came within {nearest} m of the Village.");
    }

    [Fact]
    public void The_Staff_population_recovers_from_a_kill_through_its_normal_growth()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off));
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2));
        var horde = sim.Snapshot.Hordes.First(h => h.SectorId is not null && h.StaffIds.Count > 0);
        int InSector() => sim.Snapshot.Hordes.Where(h => h.SectorId == horde.SectorId).Sum(h => h.StaffIds.Count);
        var before = InSector();
        var target = sim.Snapshot.Staff.Single(s => s.Id == horde.StaffIds[0]);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Scenario.FightStaff(target.Id, Fighters(sim, target.Position, 4, GearKind.MetalArmour, GearKind.Halberd));
        Step(sim, () => events.OfType<StaffKilled>().Any(), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));
        Assert.Single(events.OfType<StaffKilled>());
        Assert.Equal(before - 1, InSector());

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(21) + TimeSpan.FromMinutes(59));
        sim.Advance(TimeSpan.FromHours(1));
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(21) + TimeSpan.FromMinutes(59));
        sim.Advance(TimeSpan.FromMinutes(2));
        Assert.True(InSector() >= before, $"{InSector()} Staff after the next night's growth, {before} before the kill.");
        Assert.DoesNotContain(sim.Snapshot.Staff, s => s.Id == target.Id);
    }

    [Fact]
    public void Remains_and_a_fight_survive_a_save_and_load()
    {
        using var sim = Sandbox();
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
        PlaceVillage(sim, 2);
        sim.Scenario.PlaceStaffRemains(At + new Vector2(20, 0), TimeSpan.FromHours(5));
        var staff = PlaceStaff(sim, At + new Vector2(-20, 0));
        var group = Fighters(sim, At + new Vector2(-20, 0), 4, GearKind.MetalArmour, GearKind.Halberd);
        sim.Scenario.FightStaff(staff.Id, group);
        sim.Advance(TimeSpan.FromSeconds(70));
        var fight = Assert.Single(sim.Snapshot.StaffFights);
        var health = sim.Snapshot.Staff.Single().Health;
        Assert.True(health < 1);

        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);

        var remains = Assert.Single(loaded.Snapshot.StaffRemains);
        Assert.Equal(sim.Snapshot.StaffRemains.Single() with { Pull = remains.Pull }, remains);
        Assert.Equal(sim.Snapshot.StaffRemains.Single().Pull, remains.Pull);
        var loadedFight = Assert.Single(loaded.Snapshot.StaffFights);
        Assert.Equal((fight.StaffId, fight.NextBlowTick, fight.UntilTick), (loadedFight.StaffId, loadedFight.NextBlowTick, loadedFight.UntilTick));
        Assert.Equal(fight.VillagerIds, loadedFight.VillagerIds);
        Assert.Equal(health, loaded.Snapshot.Staff.Single().Health);
        var events = new List<DomainEvent>();
        using var _ = loaded.Subscribe(events.Add);
        Step(loaded, () => events.OfType<StaffKilled>().Any(), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));
        Assert.Single(events.OfType<StaffKilled>());
        Assert.Equal(2, loaded.Snapshot.StaffRemains.Count);
    }
}
