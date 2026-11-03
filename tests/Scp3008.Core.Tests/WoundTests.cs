using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class WoundTests
{
    private static Simulation Sandbox(out Villager villager, Needs? needs = null)
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        villager = sim.Scenario.PlaceVillager(new(20, 104), needs);
        return sim;
    }

    [Fact]
    public void A_first_hit_wounds_a_second_takes_the_Villager_without_a_body()
    {
        using var sim = Sandbox(out var v);
        var taken = new List<VillagerTaken>();
        using var sub = sim.Subscribe(e => { if (e is VillagerTaken t) taken.Add(t); });
        Assert.False(sim.Scenario.StaffHitsVillager(v.Id));
        Assert.Equal(v.Id, Assert.Single(sim.Snapshot.Wounds).VillagerId);
        Assert.Single(sim.Snapshot.Villagers);
        Assert.True(sim.Scenario.StaffHitsVillager(v.Id));
        Assert.Empty(sim.Snapshot.Villagers);
        Assert.Empty(sim.Snapshot.Wounds);
        Assert.Empty(sim.Snapshot.Bodies);
        Assert.Single(taken);
        Assert.Contains(sim.Snapshot.Chronicle, c => c.Contains("Staff") && c.Contains(v.Name));
    }

    private static double HealedAfter(bool bandage, bool mattress = false)
    {
        using var sim = Sandbox(out var v, mattress ? new Needs(Rest: 0.1) : null);
        if (mattress) sim.Scenario.PlaceFurniture(FurnitureType.Mattress, new(22, 104));
        sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        if (bandage) sim.Scenario.EquipGear(v.Id, GearKind.Bandage);
        sim.Scenario.StaffHitsVillager(v.Id);
        if (bandage) Assert.True(sim.Scenario.UseBandage(v.Id));
        sim.Advance(TimeSpan.FromHours(6));
        return sim.Snapshot.Wounds.Single().Healed;
    }

    [Fact]
    public void A_bandage_makes_a_wound_heal_faster()
    {
        var plain = HealedAfter(false);
        Assert.True(plain > 0);
        Assert.True(HealedAfter(true) > plain * 1.5);
    }

    [Fact]
    public void Sleeping_on_a_mattress_speeds_up_healing()
    {
        using var sim = Sandbox(out var v, new Needs(Rest: 0.1));
        sim.Scenario.PlaceVillage(v.Position, [v.Id]);
        sim.Scenario.StaffHitsVillager(v.Id);
        sim.Advance(TimeSpan.FromHours(6));
        var floor = sim.Snapshot.Wounds.Single().Healed;
        Assert.True(HealedAfter(false, mattress: true) > floor);
    }

    [Fact]
    public void A_wound_heals_in_the_end_and_survives_saving()
    {
        using var sim = Sandbox(out var v);
        sim.Scenario.StaffHitsVillager(v.Id);
        sim.Advance(TimeSpan.FromHours(3));
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Wounds, loaded.Snapshot.Wounds);
        Assert.Equal(v.Id, Assert.Single(loaded.Snapshot.Wounds).VillagerId);
        loaded.Advance(TimeSpan.FromHours(400));
        Assert.Empty(loaded.Snapshot.Wounds);
    }
}
