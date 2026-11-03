using System.Numerics;
using System.Text.Json;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StaffTests
{
    private static Simulation Store(int seed)
    {
        var sim = Simulation.Create(new WorldConfig(seed, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off));
        sim.Scenario.SetFogBankFormation(false);
        return sim;
    }

    private const int Seed = 3008;

    private static bool InDeadZone(WorldSnapshot snapshot, Vector2 position) =>
        snapshot.Landmarks.Any(l => l.Type == LandmarkType.DeadZone && l.Covers(snapshot.Space, position));

    // Just into the next Closing time.
    private static void ToClosingTime(Simulation sim)
    {
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2));
    }

    private static bool AtBallPit(WorldSnapshot snapshot, Vector2 position) =>
        snapshot.BallPitNetwork.BallPitIds.Select(id => snapshot.PointsOfInterest.Single(p => p.Id == id))
            .Any(pit => snapshot.Space.Distance(pit.Centre, position) <= pit.Size.Length() / 2 + 2);

    [Fact]
    public void Staff_do_not_act_while_the_Store_is_open()
    {
        using var sim = Store(Seed);
        ToClosingTime(sim);
        var before = sim.Snapshot.Staff.ToDictionary(s => s.Id, s => s.Position);
        Assert.NotEmpty(before);
        Assert.All(sim.Snapshot.Staff, s => Assert.True(s.Hostile));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(sim.Snapshot.Staff, s => sim.Snapshot.Space.Distance(before[s.Id], s.Position) > 5);

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var standing = sim.Snapshot.Staff.ToDictionary(s => s.Id, s => s.Position);
        sim.Advance(TimeSpan.FromHours(2));
        // The dead zone is the exception (below).
        Assert.All(sim.Snapshot.Staff.Where(s => !InDeadZone(sim.Snapshot, standing[s.Id])), s =>
        {
            Assert.Equal(standing[s.Id], s.Position);
            Assert.Null(s.Destination);
            Assert.False(s.Hostile);
        });
    }

    [Fact]
    public void A_Horde_follows_the_same_route_on_consecutive_nights()
    {
        using var sim = Store(Seed);
        ToClosingTime(sim);
        // The Horde that comes out nearest its Patrol route, so it walks a good part of it in one night.
        var horde = sim.Snapshot.Hordes.Where(h => h.SectorId is not null)
            .MinBy(h => sim.Snapshot.Space.Distance(sim.Snapshot.Staff.Single(s => s.Id == h.StaffIds[0]).Position, h.Route[0]))!;

        // The waypoints the Horde's first Staff is sent to in one Closing time, in order.
        List<Vector2> Night()
        {
            var waypoints = new List<Vector2>();
            while (sim.Snapshot.Phase == DayPhase.ClosingTime)
            {
                if (sim.Snapshot.Staff.Single(s => s.Id == horde.StaffIds[0]).Destination is { } destination
                    && sim.Snapshot.Hordes.Single(h => h.Id == horde.Id).Route.Contains(destination)
                    && (waypoints.Count == 0 || waypoints[^1] != destination))
                    waypoints.Add(destination);
                sim.Advance(TimeSpan.FromMinutes(1));
            }
            return waypoints;
        }

        var first = Night();
        ToClosingTime(sim);
        var second = Night();
        Assert.Equal(horde.Route, sim.Snapshot.Hordes.Single(h => h.Id == horde.Id).Route);
        Assert.True(first.Count >= 3, $"The Horde reached only {first.Count} waypoints.");
        Assert.Equal(first.Take(3), second.Take(3));
    }

    [Fact]
    public void Staff_grow_over_Store_days_and_faster_where_more_people_live()
    {
        using var sim = Store(Seed);
        var populated = sim.Snapshot.Sectors[0];
        for (var i = 0; i < 12; i++)
            sim.Scenario.PlaceVillager(populated.Centre);
        Assert.All(sim.Snapshot.Villagers, v => Assert.Equal(populated.Id, sim.Snapshot.SectorAt(v.Position).Id));

        Dictionary<int, int> StaffPerSector() => sim.Snapshot.Hordes.Where(h => h.SectorId is not null)
            .GroupBy(h => h.SectorId!.Value).ToDictionary(g => g.Key, g => g.Sum(h => h.StaffIds.Count));

        var counts = new List<Dictionary<int, int>>();
        for (var night = 0; night < 4; night++)
        {
            ToClosingTime(sim);
            counts.Add(StaffPerSector());
        }
        var quiet = sim.Snapshot.Sectors.First(s => s.Id != populated.Id).Id;
        Assert.True(counts[^1].Values.Sum() > counts[0].Values.Sum(), "The Staff do not grow.");
        Assert.True(counts[^1][quiet] > counts[0][quiet], "A quiet Sector's Staff do not grow.");
        Assert.All(counts, c => Assert.True(c[populated.Id] > c[quiet], "More people do not bring more Staff."));
    }

    [Fact]
    public void New_Staff_appear_only_at_a_Ball_pit_at_Closing_time()
    {
        using var sim = Store(Seed);
        sim.Advance(TimeSpan.FromHours(13));
        Assert.Equal(DayPhase.Open, sim.Snapshot.Phase);
        Assert.Empty(sim.Snapshot.Staff);

        var known = new HashSet<int>();
        for (var night = 0; night < 3; night++)
        {
            sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(21.5));
            sim.Advance(TimeSpan.FromMinutes(29));
            Assert.Equal(known, sim.Snapshot.Staff.Select(s => s.Id).ToHashSet());
            sim.Advance(TimeSpan.FromMinutes(1) + StoreClock.TickDuration);
            var fresh = sim.Snapshot.Staff.Where(s => !known.Contains(s.Id)).ToList();
            // Growth may skip a night, but the first and third bring new Staff.
            if (night != 1) Assert.NotEmpty(fresh);
            Assert.All(fresh, s => Assert.True(AtBallPit(sim.Snapshot, s.Position), $"Staff {s.Id} appeared at {s.Position}."));
            known.UnionWith(fresh.Select(s => s.Id));
            sim.Advance(TimeSpan.FromHours(3));
            Assert.Equal(known, sim.Snapshot.Staff.Select(s => s.Id).ToHashSet());
        }
    }

    [Fact]
    public void A_dormant_Staff_hit_while_open_falls_and_gets_up_unharmed()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("staff nearby");
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var staff = Assert.Single(sim.Snapshot.Staff);
        Assert.False(staff.Hostile);

        sim.Scenario.HitStaff(staff.Id);
        var fallen = Assert.Single(sim.Snapshot.Staff);
        Assert.True(fallen.Fallen);
        Assert.Equal(1f, fallen.Health);
        Assert.False(fallen.Hostile);

        sim.Advance(TimeSpan.FromMinutes(11));
        var standing = Assert.Single(sim.Snapshot.Staff);
        Assert.False(standing.Fallen);
        Assert.Equal(1f, standing.Health);
        Assert.False(standing.Hostile);
        Assert.Equal(staff.Position, standing.Position);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(staff.Position, Assert.Single(sim.Snapshot.Staff).Position);
    }

    [Fact]
    public void Staff_are_always_active_in_the_dead_zone()
    {
        var seed = new[] { 3008, 42, -1, 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Concat(Enumerable.Range(10, 40))
            .First(s => TestStores.Get(s).Landmarks.Any(l => l.Type == LandmarkType.DeadZone));
        var zone = TestStores.Get(seed).Landmarks.First(l => l.Type == LandmarkType.DeadZone);
        using var sim = Store(seed);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var inside = sim.Scenario.PlaceStaff(zone.Centre, [zone.Centre + new Vector2(zone.Size.X / 4, 0), zone.Centre]);
        var away = zone.Centre + new Vector2(zone.Size.X + 40, 0);
        var outside = sim.Scenario.PlaceStaff(away, [away + new Vector2(0, 10), away]);

        sim.Advance(TimeSpan.FromMinutes(10));
        var snapshot = sim.Snapshot;
        Assert.Equal(DayPhase.Open, snapshot.Phase);
        var walker = snapshot.Staff.Single(s => s.Id == inside.Id);
        Assert.True(snapshot.Space.Distance(inside.Position, walker.Position) > 1, "The Staff in the dead zone stands still.");
        Assert.True(walker.Hostile);
        var dormant = snapshot.Staff.Single(s => s.Id == outside.Id);
        Assert.Equal(outside.Position, dormant.Position);
        Assert.False(dormant.Hostile);
    }

    [Fact]
    public void At_Closing_time_Staff_pass_from_any_Ball_pit_to_any_other_and_Villagers_cannot()
    {
        using var sim = Store(Seed);
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        var pits = sim.Snapshot.BallPitNetwork.BallPitIds.Select(id => sim.Snapshot.PointsOfInterest.Single(p => p.Id == id)).ToList();
        Assert.Equal(3, pits.Count);
        var passages = (from a in pits from to in pits where a != to
            select (Staff: sim.Scenario.PlaceStaff(a.Centre, [to.Centre]), To: to)).ToList();
        var villager = sim.Scenario.PlaceVillager(pits[0].Centre);
        sim.Scenario.SendVillager(villager.Id, pits[1].Centre);

        // While the Store is open the Staff stand at their Ball pit.
        sim.Advance(TimeSpan.FromMinutes(30));
        Assert.All(passages, p => Assert.Equal(p.Staff.Position, sim.Snapshot.Staff.Single(s => s.Id == p.Staff.Id).Position));

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        var villagerAt = sim.Snapshot.Villagers.Single(v => v.Id == villager.Id).Position;
        sim.Advance(TimeSpan.FromMinutes(3));
        var snapshot = sim.Snapshot;
        Assert.All(passages, p => Assert.True(
            snapshot.Space.Distance(p.To.Centre, snapshot.Staff.Single(s => s.Id == p.Staff.Id).Position) <= p.To.Size.Length() / 2 + 2,
            $"Staff {p.Staff.Id} did not come out of Ball pit {p.To.Id}."));
        var walked = snapshot.Villagers.Single(v => v.Id == villager.Id).Position;
        Assert.True(snapshot.Space.Distance(villagerAt, walked) < 30, "The Villager passed through the Ball pits.");
        Assert.True(snapshot.Space.Distance(pits[1].Centre, walked) > 50);
    }

    [Fact]
    public void Staff_and_Hordes_survive_a_save_and_load()
    {
        using var original = Store(Seed);
        ToClosingTime(original);
        original.Advance(TimeSpan.FromHours(1));
        original.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
        original.Scenario.HitStaff(original.Snapshot.Staff[0].Id);
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        AssertSameSnapshot(original, loaded);
        Assert.NotEmpty(loaded.Snapshot.Hordes);
        Assert.True(loaded.Snapshot.Staff[0].Fallen);

        foreach (var sim in new[] { original, loaded })
        {
            ToClosingTime(sim);
            sim.Advance(TimeSpan.FromHours(1));
        }
        AssertSameSnapshot(original, loaded);
    }

    private static void AssertSameSnapshot(Simulation original, Simulation loaded) =>
        Assert.Equal(JsonSerializer.Serialize(original.Snapshot, new JsonSerializerOptions { IncludeFields = true }),
            JsonSerializer.Serialize(loaded.Snapshot, new JsonSerializerOptions { IncludeFields = true }));
}
