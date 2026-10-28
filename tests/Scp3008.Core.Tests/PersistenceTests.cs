using System.Text.Json;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PersistenceTests
{
    [Theory]
    [InlineData("VillageId", 999)]
    [InlineData("Id", -1)]
    public void Damaged_Villager_references_are_rejected_before_the_clock_can_crash(string field, int value)
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox("empty");
        var person = original.Scenario.PlaceVillager(new(20, 108));
        original.Scenario.PlaceVillage(person.Position, [person.Id]);
        using var save = new MemoryStream();
        original.Save(save);
        var document = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        document["State"]!["Villagers"]!["Walkers"]![0]!["Villager"]![field] = value;
        using var damaged = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => Simulation.Load(damaged));
    }

    [Fact]
    public void Save_load_restores_the_snapshot_and_continues_the_same_world()
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox("scavenge");
        original.Scenario.PlaceFogBank(new(10, 20), 25);
        original.Scenario.HoldWind(new(12, -4));
        original.Advance(TimeSpan.FromHours(1));
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        AssertSnapshot(original, loaded);
        var originalEvents = new List<DomainEvent>();
        var loadedEvents = new List<DomainEvent>();
        using var first = original.Subscribe(originalEvents.Add);
        using var second = loaded.Subscribe(loadedEvents.Add);
        original.Advance(TimeSpan.FromHours(3));
        loaded.Advance(TimeSpan.FromHours(3));
        AssertSnapshot(original, loaded);
        Assert.Equal(EventText(originalEvents), EventText(loadedEvents));
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.DiamondLarge)]
    public void Full_Store_layout_weather_and_Restock_survive_loading(PillarLayout layout)
    {
        using var original = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 42, PillarLayout: layout,
            StartingBands: false, Restock: new RestockConfig { DrySpellIntervalHours = 1 },
            // One arrival late in the continuation keeps the saved arrival stream covered without days of Wanderers.
            Arrivals: new ArrivalConfig { IntervalHours = 23 }));
        var removed = original.Snapshot.Furniture.First(p => p.Type == FurnitureType.Armchair);
        original.Scenario.RemoveFurniture(p => p.Id == removed.Id);
        original.Advance(TimeSpan.FromHours(2));
        Assert.NotEmpty(original.Snapshot.DrySpells);
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        AssertSnapshot(original, loaded);
        var expectedEvents = new List<DomainEvent>();
        var actualEvents = new List<DomainEvent>();
        using var first = original.Subscribe(expectedEvents.Add);
        using var second = loaded.Subscribe(actualEvents.Add);
        original.Advance(TimeSpan.FromHours(24));
        loaded.Advance(TimeSpan.FromHours(24));
        Assert.Contains(loaded.Snapshot.Furniture, p => p.Id == removed.Id);
        Assert.Contains(actualEvents, e => e is WanderersArrived);
        AssertSnapshot(original, loaded);
        Assert.Equal(EventText(expectedEvents), EventText(actualEvents));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("starving")]
    [InlineData("staff nearby")]
    [InlineData("closing soon")]
    public void Sandbox_continues_Needs_death_knowledge_and_Staff_patrols(string preset)
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox(preset);
        original.Speed = SimulationSpeed.Fast4;
        original.AdvanceRealTime(TimeSpan.FromMilliseconds(25));
        original.Advance(TimeSpan.FromHours(1));
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        AssertSnapshot(original, loaded);
        original.AdvanceRealTime(TimeSpan.FromMilliseconds(25));
        loaded.AdvanceRealTime(TimeSpan.FromMilliseconds(25));
        original.Advance(TimeSpan.FromHours(48));
        loaded.Advance(TimeSpan.FromHours(48));
        AssertSnapshot(original, loaded);
        Assert.Equal(original.Scenario.PlaceVillager(new(20, 100)).Id, loaded.Scenario.PlaceVillager(new(20, 100)).Id);
    }

    [Theory]
    [InlineData("{\"Version\":999}", "Incompatible world save version 999")]
    [InlineData("{}", "missing format version")]
    [InlineData("{\"Version\":13,\"State\":{}}", "Invalid or damaged")]
    [InlineData("{\"Version\":13,\"State\":null}", "Invalid or damaged")]
    [InlineData("{", "Invalid or damaged")]
    public void Incompatible_and_damaged_saves_have_clear_errors(string json, string message)
    {
        using var save = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var error = Assert.Throws<InvalidDataException>(() => Simulation.Load(save));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Running_world_can_be_saved_without_stopping_its_thread()
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox("empty");
        original.Start();
        using var save = new MemoryStream();
        original.Save(save);
        Assert.True(original.IsRunning);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.False(loaded.IsRunning);
        Assert.Equal(original.Snapshot.Seed, loaded.Snapshot.Seed);
        loaded.AdvanceTicks(1);
        Assert.True(loaded.Snapshot.Clock.Tick > 0);
    }

    [Fact]
    public void Corrupt_stock_is_rejected_before_the_world_can_run()
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox("scavenge");
        using var save = new MemoryStream();
        original.Save(save);
        var document = System.Text.Json.Nodes.JsonNode.Parse(save.ToArray())!;
        document["State"]!["Snapshot"]!["Stock"]![0]!["FurnitureId"] = 999;
        using var damaged = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Contains("inconsistent world state", Assert.Throws<InvalidDataException>(() => Simulation.Load(damaged)).Message);
    }

    [Fact]
    public void Village_stores_chronicle_and_Villager_work_continue_after_loading()
    {
        using var original = ScenarioBuilder.LoadVillagerSandbox("empty");
        var shelf = original.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
        original.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        original.Scenario.PlaceWaterPoint(new(22, 108));
        original.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
        original.Advance(TimeSpan.FromHours(1));
        Assert.True(Assert.Single(original.Snapshot.Villages).Stores.GetValueOrDefault(GoodsKind.TinnedFood) > 0);
        Assert.NotEmpty(original.Snapshot.Chronicle);
        using var save = new MemoryStream();
        original.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        AssertSnapshot(original, loaded);
        original.Advance(TimeSpan.FromHours(3));
        loaded.Advance(TimeSpan.FromHours(3));
        AssertSnapshot(original, loaded);
    }

    private static string[] EventText(IEnumerable<DomainEvent> events) => events
        .Select(e => JsonSerializer.Serialize(e, e.GetType(), new JsonSerializerOptions { IncludeFields = true })).ToArray();

    private static void AssertSnapshot(Simulation original, Simulation loaded) =>
        Assert.Equal(JsonSerializer.Serialize(original.Snapshot, new JsonSerializerOptions { IncludeFields = true }),
            JsonSerializer.Serialize(loaded.Snapshot, new JsonSerializerOptions { IncludeFields = true }));
}
