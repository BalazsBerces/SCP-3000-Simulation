using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class ChronicleTests
{
    [Fact]
    public void First_Gear_stories_have_several_templates_names_and_places()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        for (var i = 0; i < 3; i++)
        {
            var person = sim.Scenario.PlaceVillager(new(20 + i, 108));
            sim.Scenario.PlaceVillage(person.Position, [person.Id]);
            sim.Scenario.SetMaterials(i, MaterialKind.WoodPanel, 2);
            sim.Scenario.MakeGear(person.Id, GearKind.TableLegClub);
        }
        sim.Advance(TimeSpan.FromMinutes(31));
        var stories = sim.Snapshot.Chronicle;
        Assert.Equal(3, stories.Count);
        Assert.Contains("made the first Gear", stories[0]);
        Assert.Contains("finished Table-leg club", stories[1]);
        Assert.Contains("celebrated its first Gear", stories[2]);
        Assert.All(stories, story => Assert.Contains("Sector A-1", story));
    }

    [Theory]
    [InlineData(DeathCause.Thirst)]
    [InlineData(DeathCause.Starvation)]
    public void Death_stories_keep_names_causes_places_and_Village_history_through_save_load(DeathCause cause)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Needs: new NeedsConfig(StarvationHours: 0.1, DehydrationHours: 0.1)));
        for (var i = 0; i < 3; i++)
            sim.Scenario.PlaceBand([new(20, 104)],
                new Needs(Rest: 0.1, Hunger: cause == DeathCause.Starvation ? 1 : 0,
                    Thirst: cause == DeathCause.Thirst ? 1 : 0));
        sim.AdvanceTicks(1);
        var founding = sim.Snapshot;
        using var saved = new MemoryStream();
        sim.Save(saved);
        saved.Position = 0;
        using var loaded = Simulation.Load(saved);
        loaded.Advance(TimeSpan.FromHours(0.2));

        Assert.Empty(loaded.Snapshot.Villagers);
        Assert.Equal(3, loaded.Snapshot.Bodies.Count);
        Assert.Equal(6, loaded.Snapshot.Chronicle.Count);
        Assert.Equal(founding.Chronicle, loaded.Snapshot.Chronicle.Take(3));
        Assert.Equal(3, founding.Chronicle.Count);
        var deaths = loaded.Snapshot.Chronicle.Skip(3).ToArray();
        Assert.Contains("died of", deaths[0]);
        Assert.Contains("claimed", deaths[1]);
        Assert.Contains("took its toll", deaths[2]);
        for (var i = 0; i < 3; i++)
        {
            Assert.Contains($"of Village {i}", deaths[i]);
            Assert.Contains(founding.Villagers[i].Name, deaths[i]);
            Assert.Contains("Sector A-1", deaths[i]);
            Assert.Contains(cause == DeathCause.Thirst ? "thirst" : "starvation", deaths[i]);
        }
        saved.SetLength(0);
        loaded.Save(saved);
        saved.Position = 0;
        using var restored = Simulation.Load(saved);
        Assert.Equal(loaded.Snapshot.Chronicle, restored.Snapshot.Chronicle);
        restored.Advance(TimeSpan.FromHours(1));
        Assert.Equal(6, restored.Snapshot.Chronicle.Count);
    }

    [Fact]
    public void A_Wanderer_death_is_recorded_without_inventing_a_Village()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Needs: new NeedsConfig(DehydrationHours: 0.1)));
        var person = sim.Scenario.PlaceVillager(new(20, 104), new Needs(Thirst: 1));
        sim.Advance(TimeSpan.FromHours(0.2));
        var story = Assert.Single(sim.Snapshot.Chronicle);
        Assert.Contains(person.Name, story);
        Assert.Contains("thirst", story);
        Assert.DoesNotContain("Village", story);
    }

    [Fact]
    public void Founding_uses_several_stories_with_names_and_places()
    {
        using var sim = ScenarioBuilder.LoadVillagerSandbox("empty");
        for (var i = 0; i < 3; i++)
            sim.Scenario.PlaceBand([new(20, 104)], new Needs(Rest: 0.1));

        sim.AdvanceTicks(1);

        Assert.Equal(3, sim.Snapshot.Chronicle.Count);
        Assert.Contains("Mara", sim.Snapshot.Chronicle[0]);
        Assert.Contains("founded Village 0", sim.Snapshot.Chronicle[0]);
        Assert.Contains("made a home", sim.Snapshot.Chronicle[1]);
        Assert.Contains("put down roots", sim.Snapshot.Chronicle[2]);
        Assert.All(sim.Snapshot.Chronicle, line =>
        {
            Assert.Contains("Sector A-1", line);
            Assert.Contains("before their strength ran out", line);
        });
    }
}
