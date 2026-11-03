using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class HordeNightTests
{
    private static readonly Vector2 At = new(30, 104);

    private static Simulation Sandbox(HordeNightConfig hordeNights) => Simulation.Create(new WorldConfig(3008,
        PillarLayout: PillarLayout.VillagerSandbox, RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }, HordeNights: hordeNights));

    // A Village of three behind a complete wall ring, stocked for many nights.
    private static Village Fortified(Simulation sim, Vector2 at)
    {
        var members = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(at + new Vector2(i * 0.8f - 1, -1))).ToArray();
        var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        sim.Scenario.PlanBarricade(village.Id);
        sim.Scenario.CompleteWall(village.Id, 0);
        return sim.Snapshot.Villages[village.Id];
    }

    // One Closing night: just into it, then the given time of it, then on to the morning.
    private static void Night(Simulation sim, TimeSpan length)
    {
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2) + length);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Horde_nights_come_periodically_and_grow_with_the_worlds_development()
    {
        using var sim = Sandbox(new HordeNightConfig { EveryNights = 3, CalmNights = 1, Ceiling = 100 });
        var village = Fortified(sim, At);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        for (var night = 0; night < 7; night++)
        {
            Night(sim, TimeSpan.FromMinutes(5));
            if (night == 3)
            {
                // The world grows: more people, and the Village finishes its Keep.
                for (var i = 0; i < 6; i++) sim.Scenario.PlaceVillager(At + new Vector2(i * 0.6f - 2, 1));
                sim.Scenario.BuildKeep(village.Id);
            }
        }

        var hordeNights = events.OfType<HordeNightBegan>().ToList();
        Assert.Equal(2, hordeNights.Count);
        Assert.Equal(3, hordeNights[1].Night - hordeNights[0].Night);
        Assert.True(hordeNights[1].Size > hordeNights[0].Size, $"Horde nights do not grow: {hordeNights[0].Size} then {hordeNights[1].Size}.");
        Assert.All(hordeNights, h => Assert.Equal([village.Id], h.VillageIds));
        Assert.Equal(2, sim.Snapshot.Chronicle.Count(line => line.Contains("Horde night")));
        // The Horde night's Staff are gone when the Store opens.
        Assert.Empty(sim.Snapshot.Staff);
    }

    [Fact]
    public void A_Horde_night_besieges_several_Villages_at_once_and_never_exceeds_the_ceiling()
    {
        using var sim = Sandbox(new HordeNightConfig { EveryNights = 1, CalmNights = 0, Ceiling = 4 });
        var first = Fortified(sim, At);
        var second = Fortified(sim, At + new Vector2(64, -64));
        // Far more people than the ceiling allows for.
        for (var i = 0; i < 30; i++) sim.Scenario.PlaceVillager(At + new Vector2(i % 6 - 3, 3 + i / 6));
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2));
        var hordeNight = Assert.Single(events.OfType<HordeNightBegan>());
        Assert.Equal(4, hordeNight.Size);
        Assert.True(sim.Snapshot.Staff.Count <= 4, $"{sim.Snapshot.Staff.Count} Staff came out.");
        Assert.Equal([first.Id, second.Id], hordeNight.VillageIds.Order());

        sim.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal([first.Id, second.Id], events.OfType<SiegeBegan>().Select(s => s.VillageId).Distinct().Order());
        Assert.Contains(sim.Snapshot.Chronicle, line => line.Contains("Horde night") && line.Contains($"Villages {first.Id}, {second.Id}"));
    }

    [Fact]
    public void No_Siege_happens_in_the_calm_stretch_after_a_Horde_night()
    {
        using var sim = Sandbox(new HordeNightConfig { EveryNights = 3, CalmNights = 2, Ceiling = 100 });
        var village = Fortified(sim, At);
        // A Staff waits outside the walls every night.
        var outside = At + new Vector2(18, 0);
        sim.Scenario.PlaceStaff(outside, [outside, outside + new Vector2(0.5f, 0)]);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        var sieged = new List<bool>();
        var hordeNight = -1;
        for (var night = 0; night < 6; night++)
        {
            events.Clear();
            Night(sim, TimeSpan.FromHours(hordeNight >= 0 && night - hordeNight <= 2 ? 3 : 0.02));
            if (hordeNight < 0 && events.OfType<HordeNightBegan>().Any()) hordeNight = night;
            sieged.Add(events.OfType<SiegeBegan>().Any(s => s.VillageId == village.Id));
        }

        Assert.True(hordeNight is >= 0 and <= 3, $"No Horde night came ({hordeNight}).");
        Assert.True(sieged[hordeNight]);
        Assert.False(sieged[hordeNight + 1], "A Siege in the calm stretch.");
        Assert.False(sieged[hordeNight + 2], "A Siege in the calm stretch.");
        Assert.True(sieged.Take(hordeNight).All(s => s), "No Siege on an ordinary night.");
    }

    [Fact]
    public void Hordes_are_fewer_and_smaller_in_the_calm_stretch()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off,
            HordeNights: new HordeNightConfig { EveryNights = 3, CalmNights = 1 }));
        sim.Scenario.SetFogBankFormation(false);
        // People bring more Staff to their Sector, so its Horde has several.
        var populated = sim.Snapshot.Sectors[0];
        for (var i = 0; i < 12; i++) sim.Scenario.PlaceVillager(populated.Centre);
        var events = new List<DomainEvent>();
        using var _ = sim.Subscribe(events.Add);

        // Each night just after the Staff come out: the Hordes out, the Staff out, all the Staff, and whether a Horde night began.
        var nights = new List<(int Hordes, int Out, int All, bool HordeNight)>();
        for (var night = 0; night < 6; night++)
        {
            events.Clear();
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
            sim.Advance(TimeSpan.FromMinutes(2));
            var hostile = sim.Snapshot.Staff.Where(s => s.Hostile).ToList();
            nights.Add((hostile.Select(s => s.HordeId).Distinct().Count(), hostile.Count, sim.Snapshot.Staff.Count, events.OfType<HordeNightBegan>().Any()));
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
            sim.Advance(TimeSpan.FromMinutes(1));
        }

        var hordeNight = nights.FindIndex(n => n.HordeNight);
        Assert.True(hordeNight is >= 0 and <= 3, $"No Horde night came ({hordeNight}).");
        var calmNight = nights[hordeNight + 1];
        var ordinary = nights[hordeNight + 2];
        // On an ordinary night every Staff comes out.
        Assert.Equal(ordinary.All, ordinary.Out);
        Assert.True(calmNight.Hordes < sim.Snapshot.Hordes.Count(h => h.SectorId is not null), "No fewer Hordes in the calm stretch.");
        Assert.True(calmNight.Out < calmNight.All, "No smaller Hordes in the calm stretch.");
        Assert.True(calmNight.Hordes < ordinary.Hordes && calmNight.Out < ordinary.Out);
    }
}
