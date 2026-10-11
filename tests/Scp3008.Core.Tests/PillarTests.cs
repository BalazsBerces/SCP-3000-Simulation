using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PillarTests
{
    [Fact]
    public void The_same_store_seed_yields_the_same_pillars_despite_different_weather_and_time()
    {
        using var first = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        using var second = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 2));
        first.Scenario.ClearFogBanks();
        second.Advance(TimeSpan.FromHours(12));

        Assert.NotEmpty(first.Snapshot.Pillars);
        Assert.Equal(first.Snapshot.Pillars, second.Snapshot.Pillars);
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(-1)]
    [InlineData(42)]
    public void Many_pillars_cover_the_store_and_each_has_a_unique_readable_marking(int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromMinutes(1));
        var snapshot = sim.Snapshot;
        Assert.True(snapshot.Pillars.Count >= 100);
        Assert.Equal(snapshot.Space.ChunkCount, snapshot.Pillars.Select(p => snapshot.Space.ChunkOf(p.Position)).Distinct().Count());
        Assert.Equal(snapshot.Pillars.Count, snapshot.Pillars.Select(p => p.Id).Distinct().Count());
        Assert.Equal(snapshot.Pillars.Count, snapshot.Pillars.Select(p => p.Marking.Number).Distinct().Count());
        Assert.All(snapshot.Pillars, p =>
        {
            Assert.Matches(@"^\d{3}$", p.Marking.Number);
            Assert.True(Enum.IsDefined(p.Marking.Band));
            Assert.True(Enum.IsDefined(p.Marking.Damage));
            Assert.Equal(snapshot.Space.Wrap(p.Position), p.Position);
            Assert.InRange(p.Height, 30f, 60f);
            Assert.InRange(p.Width, 2f, 3f);
        });
    }

    [Fact]
    public void Different_store_seeds_change_pillar_positions_and_markings()
    {
        using var first = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 1));
        using var second = Simulation.Create(new WorldConfig(43, RuntimeRngSeed: 1));
        second.Advance(TimeSpan.FromMinutes(1));
        Assert.NotEqual(first.Snapshot.Pillars.Select(p => p.Position), second.Snapshot.Pillars.Select(p => p.Position));
        Assert.NotEqual(first.Snapshot.Pillars.Select(p => p.Marking), second.Snapshot.Pillars.Select(p => p.Marking));
    }

    [Fact]
    public void Pillars_remain_inert_landmarks_through_closing_time_and_reopening()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        sim.Scenario.ClearFogBanks();
        sim.Scenario.SetFogBankFormation(false);
        var pillars = sim.Snapshot.Pillars.ToArray();
        var lamps = sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp).ToArray();
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.AdvanceTo(TimeSpan.FromHours(23));
        Assert.Equal(0, sim.Snapshot.StoreLightLevel);
        Assert.Equal(pillars, sim.Snapshot.Pillars);
        Assert.Equal(lamps, sim.Snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp));
        Assert.Single(events.OfType<ClosingTimeBegan>());
        sim.AdvanceTo(TimeSpan.FromHours(8));
        Assert.Equal(pillars, sim.Snapshot.Pillars);
        Assert.Single(events.OfType<StoreReopened>());
    }

    [Fact]
    public void Pillars_on_both_seams_keep_clear_of_furniture_displays()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1));
        sim.Advance(TimeSpan.FromMinutes(1));
        var snapshot = sim.Snapshot;
        Assert.Contains(snapshot.Pillars, p => p.Position.X == 0);
        Assert.Contains(snapshot.Pillars, p => p.Position.Y == 0);
        foreach (var pillar in snapshot.Pillars)
        {
            // Conservative axis-aligned bounds of each rotated Furniture footprint.
            foreach (var piece in snapshot.Furniture.Where(p => snapshot.Space.Distance(p.Position, pillar.Position) < 10f))
            {
                var spec = FurnitureCatalog.Spec(piece.Type);
                var delta = snapshot.Space.Delta(pillar.Position, piece.Position);
                var c = MathF.Abs(MathF.Cos(piece.Rotation));
                var s = MathF.Abs(MathF.Sin(piece.Rotation));
                Assert.True(MathF.Abs(delta.X) >= (pillar.Width + c * spec.Width + s * spec.Depth) / 2f ||
                    MathF.Abs(delta.Y) >= (pillar.Width + s * spec.Width + c * spec.Depth) / 2f,
                    $"Pillar {pillar.Marking.Number} overlaps Furniture {piece.Id}.");
            }
        }
    }
}
