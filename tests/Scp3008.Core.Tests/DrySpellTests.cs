using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class DrySpellTests
{
    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(0.5, 1, 3)]
    [InlineData(1, 0, 4)]
    public void Dry_spell_slows_one_Department_boosts_another_and_ends_with_normal_refill(double strength, int dryUnits, int fastUnits)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 17,
            Restock: new RestockConfig { GoodsPerStoreHour = 1, DrySpellIntervalHours = 4,
                DrySpellDurationHours = 4, DrySpellStrength = strength }));
        var events = new List<DomainEvent>();
        using var subscription = sim.Subscribe(events.Add);
        sim.Scenario.SetFogBankFormation(false);
        while (sim.Snapshot.DrySpells.Count == 0)
            sim.AdvanceTicks(1);
        var before = sim.Snapshot;
        var spell = Assert.Single(before.DrySpells);
        var pieces = before.Furniture.ToDictionary(p => p.Id);
        StockedGoods Source(int department) => before.Stock.First(g => !g.LeftBehind && g.Amount >= 3 &&
            before.Departments.At(pieces[g.FurnitureId].Position).Id == department);
        var dry = Source(spell.DepartmentId);
        var fast = Source(spell.BoostedDepartmentId);
        sim.Scenario.SetStock([(dry.FurnitureId, dry.Kind), (fast.FurnitureId, fast.Kind)], 10);
        sim.Scenario.SetStock([(dry.FurnitureId, dry.Kind), (fast.FurnitureId, fast.Kind)], 0);
        sim.Advance(TimeSpan.FromHours(2));
        Assert.Equal(dryUnits, sim.Snapshot.Stock.Where(g => g.FurnitureId == dry.FurnitureId && g.Kind == dry.Kind).Sum(g => g.Amount));
        Assert.Equal(fastUnits, sim.Snapshot.Stock.Single(g => g.FurnitureId == fast.FurnitureId && g.Kind == fast.Kind).Amount);
        sim.AdvanceTicks(spell.EndsAt.Tick - sim.Snapshot.Clock.Tick);
        Assert.Empty(sim.Snapshot.DrySpells);
        Assert.Single(before.DrySpells);
        Assert.Equal(spell, Assert.Single(events.OfType<DrySpellStarted>()).Spell);
        var ended = Assert.Single(events.OfType<DrySpellEnded>());
        Assert.Equal(spell, ended.Spell);
        Assert.Equal(spell.EndsAt, ended.At);
        sim.Scenario.SetStock([(dry.FurnitureId, dry.Kind), (fast.FurnitureId, fast.Kind)], 0);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, sim.Snapshot.Stock.Single(g => g.FurnitureId == dry.FurnitureId && g.Kind == dry.Kind).Amount);
        Assert.Equal(1, sim.Snapshot.Stock.Single(g => g.FurnitureId == fast.FurnitureId && g.Kind == fast.Kind).Amount);
    }
    [Fact]
    public void Long_run_visits_different_Departments_and_Sectors_without_stopping_all_food()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 17,
            Restock: new RestockConfig { GoodsPerStoreHour = 0, DrySpellStrength = 1,
                DrySpellIntervalHours = 24, DrySpellDurationHours = 24 }));
        sim.Scenario.SetFogBankFormation(false);
        sim.Scenario.ClearFogBanks();
        var starts = new List<DrySpellStarted>();
        var ends = new List<DrySpellEnded>();
        using var subscription = sim.Subscribe(e =>
        {
            if (e is DrySpellStarted start) starts.Add(start);
            if (e is DrySpellEnded end) ends.Add(end);
        });
        var initial = sim.Snapshot;
        var pieces = initial.Furniture.ToDictionary(p => p.Id);
        var foodDepartments = initial.Stock.Where(g => !g.LeftBehind && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food)
            .Select(g => initial.Departments.At(pieces[g.FurnitureId].Position).Id).Distinct().ToArray();
        sim.Advance(TimeSpan.FromDays(60));
        Assert.InRange(starts.Count, 20, 40);
        Assert.True(starts.Select(e => e.Spell.DepartmentId).Distinct().Count() > 10);
        Assert.True(starts.Select(e => initial.SectorAt(initial.Departments.All[e.Spell.DepartmentId].Anchor).Id).Distinct().Count() > 3);
        Assert.All(starts, e =>
        {
            Assert.Equal(e.At, e.Spell.StartedAt);
            Assert.Equal(StoreClock.TicksFor(TimeSpan.FromHours(24)), e.Spell.EndsAt.Tick - e.At.Tick);
            Assert.Contains(foodDepartments, id => id != e.Spell.DepartmentId);
        });
        Assert.Equal(starts.Take(ends.Count).Select(e => e.Spell), ends.Select(e => e.Spell));
        Assert.All(ends, e => Assert.Equal(e.Spell.EndsAt, e.At));
        Assert.All(starts.Skip(1), e => Assert.Contains(ends, end => end.At.Tick < e.At.Tick));
    }

    [Fact]
    public void Runtime_seed_reproduces_events_independently_of_weather_but_different_runtime_seeds_vary_them()
    {
        DrySpell[] Run(int runtimeSeed, bool weather, int mapSeed = 3008)
        {
            using var sim = Simulation.Create(new WorldConfig(mapSeed, StartingBands: false, RuntimeRngSeed: runtimeSeed,
                PointOfInterestShowcase: true, Restock: new RestockConfig
                { DrySpellIntervalHours = 2, DrySpellDurationHours = 1 }));
            sim.Scenario.SetFogBankFormation(weather);
            var spells = new List<DrySpell>();
            using var subscription = sim.Subscribe(e => { if (e is DrySpellStarted start) spells.Add(start.Spell); });
            sim.Advance(TimeSpan.FromHours(24));
            return spells.ToArray();
        }
        var first = Run(17, false);
        Assert.NotEmpty(first);
        Assert.Equal(first, Run(17, true));
        Assert.Equal(first.Select(s => s.StartedAt.Tick), Run(17, false, 123).Select(s => s.StartedAt.Tick));
        Assert.NotEqual(first.Select(s => s.StartedAt.Tick), Run(18, false).Select(s => s.StartedAt.Tick));
    }

    [Theory]
    [InlineData(-1, 24, 1)]
    [InlineData(double.NaN, 24, 1)]
    [InlineData(double.PositiveInfinity, 24, 1)]
    [InlineData(24, 0, 1)]
    [InlineData(24, double.NaN, 1)]
    [InlineData(24, double.PositiveInfinity, 1)]
    [InlineData(24, double.MaxValue, 1)]
    [InlineData(24, 24, -0.1)]
    [InlineData(24, 24, 1.1)]
    [InlineData(24, 24, double.NaN)]
    public void Invalid_Dry_spell_configuration_is_rejected(double interval, double duration, double strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new WorldConfig(3008,
            Restock: new RestockConfig { DrySpellIntervalHours = interval,
                DrySpellDurationHours = duration, DrySpellStrength = strength })));
    }

    [Fact]
    public void Zero_frequency_disables_Dry_spells_and_a_single_food_Department_is_never_stopped()
    {
        using var disabled = Simulation.Create(new WorldConfig(3008, PointOfInterestShowcase: true,
            Restock: new RestockConfig { DrySpellIntervalHours = 0 }));
        var events = new List<DomainEvent>();
        using var subscription = disabled.Subscribe(events.Add);
        disabled.Advance(TimeSpan.FromDays(7));
        Assert.Empty(disabled.Snapshot.DrySpells);
        Assert.Empty(events.OfType<DrySpellStarted>());
        using var sandbox = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Restock: new RestockConfig { GoodsPerStoreHour = 1, DrySpellIntervalHours = 0.1, DrySpellStrength = 1 }));
        var shelf = sandbox.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new System.Numerics.Vector2(20));
        sandbox.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 3);
        sandbox.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 0);
        sandbox.Advance(TimeSpan.FromHours(2));
        Assert.Empty(sandbox.Snapshot.DrySpells);
        Assert.Equal(2, Assert.Single(sandbox.Snapshot.Stock).Amount);
    }
    [Fact]
    public void Dry_spell_also_delays_Furniture_return_while_the_other_Department_returns_it_faster()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 17,
            Restock: new RestockConfig { FurnitureHours = 2, LandmarkFurnitureHours = 2,
                DrySpellIntervalHours = 4, DrySpellDurationHours = 4, DrySpellStrength = 1 }));
        sim.Scenario.SetFogBankFormation(false);
        while (sim.Snapshot.DrySpells.Count == 0) sim.AdvanceTicks(1);
        var spell = Assert.Single(sim.Snapshot.DrySpells);
        FurniturePiece Piece(int id) => sim.Snapshot.Furniture.First(p =>
            sim.Snapshot.Departments.At(p.Position).Id == id && sim.Snapshot.IsStockFurniture(p) && !FurnitureCatalog.Spec(p).IsFixed);
        var dry = Piece(spell.DepartmentId);
        var fast = Piece(spell.BoostedDepartmentId);
        sim.Scenario.RemoveFurniture(p => p.Id == dry.Id || p.Id == fast.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(fast, sim.Snapshot.Furniture);
        Assert.DoesNotContain(dry, sim.Snapshot.Furniture);
        sim.AdvanceTicks(spell.EndsAt.Tick - sim.Snapshot.Clock.Tick);
        Assert.DoesNotContain(dry, sim.Snapshot.Furniture);
        sim.Advance(TimeSpan.FromHours(2));
        Assert.Contains(dry, sim.Snapshot.Furniture);
    }
}
