using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class StaffNoiseTests
{
    private static Simulation Arena()
    {
        var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        sim.Scenario.SetFogBankFormation(false);
        return sim;
    }

    private static void ToClosingTime(Simulation sim)
    {
        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) - TimeSpan.FromMinutes(1));
        sim.Advance(TimeSpan.FromMinutes(2));
    }

    private static float Distance(Simulation sim, Vector2 a, Vector2 b) => sim.Snapshot.Space.Distance(a, b);

    private static Staff Now(Simulation sim, int id) => sim.Snapshot.Staff.Single(s => s.Id == id);

    private static float Sensitivity(Simulation sim, Vector2 at) => sim.StaffNoiseSensitivity(sim.Snapshot.SectorAt(at).Id);

    // Whether the Staff is on its way to the noise, not along its route (which leads the other way).
    private static bool Drawn(Simulation sim, Staff staff, Vector2 noise) =>
        staff.Destination is { } d && Distance(sim, d, noise) < Distance(sim, staff.Position, noise) - 1;

    private static Staff StaffWithRouteAwayFrom(Simulation sim, Vector2 at, Vector2 noise)
    {
        var away = Vector2.Normalize(sim.Snapshot.Space.Delta(noise, at)) * 12;
        return sim.Scenario.PlaceStaff(at, [at + away, at]);
    }

    [Fact]
    public void Noise_draws_a_Horde_off_its_patrol_route()
    {
        using var sim = Arena();
        var at = new Vector2(20, 64);
        var noise = new Vector2(20, 90);
        var staff = StaffWithRouteAwayFrom(sim, at, noise);
        ToClosingTime(sim);
        sim.Scenario.MakeNoise(noise, 30);
        sim.AdvanceTicks(2);
        Assert.True(Drawn(sim, Now(sim, staff.Id), noise));
    }

    [Fact]
    public void A_drawn_Staff_searches_the_noise_source_then_returns_to_its_patrol_route()
    {
        using var sim = Arena();
        var at = new Vector2(20, 64);
        var noise = new Vector2(20, 90);
        var staff = StaffWithRouteAwayFrom(sim, at, noise);
        ToClosingTime(sim);
        sim.Scenario.MakeNoise(noise, 30);
        var nearest = float.MaxValue;
        for (var minute = 0; minute < 60; minute += 5)
        {
            sim.Advance(TimeSpan.FromMinutes(5));
            nearest = Math.Min(nearest, Distance(sim, Now(sim, staff.Id).Position, noise));
        }
        Assert.True(nearest < 12, $"It reached the source (closest {nearest} m).");
        sim.Advance(TimeSpan.FromHours(4));
        Assert.True(Distance(sim, Now(sim, staff.Id).Position, at) < 20, "It is back on its route.");
    }

    [Fact]
    public void A_bright_lit_lamp_next_to_a_patrol_route_does_not_divert_the_Horde()
    {
        using var sim = Arena();
        var person = sim.Scenario.PlaceVillager(new(100, 20));
        sim.Scenario.PlaceVillage(person.Position, [person.Id]);
        sim.Scenario.AssignRole(person.Id, Role.Guard);
        var lamp = sim.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new(100, 21));
        sim.Scenario.MoveFurniture(lamp.Id, new(101, 24), 0);
        sim.Scenario.SetStores(0, GoodsKind.Batteries, 1);
        var at = new Vector2(101, 34);
        var staff = StaffWithRouteAwayFrom(sim, at, new(101, 24));
        ToClosingTime(sim);
        sim.AdvanceTicks(2);
        Assert.True(sim.Snapshot.LampLevel(sim.Snapshot.Furniture.Single(p => p.Id == lamp.Id)) > 0.5f, "The lamp is lit.");
        Assert.False(Drawn(sim, Now(sim, staff.Id), new(101, 24)));
        for (var minute = 0; minute < 60; minute += 5)
        {
            sim.Advance(TimeSpan.FromMinutes(5));
            Assert.True(Distance(sim, Now(sim, staff.Id).Position, at) < 15, "It keeps to its route.");
        }
    }

    [Fact]
    public void A_Villager_standing_still_behind_Furniture_is_not_noticed_but_one_in_the_open_is_chased()
    {
        using var sim = Arena();
        var hidden = sim.Scenario.PlaceVillager(new(40, 66));
        foreach (var x in new[] { 38f, 40f, 42f })
            sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(x, 64), 0);
        var open = sim.Scenario.PlaceVillager(new(90, 66));
        ToClosingTime(sim);
        // The Villager stands still where they are (they were on their way elsewhere).
        sim.Scenario.SendVillager(hidden.Id, hidden.Position);
        var staffHidden = sim.Scenario.PlaceStaff(new(40, 62), [new(40, 62), new(40, 61)]);
        var staffOpen = sim.Scenario.PlaceStaff(new(90, 62), [new(90, 62), new(90, 61)]);
        sim.AdvanceTicks(2);
        Assert.Null(sim.Snapshot.Villagers.Single(v => v.Id == hidden.Id).Destination);
        Assert.Null(Now(sim, staffHidden.Id).ChasingVillagerId);
        Assert.Equal(open.Id, Now(sim, staffOpen.Id).ChasingVillagerId);
        Assert.NotNull(Now(sim, staffOpen.Id).Destination);
    }

    [Fact]
    public void Noise_inside_a_Fog_bank_carries_less_far()
    {
        using var sim = Arena();
        var at = new Vector2(20, 64);
        var noise = new Vector2(20, 90);
        var s = Sensitivity(sim, at);
        // Heard in the clear, but not through thick fog.
        var radius = 26f / (3 * s) * 1.4f;
        var staff = StaffWithRouteAwayFrom(sim, at, noise);
        ToClosingTime(sim);
        sim.Scenario.MakeNoise(noise, radius);
        sim.AdvanceTicks(2);
        Assert.True(Drawn(sim, Now(sim, staff.Id), noise), "Heard in the clear.");

        sim.Scenario.PlaceFogBank(noise, 40, 1f, TimeSpan.FromHours(10));
        sim.AdvanceTicks(1);
        var quiet = StaffWithRouteAwayFrom(sim, at, noise);
        sim.Scenario.MakeNoise(noise, radius);
        sim.AdvanceTicks(2);
        Assert.False(Drawn(sim, Now(sim, quiet.Id), noise), "Muffled by fog.");
    }

    [Fact]
    public void Noise_sensitivity_varies_by_region()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off));
        var values = sim.Snapshot.Sectors.Select(sector => sim.StaffNoiseSensitivity(sector.Id)).ToList();
        Assert.True(values.Distinct().Count() > 1);
        Assert.All(values, v => Assert.InRange(v, 0.6f, 1.6f));
    }

    private static bool HeardAt(Simulation sim, Landmark landmark, float fraction)
    {
        var at = landmark.Centre + new Vector2(landmark.Size.X / 2 + 10, 0);
        var s = Sensitivity(sim, at);
        var staff = StaffWithRouteAwayFrom(sim, at, landmark.Centre);
        ToClosingTime(sim);
        // Quiet enough to be muffled out of hearing from here, loud enough to carry in the open.
        var distance = Distance(sim, at, landmark.Centre);
        sim.Scenario.MakeNoise(landmark.Centre, distance / (3 * s) / fraction);
        sim.AdvanceTicks(2);
        return Drawn(sim, Now(sim, staff.Id), landmark.Centre);
    }

    [Fact]
    public void The_mattress_mountain_muffles_noise_but_the_box_fort_does_not_stop_the_Staff()
    {
        var seed = new[] { 3008, 42, 1, 2, 3, 4, 5, 6 }.First(x => TestStores.Get(x).Landmarks.Any(l => l.Type == LandmarkType.MattressMountain) &&
            TestStores.Get(x).Landmarks.Any(l => l.Type == LandmarkType.BoxFort));
        Simulation Store()
        {
            var sim = Simulation.Create(new WorldConfig(seed, StartingBands: false, RuntimeRngSeed: 1, Arrivals: ArrivalConfig.Off));
            sim.Scenario.SetFogBankFormation(false);
            return sim;
        }
        using (var sim = Store())
            Assert.False(HeardAt(sim, sim.Snapshot.Landmarks.First(l => l.Type == LandmarkType.MattressMountain), 0.7f), "Muffled by the mattresses.");
        using (var sim = Store())
            Assert.True(HeardAt(sim, sim.Snapshot.Landmarks.First(l => l.Type == LandmarkType.BoxFort), 0.7f), "Not stopped by the boxes.");
    }
}
