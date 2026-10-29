using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class ClimbingTests
{
    private static Simulation Sandbox() => ScenarioBuilder.LoadVillagerSandbox("empty");

    [Fact]
    public void Climbable_links_connect_across_a_Seam_and_an_unsupported_Lookout_is_unreachable()
    {
        using var sim = Sandbox();
        sim.Scenario.PlaceFurniture(FurnitureType.PalletRack, new(1, 100));
        var person = sim.Scenario.PlaceVillager(new(126, 104));
        sim.Scenario.SendToLookout(person.Id, new(1, 100.55f, 9.2f));
        Assert.Null(sim.Snapshot.Villagers.Single().Destination);
        sim.Scenario.SendToLookout(person.Id, new(1, 100.55f, 4.6f));
        for (var i = 0; i < 1000 && sim.Snapshot.Villagers.Single().Elevation < 4.6f; i++) sim.AdvanceTicks(1);
        Assert.Equal(4.6f, sim.Snapshot.Villagers.Single().Elevation, 2);
        Assert.InRange(sim.Snapshot.Space.Distance(sim.Snapshot.Villagers.Single().Position, new(1, 100.55f)), 0, 0.01f);
    }

    [Fact]
    public void Height_reveals_Goods_beyond_floor_sight_and_a_local_rebuild_keeps_the_climb()
    {
        using var sim = Sandbox();
        for (var i = 0; i < 5; i++)
            sim.Scenario.PlaceFurniture(FurnitureType.PalletRack, new(20, 100), elevation: i * 4.6f);
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 30));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
        var person = sim.Scenario.PlaceVillager(new(20, 104));
        sim.AdvanceTicks(1);
        Assert.DoesNotContain(sim.Snapshot.Villagers.Single().KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
        sim.Scenario.SendToLookout(person.Id, new(20, 100.55f, 23));
        sim.Advance(TimeSpan.FromMinutes(20));
        var height = sim.Snapshot.Villagers.Single().Elevation;
        sim.Scenario.PlaceFurniture(FurnitureType.Chair, new(40, 105));
        Assert.Equal(height, sim.Snapshot.Villagers.Single().Elevation);
        sim.Advance(TimeSpan.FromMinutes(1));
        Assert.True(sim.Snapshot.Villagers.Single().Elevation > height);
        for (var i = 0; i < 1000 && !sim.Snapshot.Villagers.Single().KnownMap.Goods.Any(g => g.Goods.FurnitureId == shelf.Id); i++) sim.AdvanceTicks(1);
        Assert.Contains(sim.Snapshot.Villagers.Single().KnownMap.Goods, g => g.Goods.FurnitureId == shelf.Id);
    }

    [Fact]
    public void Breaking_a_scaffolds_support_collapses_it_and_clears_the_Blueprint()
    {
        using var sim = Sandbox();
        var pillar = sim.Snapshot.Pillars.Single();
        var at = pillar.Position + new Vector2(0, pillar.Width / 2 + 0.55f);
        for (var i = 0; i < 8; i++) sim.Scenario.PlaceFurniture(FurnitureType.PalletRack, at, elevation: i * 4.6f);
        var person = sim.Scenario.PlaceVillager(at + new Vector2(0, 3));
        var village = sim.Scenario.PlaceVillage(person.Position, [person.Id]);
        var plan = sim.Scenario.PlanPillarScaffold(village.Id, pillar.Id);
        sim.Scenario.SendToLookout(person.Id, plan.Lookout);
        sim.Advance(TimeSpan.FromMinutes(20));
        sim.Scenario.RemoveFurniture(p => p.Id == plan.Slots[0].FurnitureId);
        sim.AdvanceTicks(1);
        Assert.False(sim.Snapshot.Villages.Single().PillarScaffold!.Complete);
        Assert.True(sim.Snapshot.Villages.Single().PillarScaffold!.Slots.All(s => s.FurnitureId is null));
        Assert.Equal(0, sim.Snapshot.Villagers.Single().Elevation);
    }

    [Theory]
    [InlineData(LandmarkType.RackCanyon)]
    [InlineData(LandmarkType.MattressMountain)]
    public void Generated_Lookouts_are_reached_through_their_Furniture(LandmarkType type)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, LandmarkShowcase: true, StartingBands: false));
        sim.Scenario.ClearFogBanks(); sim.Scenario.SetFogBankFormation(false);
        var landmark = sim.Snapshot.Landmarks.Single(l => l.Type == type);
        sim.Scenario.RemoveFurniture(p => p.LandmarkId != landmark.Id);
        var lookout = landmark.Lookouts[0];
        var person = sim.Scenario.PlaceVillager(type == LandmarkType.RackCanyon
            ? new Vector2(lookout.X, lookout.Y) + new Vector2(0, 3)
            : landmark.Centre + new Vector2(0, landmark.Size.Y / 2 + 3), new Needs(Hunger: 0.5));
        sim.AdvanceTicks(1);
        Assert.NotNull(sim.Snapshot.Villagers.Single().Destination);
        for (var i = 0; i < 4000 && sim.Snapshot.Villagers.Single().Elevation < lookout.Z; i++) sim.AdvanceTicks(1);
        Assert.Equal(lookout.Z, sim.Snapshot.Villagers.Single().Elevation, 2);
    }

    [Fact]
    public void Finishing_the_Abandoned_scaffold_reuses_its_stages_and_costs_fewer_Materials()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, LandmarkShowcase: true, StartingBands: false));
        var abandoned = Assert.Single(sim.Snapshot.Landmarks, l => l.Type == LandmarkType.AbandonedScaffold);
        var pillar = sim.Snapshot.NearestPillar(abandoned.Centre);
        var member = sim.Scenario.PlaceVillager(abandoned.Centre + new Vector2(0, 3));
        var helper = sim.Scenario.PlaceVillager(member.Position + Vector2.UnitX);
        var village = sim.Scenario.PlaceVillage(member.Position, [member.Id, helper.Id]);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 500);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 500);
        var plan = sim.Scenario.PlanPillarScaffold(village.Id, pillar.Id);
        Assert.Equal(5, plan.Slots.Count(s => s.FurnitureId is not null));
        Assert.Equal(24, plan.MaterialsNeeded);
        Assert.True(plan.MaterialsNeeded < PillarScaffold.Stages * PillarScaffold.MetalPerStage);
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Metal, plan.MaterialsNeeded);
        for (var i = 0; i < 6000 && sim.Snapshot.Villages[0].PillarScaffold?.Complete != true; i++) sim.AdvanceTicks(1);
        Assert.True(sim.Snapshot.Villages[0].PillarScaffold!.Complete);
        Assert.Equal(0, sim.Snapshot.Villages[0].Materials.GetValueOrDefault(MaterialKind.Metal));
    }

    [Fact]
    public void A_Villager_climbs_a_built_tower_and_walks_down_again()
    {
        using var sim = Sandbox();
        for (var i = 0; i < 5; i++)
            sim.Scenario.PlaceFurniture(FurnitureType.PalletRack, new(20, 100), elevation: i * 4.6f);
        var person = sim.Scenario.PlaceVillager(new(20, 104));
        var top = new Vector3(20, 100.55f, 23);
        sim.Scenario.SendToLookout(person.Id, top);
        sim.Advance(TimeSpan.FromMinutes(12));
        var climbing = sim.Snapshot.Villagers.Single();
        Assert.InRange(climbing.Elevation, 1, 22);
        Assert.True(climbing.ViewRange > 40);
        using var saved = new MemoryStream();
        sim.Save(saved); saved.Position = 0;
        using var loaded = Simulation.Load(saved);
        for (var i = 0; i < 1000 && loaded.Snapshot.Villagers.Single().Elevation < 23; i++) loaded.AdvanceTicks(1);
        Assert.Equal(23, loaded.Snapshot.Villagers.Single().Elevation, 2);
        loaded.Scenario.SendVillager(person.Id, new Vector2(25, 104));
        for (var i = 0; i < 2000 && loaded.Snapshot.Villagers.Single().Elevation > 0; i++) loaded.AdvanceTicks(1);
        Assert.Equal(0, loaded.Snapshot.Villagers.Single().Elevation);
    }

    [Fact]
    public void A_Pillar_scaffold_is_built_one_stable_piece_at_a_time_from_Materials()
    {
        using var sim = Sandbox();
        var pillar = sim.Snapshot.Pillars.Single();
        var at = pillar.Position + new Vector2(0, pillar.Width / 2 + 5);
        var member = sim.Scenario.PlaceVillager(at);
        var helper = sim.Scenario.PlaceVillager(at + Vector2.UnitX);
        var village = sim.Scenario.PlaceVillage(at, [member.Id, helper.Id], Personality.Builders);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 500);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 500);
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Metal, 100);
        var plan = sim.Scenario.PlanPillarScaffold(village.Id, pillar.Id);
        Assert.False(plan.Complete);
        for (var i = 0; i < 6000 && sim.Snapshot.Villages[0].PillarScaffold?.Complete != true; i++) sim.AdvanceTicks(1);
        var built = sim.Snapshot.Villages[0].PillarScaffold!;
        Assert.True(built.Complete);
        Assert.Equal(36, sim.Snapshot.Villages[0].Materials[MaterialKind.Metal]);
        Assert.Equal(36.8f, built.Lookout.Z, 2);
        Assert.All(built.Slots, s => Assert.Equal(0, sim.Snapshot.Furniture.Single(p => p.Id == s.FurnitureId).Damage));
        sim.Scenario.SendToLookout(member.Id, built.Lookout);
        for (var i = 0; i < 2000 && sim.Snapshot.Villagers.Single(v => v.Id == member.Id).Elevation < built.Lookout.Z; i++) sim.AdvanceTicks(1);
        Assert.Equal(built.Lookout.Z, sim.Snapshot.Villagers.Single(v => v.Id == member.Id).Elevation, 2);
        using var save = new MemoryStream(); sim.Save(save); save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.True(loaded.Snapshot.Villages[0].PillarScaffold!.Complete);
        Assert.Equal(built.Lookout, loaded.Snapshot.Villages[0].PillarScaffold!.Lookout);
    }
}
