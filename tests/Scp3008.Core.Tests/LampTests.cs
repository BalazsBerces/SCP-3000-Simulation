using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class LampTests
{
    [Fact]
    public void Display_lamps_keep_their_light_radius_when_the_store_closes_and_reopens()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.ClearFogBanks();
        var lamps = sim.Snapshot.Furniture.Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p.Type).IsLamp).ToArray();
        Assert.NotEmpty(lamps);
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        sim.AdvanceTo(TimeSpan.FromHours(23));
        Assert.Equal(0.0, sim.Snapshot.StoreLightLevel);
        Assert.Single(events.OfType<ClosingTimeBegan>());
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p.Type).IsLamp));
        Assert.All(lamps, p => Assert.Equal(p.Type is FurnitureType.TableLamp or FurnitureType.DeskLamp ? 3f : 5f,
            FurnitureCatalog.Spec(p.Type).LightRadius));

        sim.AdvanceTo(TimeSpan.FromHours(8));
        Assert.Equal(1.0, sim.Snapshot.StoreLightLevel);
        Assert.Single(events.OfType<StoreReopened>());
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p.Type).IsLamp));
    }

    private static readonly FurnitureType[] LampTypes = [FurnitureType.TableLamp, FurnitureType.FloorLamp, FurnitureType.DeskLamp, FurnitureType.ArcLamp,
        FurnitureType.LanternTent, FurnitureType.CampfireFlame, FurnitureType.CampLantern];

    [Fact]
    public void Lamps_give_light_and_other_furniture_does_not()
    {
        foreach (var type in Enum.GetValues<FurnitureType>())
        {
            var spec = FurnitureCatalog.Spec(type);
            Assert.Equal(LampTypes.Contains(type), spec.IsLamp);
            Assert.Equal(spec.IsLamp, spec.LightRadius > 0f);
        }
        Assert.True(FurnitureCatalog.Spec(FurnitureType.FloorLamp).LightRadius > FurnitureCatalog.Spec(FurnitureType.TableLamp).LightRadius);
    }

    [Fact]
    public void The_lighting_departments_are_full_of_lamps()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        var snapshot = sim.Snapshot;

        var lampsByDepartment = snapshot.Furniture
            .Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p.Type).IsLamp)
            .GroupBy(p => snapshot.Departments.At(p.Position).Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var lightingArea = snapshot.Departments.All.Where(d => d.Type == DepartmentType.LightingAndElectrical).Sum(d => d.Area);
        Assert.True(lampsByDepartment[DepartmentType.LightingAndElectrical] / lightingArea > 0.01f,
            "expected more than one Lamp per 100 m² in Lighting & electrical");
        foreach (var type in lampsByDepartment.Keys)
        {
            var area = snapshot.Departments.All.Where(d => d.Type == type).Sum(d => d.Area);
            Assert.True(lampsByDepartment[type] / area <= lampsByDepartment[DepartmentType.LightingAndElectrical] / lightingArea,
                $"{type} has a higher Lamp density than Lighting & electrical.");
        }
    }
}

public class LampLevelTests
{
    private static Simulation NewSim()
    {
        var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.ClearFogBanks();
        return sim;
    }

    private static FurniturePiece[] LampsOf(Simulation sim) =>
        sim.Snapshot.Furniture.Where(p => p.DisplayId >= 0 && FurnitureCatalog.Spec(p).IsLamp
            && !sim.Snapshot.GroundAt(p.Position).HasFlag(GroundProperty.Darkness)).ToArray();

    private static float[] Levels(Simulation sim, FurniturePiece[] lamps) =>
        lamps.Select(sim.Snapshot.LampLevel).ToArray();

