using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class WeatherTests
{
    private static Simulation NewWorld(int runtimeSeed = 1) =>
        Simulation.Create(new WorldConfig(Seed: 3008, StartingBands: false, RuntimeRngSeed: runtimeSeed,
            Arrivals: new ArrivalConfig { IntervalHours = 0 }));

    /// <summary>A world with no Fog banks, where none form on their own.</summary>
    private static Simulation FogFreeWorld()
    {
        var sim = NewWorld();
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.ClearFogBanks();
        return sim;
    }

    [Fact]
    public void Fog_bank_snapshots_are_read_only_and_stay_at_their_original_moment()
    {
        using var sim = FogFreeWorld();
        sim.Scenario.HoldWind(new Vector2(60f, 0f));
        var placed = sim.Scenario.PlaceFogBank(new Vector2(500f, 500f), radius: 100f);
        var before = sim.Snapshot;

        sim.Advance(TimeSpan.FromHours(1));

        Assert.Equal(placed, Assert.Single(before.FogBanks));
        Assert.NotEqual(placed.Position, Assert.Single(sim.Snapshot.FogBanks).Position);
        var collection = Assert.IsAssignableFrom<IList<FogBank>>(before.FogBanks);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection[0] = placed with { Density = 0f });
    }

    [Fact]
    public void A_fog_bank_is_thickest_at_its_centre_and_fades_to_nothing_at_its_edge()
    {
        using var sim = FogFreeWorld();
        var centre = new Vector2(400f, 500f);
        sim.Scenario.PlaceFogBank(centre, radius: 100f);

        var densities = new[] { 0f, 30f, 50f, 70f, 90f, 99f, 100f, 150f, 600f }
            .Select(d => sim.FogDensityAt(centre + new Vector2(d, 0f)))
            .ToArray();

        Assert.Equal(1f, densities[0], 3);
        Assert.Equal(1f, densities[1], 3);
        for (var i = 1; i < densities.Length; i++)
            Assert.True(densities[i] <= densities[i - 1], $"Density rose away from the centre: {string.Join(", ", densities)}");
        Assert.InRange(densities[3], 0.2f, 0.9f);
        Assert.InRange(densities[5], 0.0001f, 0.05f);
        Assert.Equal(0f, densities[6]);
        Assert.Equal(0f, densities[7]);
        Assert.Equal(0f, densities[8]);
    }

    [Fact]
    public void The_fog_density_query_uses_wrapped_distance_across_a_seam()
    {
        using var sim = FogFreeWorld();
        var size = sim.Snapshot.Space.Size;
        sim.Scenario.PlaceFogBank(new Vector2(10f, 500f), radius: 100f);

        var acrossSeam = sim.FogDensityAt(new Vector2(size - 50f, 500f)); // 60 m west, across the seam
        var sameSide = sim.FogDensityAt(new Vector2(70f, 500f));          // 60 m east

        Assert.True(acrossSeam > 0.2f, $"Density across the seam was {acrossSeam}.");
        Assert.Equal(sameSide, acrossSeam, 4);
    }

    [Fact]
    public void Overlapping_fog_banks_thicken_each_other_but_never_beyond_one()
    {
        using var sim = FogFreeWorld();
        var between = new Vector2(500f, 500f);
        sim.Scenario.PlaceFogBank(between - new Vector2(60f, 0f), radius: 100f, density: 0.6f);
        var one = sim.FogDensityAt(between);
        sim.Scenario.PlaceFogBank(between + new Vector2(60f, 0f), radius: 100f, density: 0.6f);
        var two = sim.FogDensityAt(between);
        sim.Scenario.PlaceFogBank(between, radius: 100f, density: 1f);

        Assert.True(two > one);
        Assert.Equal(1f, sim.FogDensityAt(between), 4);
    }

    [Fact]
    public void A_fog_bank_placed_through_the_scenario_builder_is_in_the_snapshot_and_raises_formed()
    {
        using var sim = FogFreeWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);

        var placed = sim.Scenario.PlaceFogBank(new Vector2(-20f, 700f), radius: 120f, density: 0.8f);

        var bank = Assert.Single(sim.Snapshot.FogBanks);
        Assert.Equal(placed, bank);
        Assert.Equal(new Vector2(sim.Snapshot.Space.Size - 20f, 700f), bank.Position);
        Assert.Equal((120f, 0.8f, FogBankStage.Lingering), (bank.Radius, bank.Density, bank.Stage));
        Assert.Equal(0.8f, sim.FogDensityAt(bank.Position), 4);
        var formed = Assert.Single(events.OfType<FogBankFormed>());
        Assert.Equal(placed, formed.Bank);
        Assert.Equal(sim.Snapshot.Clock, formed.At);
    }

    [Fact]
    public void A_placed_fog_bank_lingers_then_thins_out_and_dissipates()
    {
        using var sim = FogFreeWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        var placed = sim.Scenario.PlaceFogBank(new Vector2(600f, 600f), radius: 100f, lingerFor: TimeSpan.FromHours(2));

        sim.Advance(TimeSpan.FromHours(1.5));
        Assert.Equal(FogBankStage.Lingering, Assert.Single(sim.Snapshot.FogBanks).Stage);

        sim.Advance(TimeSpan.FromHours(1.5));
        var thinning = Assert.Single(sim.Snapshot.FogBanks);
        Assert.Equal(FogBankStage.Dissipating, thinning.Stage);
        Assert.InRange(thinning.Density, 0.01f, 0.99f);

        sim.Advance(TimeSpan.FromHours(2));
        Assert.Empty(sim.Snapshot.FogBanks);
        var dissipated = Assert.Single(events.OfType<FogBankDissipated>());
        Assert.Equal(placed.Id, dissipated.FogBankId);
        Assert.Equal(0f, sim.FogDensityAt(placed.Position));
    }

    [Fact]
    public void A_fog_bank_drifts_with_the_wind_across_a_seam()
    {
        using var sim = FogFreeWorld();
        var size = sim.Snapshot.Space.Size;
        sim.Scenario.HoldWind(new Vector2(60f, -10f));
        var placed = sim.Scenario.PlaceFogBank(new Vector2(size - 40f, 20f), radius: 100f);

        var positions = new List<Vector2>();
        for (var i = 0; i < 20; i++)
        {
            sim.Advance(TimeSpan.FromMinutes(6));
            positions.Add(Assert.Single(sim.Snapshot.FogBanks).Position);
        }

        // 2 Store hours at (60, -10) m/h: 120 m east across the east seam and 20 m north across the north seam.
        var expected = new Vector2(80f, 0f);
        var bank = Assert.Single(sim.Snapshot.FogBanks);
        Assert.Equal(placed.Id, bank.Id);
        Assert.True(sim.Snapshot.Space.Distance(expected, bank.Position) < 0.5f, $"The bank ended at {bank.Position}.");
        Assert.All(positions, p => Assert.True(p.X >= 0f && p.X < size && p.Y >= 0f && p.Y < size, $"Unwrapped position {p}."));
        Assert.True(sim.FogDensityAt(expected) > 0.99f);
        Assert.Equal(0f, sim.FogDensityAt(placed.Position + new Vector2(-100f, 0f)));
    }

    [Fact]
    public void Fog_banks_form_and_dissipate_over_store_time_with_clear_stretches_between_them()
    {
        using var sim = NewWorld();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        var space = sim.Snapshot.Space;
        const int grid = 48;
        var samples = Enumerable.Range(0, grid * grid)
            .Select(i => new Vector2((i % grid + 0.5f) * space.Size / grid, (i / grid + 0.5f) * space.Size / grid))
            .ToArray();

        for (var hour = 0; hour < 96; hour++)
        {
            sim.Advance(TimeSpan.FromHours(1));
            var snapshot = sim.Snapshot;
            Assert.NotEmpty(snapshot.FogBanks);
            var foggy = samples.Count(p => snapshot.FogDensityAt(p) > 0.3f) / (float)samples.Length;
            var clear = samples.Count(p => snapshot.FogDensityAt(p) < 0.05f) / (float)samples.Length;
            Assert.True(foggy > 0.01f, $"Hour {hour}: only {foggy:P1} of the Store was under fog.");
            Assert.True(clear > 0.55f, $"Hour {hour}: only {clear:P0} of the Store was clear.");
        }

        var formed = events.OfType<FogBankFormed>().ToList();
        var dissipated = events.OfType<FogBankDissipated>().ToList();
        Assert.True(formed.Count >= 8, $"Only {formed.Count} Fog banks formed in 4 Store days.");
        Assert.True(dissipated.Count >= 8, $"Only {dissipated.Count} Fog banks dissipated in 4 Store days.");
        Assert.All(formed, f => Assert.Equal(FogBankStage.Forming, f.Bank.Stage));
        Assert.Equal(formed.Count, formed.Select(f => f.Bank.Id).Distinct().Count());
        Assert.DoesNotContain(sim.Snapshot.FogBanks, b => dissipated.Any(d => d.FogBankId == b.Id));
    }

    [Fact]
    public void A_forming_fog_bank_grows_and_thickens()
    {
        using var sim = NewWorld();
        FogBank? forming = null;
        using var subscription = sim.Subscribe(e => forming ??= (e as FogBankFormed)?.Bank);
        while (forming is null)
            sim.Advance(TimeSpan.FromMinutes(10));

        sim.Advance(TimeSpan.FromMinutes(30));
        var later = sim.Snapshot.FogBanks.Single(b => b.Id == forming.Id);

        Assert.True(later.Radius > forming.Radius, $"Radius {forming.Radius} -> {later.Radius}.");
        Assert.True(later.Density > forming.Density, $"Density {forming.Density} -> {later.Density}.");
    }

    [Fact]
    public void A_new_world_opens_with_weather_already_under_way()
    {
        using var sim = NewWorld();

        Assert.NotEmpty(sim.Snapshot.FogBanks);
        Assert.NotEqual(Vector2.Zero, sim.Snapshot.Wind);
    }

    [Fact]
    public void The_wind_changes_slowly()
    {
        using var sim = NewWorld();
        var headings = new List<float>();
        for (var hour = 0; hour < 72; hour++)
        {
            sim.Advance(TimeSpan.FromHours(1));
            var wind = sim.Snapshot.Wind;
            Assert.InRange(wind.Length(), 15f, 75f);
            headings.Add(MathF.Atan2(wind.Y, wind.X));
        }

        var turns = headings.Zip(headings.Skip(1), (a, b) => MathF.Abs(MathF.IEEERemainder(b - a, MathF.Tau)) * 180f / MathF.PI).ToList();
        Assert.True(turns.Max() <= 15f, $"The wind turned {turns.Max():0.0} degrees in one Store hour.");
        Assert.True(turns.Sum() > 20f, "The wind should change direction over three Store days.");
    }

    [Fact]
    public void The_same_runtime_seed_gives_the_same_weather()
    {
        using var first = NewWorld(runtimeSeed: 7);
        using var second = NewWorld(runtimeSeed: 7);
        using var other = NewWorld(runtimeSeed: 8);

        foreach (var sim in new[] { first, second, other })
            sim.Advance(TimeSpan.FromHours(30));

        Assert.Equal(first.Snapshot.FogBanks, second.Snapshot.FogBanks);
        Assert.Equal(first.Snapshot.Wind, second.Snapshot.Wind);
        Assert.NotEqual(first.Snapshot.FogBanks, other.Snapshot.FogBanks);
    }

    [Fact]
    public void The_store_seed_does_not_decide_the_weather()
    {
        using var first = Simulation.Create(new WorldConfig(Seed: 3008, StartingBands: false, RuntimeRngSeed: 5));
        using var otherStore = Simulation.Create(new WorldConfig(Seed: 4242, StartingBands: false, RuntimeRngSeed: 5));

        Assert.Equal(first.Snapshot.FogBanks, otherStore.Snapshot.FogBanks);
    }
}
