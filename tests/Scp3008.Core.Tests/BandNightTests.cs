using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class BandNightTests
{
    private static Simulation Arena()
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        sim.Scenario.SetFogBankFormation(false);
        return sim;
    }

    // Loose bookcases near where the Band will spend the night; the sandbox has no Displays to withdraw into.
    private static Band PlaceBand(Simulation sim, Vector2 at, Personality personality, bool bookcases = true)
    {
        if (bookcases)
            for (var i = 0; i < 8; i++)
                sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, at + new Vector2(-6 + i * 1.5f, 5));
        return sim.Scenario.PlaceBand([at, at + new Vector2(1, 0), at + new Vector2(0, 1)], personality: personality);
    }

    private static Band Now(Simulation sim, int id) => sim.Snapshot.Bands.Single(b => b.Id == id);

    private static Villager[] Members(Simulation sim, Band band) =>
        sim.Snapshot.Villagers.Where(v => band.VillagerIds.Contains(v.Id)).ToArray();

    // Just before a Band shelters (at 21:30), so it has not wandered off, into the first half hour of Closing time.
    private static readonly TimeSpan Dusk = TimeSpan.FromHours(21.48);

    private static void IntoClosingTime(Simulation sim)
    {
        sim.Scenario.SetTimeOfDay(Dusk);
        sim.Advance(TimeSpan.FromHours(1));
    }

    private static float Distance(Simulation sim, Vector2 a, Vector2 b) => sim.Snapshot.Space.Distance(a, b);

    [Fact]
    public void A_cautious_Band_hides_behind_Furniture_a_bold_one_moves_on_a_builder_Band_barricades()
    {
        using var sim = Arena();
        var cautious = PlaceBand(sim, new(20, 100), Personality.Cautious);
        var bold = PlaceBand(sim, new(70, 60), Personality.Bold, bookcases: false);
        var builders = PlaceBand(sim, new(110, 100), Personality.Builders);
        var began = new List<BandNightBegan>();
        using var sub = sim.Subscribe(e => { if (e is BandNightBegan b) began.Add(b); });
        sim.Scenario.SetTimeOfDay(Dusk);
        sim.Advance(TimeSpan.FromHours(0.55));
        var boldAtDusk = Members(sim, Now(sim, bold.Id)).Select(v => v.Position).ToArray();
        sim.Advance(TimeSpan.FromHours(0.75));

        var hiding = Now(sim, cautious.Id);
        Assert.Equal(BandStrategy.Hide, hiding.Strategy);
        Assert.Equal(8, hiding.Barricade.Count);
        var shelter = Assert.NotNull(hiding.Shelter);
        Assert.True(Distance(sim, shelter, cautious.Position) < 2, "With no Display near, it hides where it stands.");
        Assert.All(hiding.Barricade, id => Assert.InRange(Distance(sim, sim.Snapshot.Furniture.Single(p => p.Id == id).Position, shelter), 0.5f, 1.5f));
        Assert.All(Members(sim, hiding), v => Assert.True(Distance(sim, v.Position, shelter) < 0.6f, "Every member crowds into the hiding place."));
        Assert.Single(Members(sim, hiding), v => v.Action != VillagerAction.Resting);

        var moving = Now(sim, bold.Id);
        Assert.Equal(BandStrategy.MoveOn, moving.Strategy);
        Assert.Empty(moving.Barricade);
        Assert.DoesNotContain(Members(sim, moving), v => v.Action == VillagerAction.Resting);
        Assert.True(Members(sim, moving).Zip(boldAtDusk).Max(p => Distance(sim, p.First.Position, p.Second)) > 5, "The bold Band walked on in the dark.");

        var barricaded = Now(sim, builders.Id);
        Assert.Equal(BandStrategy.Barricade, barricaded.Strategy);
        Assert.Equal(8, barricaded.Barricade.Count);
        Assert.True(Distance(sim, Assert.NotNull(barricaded.Shelter), builders.Position) < 3, "It barricades where it stands.");
        Assert.All(Members(sim, barricaded), v => Assert.Equal(VillagerAction.Resting, v.Action));

        Assert.Equal([BandStrategy.Hide, BandStrategy.MoveOn, BandStrategy.Barricade],
            began.OrderBy(b => b.BandId).Select(b => b.Strategy));
        Assert.Contains(sim.Snapshot.Chronicle, c => c.Contains($"Band {cautious.Id} hid in silence"));
        Assert.Contains(sim.Snapshot.Chronicle, c => c.Contains($"Band {bold.Id} kept moving"));
        Assert.Contains(sim.Snapshot.Chronicle, c => c.Contains($"Band {builders.Id} threw up a quick barricade"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_silent_hiding_Band_is_passed_by_a_Horde_but_a_noisy_member_draws_it(bool noisy)
    {
        using var sim = Arena();
        var at = new Vector2(40, 100);
        var band = PlaceBand(sim, at, Personality.Cautious);
        if (noisy) sim.Scenario.StaffHitsVillager(band.VillagerIds[1]);
        IntoClosingTime(sim);
        var shelter = Assert.NotNull(Now(sim, band.Id).Shelter);
        Assert.Equal(8, Now(sim, band.Id).Barricade.Count);
        var staff = sim.Scenario.PlaceStaff(shelter + new Vector2(-15, 4), [shelter + new Vector2(15, 4), shelter + new Vector2(-15, 4)]);

        var noises = new List<NoiseEmitted>();
        using var sub = sim.Subscribe(e => { if (e is NoiseEmitted n) noises.Add(n); });
        var closest = float.MaxValue;
        var chased = false;
        var drawn = false;
        for (var minute = 0; minute < 60; minute++)
        {
            sim.Advance(TimeSpan.FromMinutes(1));
            var now = sim.Snapshot.Staff.Single(s => s.Id == staff.Id);
            closest = Math.Min(closest, Distance(sim, now.Position, shelter));
            chased |= now.ChasingVillagerId is not null;
            drawn |= now.Destination is { } d && Distance(sim, d, shelter) < 6;
        }
        if (noisy)
            Assert.True(drawn || chased, $"The groaning member drew the Horde to the hiding place ({noises.Count} noises, closest {closest} m, sensitivity {sim.StaffNoiseSensitivity(sim.Snapshot.SectorAt(shelter).Id)}).");
        else
        {
            Assert.True(closest < 6, $"The Horde passed within sight ({closest} m).");
            Assert.False(chased, "Nobody behind the Furniture was noticed.");
            Assert.False(drawn, "Nothing drew the Horde to the hiding place.");
        }
    }

    [Fact]
    public void After_a_member_is_Taken_the_Band_switches_strategy_in_the_morning_and_it_survives_saving()
    {
        using var sim = Arena();
        var band = PlaceBand(sim, new(40, 100), Personality.Cautious);
        IntoClosingTime(sim);
        var ring = Now(sim, band.Id).Barricade.ToDictionary(id => id, id => sim.Snapshot.Furniture.Single(p => p.Id == id).Position);
        Assert.False(sim.Scenario.StaffHitsVillager(band.VillagerIds[2]));
        Assert.True(sim.Scenario.StaffHitsVillager(band.VillagerIds[2]));
        Assert.Equal(BandStrategy.Barricade, Now(sim, band.Id).NextStrategy);
        Assert.Equal(BandStrategy.Hide, Now(sim, band.Id).Strategy);

        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        var before = Now(sim, band.Id);
        var after = Now(loaded, band.Id);
        Assert.Equal((before.Strategy, before.NextStrategy, before.Shelter, before.LastNight), (after.Strategy, after.NextStrategy, after.Shelter, after.LastNight));
        Assert.Equal(before.Barricade, after.Barricade);

        var changed = new List<BandStrategyChanged>();
        using var sub = loaded.Subscribe(e => { if (e is BandStrategyChanged c) changed.Add(c); });
        // Two of three left, it founds a Village once the morning lets it.
        loaded.Advance(TimeSpan.FromHours(10.5));
        Assert.Equal((band.Id, BandStrategy.Hide, BandStrategy.Barricade), Assert.Single(changed) is var c ? (c.BandId, c.From, c.To) : default);
        Assert.Contains(loaded.Snapshot.Chronicle, line => line.Contains($"Band {band.Id} gave up hiding and turned to barricading"));
        Assert.All(ring, p => Assert.True(Distance(loaded, loaded.Snapshot.Furniture.Single(f => f.Id == p.Key).Position, p.Value) > 1,
            "The Band pushed its barricade open to walk out."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cautious_Village_does_no_noisy_work_at_Closing_time_but_a_desperate_one_does_and_draws_Staff(bool desperate)
    {
        using var sim = Arena();
        var members = new[] { sim.Scenario.PlaceVillager(new(30, 104)), sim.Scenario.PlaceVillager(new(31, 104)) };
        var village = sim.Scenario.PlaceVillage(new(30, 104), members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 100);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 100);
        var plan = sim.Scenario.PlanBarricade(village.Id);
        foreach (var i in Enumerable.Range(0, plan.Slots.Count + 3))
            sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(4 + i % 12 * 2, 80 + i / 12 * 3), tint: i % 12);
        // A Village the Staff have already struck, with a member lying Wounded, is desperate to finish its walls.
        if (desperate) sim.Scenario.StaffHitsVillager(members[0].Id);
        var staff = sim.Scenario.PlaceStaff(new(50, 104), [new(70, 104), new(50, 104)]);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));

        var noises = 0;
        var placed = 0;
        using var sub = sim.Subscribe(e =>
        {
            if (e is NoiseEmitted) noises++;
            if (e is BlueprintPiecePlaced) placed++;
        });
        var drawn = false;
        for (var minute = 0; minute < 180; minute += 2)
        {
            sim.Advance(TimeSpan.FromMinutes(2));
            var now = sim.Snapshot.Staff.Single(s => s.Id == staff.Id);
            drawn |= now.ChasingVillagerId is not null || now.Destination is { } d && Distance(sim, d, village.Position) < 12;
        }
        Assert.Equal(DayPhase.ClosingTime, sim.Snapshot.Clock.Phase);
        if (desperate)
        {
            Assert.True(noises > 0, "Building at Closing time is heard.");
            Assert.True(drawn, "The noise drew the Staff towards the Village.");
        }
        else
        {
            Assert.Equal(0, noises);
            Assert.Equal(0, placed);
            Assert.False(drawn);
        }
    }
}