    [Theory]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(21, 29)]
    [InlineData(21, 30)]
    public void Every_display_lamp_is_off_while_the_store_is_open(int hour, int minute)
    {
        using var sim = NewSim();
        var lamps = LampsOf(sim);
        Assert.NotEmpty(lamps);
        sim.AdvanceTo(new TimeSpan(hour, minute, 0));
        Assert.Equal(1.0, sim.Snapshot.StoreLightLevel);
        Assert.All(Levels(sim, lamps), level => Assert.Equal(0f, level));
    }

    [Theory]
    [InlineData(22, 0)]
    [InlineData(23, 0)]
    [InlineData(3, 0)]
    [InlineData(7, 29)]
    public void Every_display_lamp_is_fully_lit_at_closing_time(int hour, int minute)
    {
        using var sim = NewSim();
        var lamps = LampsOf(sim);
        sim.AdvanceTo(new TimeSpan(hour, minute, 0));
        Assert.Equal(DayPhase.ClosingTime, sim.Snapshot.Phase);
        Assert.All(Levels(sim, lamps), level => Assert.Equal(1f, level));
    }

    [Fact]
    public void Lamps_come_on_one_by_one_during_the_fade_before_closing_time()
    {
        using var sim = NewSim();
        var lamps = LampsOf(sim);
        sim.AdvanceTo(new TimeSpan(21, 30, 0));
        var first = new long?[lamps.Length];
        var full = new long?[lamps.Length];
        var previous = Levels(sim, lamps);
        var fadeTicks = (int)(StoreHours.LightFade / StoreClock.TickDuration);
        for (var tick = 1; tick <= fadeTicks; tick++)
        {
            sim.Advance(StoreClock.TickDuration);
            var now = Levels(sim, lamps);
            for (var i = 0; i < lamps.Length; i++)
            {
                Assert.True(now[i] >= previous[i], "a Lamp never dims while the Store lights fade out");
                if (now[i] > 0f) first[i] ??= tick;
                if (now[i] >= 1f) full[i] ??= tick;
            }
            previous = now;
            if (tick == fadeTicks / 2)
            {
                var lit = now.Count(l => l > 0f);
                Assert.InRange(lit, 1, lamps.Length - 1);
            }
        }
        Assert.All(full, t => Assert.NotNull(t));
        // Scattered: the moments are spread over most of the fade, not bunched together.
        Assert.True(first.Max() - first.Min() > fadeTicks / 2);
        Assert.True(first.Distinct().Count() > 20);
        // A short ramp, not a pop: more than a tick, well under the whole fade.
        Assert.All(Enumerable.Range(0, lamps.Length), i => Assert.InRange(full[i]!.Value - first[i]!.Value, 2, fadeTicks / 3));
    }

    [Fact]
    public void Lamps_go_out_one_by_one_as_the_store_lights_return()
    {
        using var sim = NewSim();
        var lamps = LampsOf(sim);
        sim.AdvanceTo(new TimeSpan(7, 30, 0));
        var out_ = new long?[lamps.Length];
        var previous = Levels(sim, lamps);
        Assert.All(previous, l => Assert.Equal(1f, l));
        var fadeTicks = (int)(StoreHours.LightFade / StoreClock.TickDuration);
        for (var tick = 1; tick <= fadeTicks; tick++)
        {
            sim.Advance(StoreClock.TickDuration);
            var now = Levels(sim, lamps);
            for (var i = 0; i < lamps.Length; i++)
            {
                Assert.True(now[i] <= previous[i], "a Lamp never brightens while the Store lights return");
                if (now[i] <= 0f) out_[i] ??= tick;
            }
            previous = now;
        }
        Assert.All(previous, l => Assert.Equal(0f, l));
        Assert.True(out_.Max() - out_.Min() > fadeTicks / 2);
        Assert.True(out_.Distinct().Count() > 20);
    }

    [Fact]
    public void A_lamp_switches_at_the_same_moment_every_store_day_and_every_run()
    {
        var times = new[] { new TimeSpan(21, 40, 0), new TimeSpan(21, 50, 0), new TimeSpan(7, 40, 0), new TimeSpan(7, 50, 0) };
        using var a = NewSim();
        using var b = NewSim();
        var lamps = LampsOf(a);
        foreach (var time in times)
        {
            a.AdvanceTo(time);
            b.AdvanceTo(time);
            var day1 = Levels(a, lamps);
            Assert.Equal(day1, Levels(b, lamps));
            Assert.Contains(day1, l => l is > 0f and < 1f);
            // The same time of day a Store day later.
            a.Advance(TimeSpan.FromDays(1));
            Assert.Equal(day1, Levels(a, lamps));
        }
    }
}
