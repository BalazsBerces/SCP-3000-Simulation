using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

internal sealed record Slots(IReadOnlyList<BlueprintSlot> All)
{
    public bool Any(Func<BlueprintSlot, bool> f) => All.Any(f);
    public int Index(Func<BlueprintSlot, bool> f) => All.ToList().FindIndex(s => f(s));
}

public class WallTierTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    // A fed Village (its Furniture yard inside, so no yard barricade) with carts, its first barricade built of bookcases.
    private static Village BuiltBarricade(Simulation sim, Vector2 at)
    {
        var members = new[] { sim.Scenario.PlaceVillager(at), sim.Scenario.PlaceVillager(at + new Vector2(1, 0)) };
        foreach (var m in members) sim.Scenario.EquipGear(m.Id, GearKind.ShoppingCart);
        var village = sim.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
        sim.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 400);
        sim.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 400);
        var plan = sim.Scenario.PlanBarricade(village.Id);
        for (var i = 0; i < plan.Slots.Count + 3; i++)
            sim.Scenario.PlaceFurniture(FurnitureType.Bookcase, at + new Vector2(-22, -24) + new Vector2(i % 12 * 2.2f, i / 12 * 2.5f), tint: i % 12);
        Run(sim, () => sim.Snapshot.Villages[village.Id].Walls[0].Complete, 60000);
        return sim.Snapshot.Villages[village.Id];
    }

    private static void Run(Simulation sim, Func<bool> done, int ticks)
    {
        for (var i = 0; i < ticks && !done(); i++) sim.AdvanceTicks(1);
        Assert.True(done(), "Did not finish in time: " + string.Join("; ", sim.Snapshot.Villages.Select(v =>
            $"tiers {string.Join(",", v.Walls.SelectMany(w => w.Slots).Select(s => s.Tier))} metal {v.Materials.GetValueOrDefault(MaterialKind.Metal)}")));
    }

    [Fact]
    public void A_wall_reaches_a_Tier_once_all_its_pieces_have_been_swapped_Strapped_or_Backfilled()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        var wall = village.Walls[0];
        Assert.Equal(1, wall.Tier);
        Assert.All(wall.Slots, s => Assert.False(s.Strapped || s.Backfilled));

        // Strap every piece but one, and the wall stays at Tier 1 (the one weak spot).
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Metal, 2 * (wall.Slots.Count - 1));
        var upgrades = new List<WallPieceUpgraded>();
        using var sub = sim.Subscribe(e => { if (e is WallPieceUpgraded u) upgrades.Add(u); });
        Run(sim, () => upgrades.Count == wall.Slots.Count - 1, 40000);
        var patchy = sim.Snapshot.Villages[village.Id].Walls[0];
        Assert.Equal(wall.Slots.Count - 1, patchy.Slots.Count(s => s.Strapped && s.Tier == 2));
        Assert.Equal(1, patchy.Tier);

        // The last piece is Backfilled with foam, and the wall steps up one Tier.
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Foam, 3);
        Run(sim, () => sim.Snapshot.Villages[village.Id].Walls[0].Tier == 2, 40000);
        Assert.Contains(sim.Snapshot.Villages[village.Id].Walls[0].Slots, s => s.Backfilled && !s.Strapped);
    }

    [Fact]
    public void A_piece_swapped_for_sturdier_Furniture_gains_a_Tier()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        var old = village.Walls[0].Slots[0].FurnitureId!.Value;
        // A wardrobe (Tier 3) loose in the Store replaces a bookcase (Tier 1).
        var sturdy = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(10, 120));
        var swapped = new List<WallPieceUpgraded>();
        using var sub = sim.Subscribe(e => { if (e is WallPieceUpgraded { How: WallUpgrade.Swapped } u) swapped.Add(u); });
        Run(sim, () => swapped.Count == 1, 40000);
        var slot = sim.Snapshot.Villages[village.Id].Walls[0].Slots[swapped[0].Slot];
        Assert.Equal(sturdy.Id, slot.FurnitureId);
        Assert.Equal(3, slot.Tier);
        Assert.NotEqual(old, slot.FurnitureId);
        Assert.Equal(1, sim.Snapshot.Villages[village.Id].Walls[0].Tier);
    }

    [Fact]
    public void A_Strapped_piece_takes_more_blows_and_a_Backfilled_one_absorbs_them_and_muffles_noise()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        int Blows(int slot)
        {
            var blows = 1;
            while (!sim.Scenario.StrikeWall(village.Id, 0, slot).Broken) blows++;
            return blows;
        }
        Slots Walls() => new(sim.Snapshot.Villages[village.Id].Walls[0].Slots);

        var last = village.Walls[0].Slots.Count - 1;
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Metal, 2);
        Run(sim, () => Walls().Any(s => s.Strapped), 40000);
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Foam, 3);
        Run(sim, () => Walls().Any(s => s.Backfilled), 40000);

        var bare = sim.Scenario.StrikeWall(village.Id, 0, last);
        Assert.Equal(Simulation.BlowNoise, bare.NoiseRadius);
        var plain = 1 + Blows(last);
        Assert.True(Blows(Walls().Index(s => s.Strapped)) > plain);
        var blow = sim.Scenario.StrikeWall(village.Id, 0, Walls().Index(s => s.Backfilled));
        Assert.True(blow.Damage < Simulation.BlowDamage);
        Assert.True(blow.NoiseRadius < bare.NoiseRadius);
    }

    [Fact]
    public void Damaged_pieces_are_repaired_while_the_Store_is_open_and_the_state_survives_a_save()
    {
        using var sim = Sandbox();
        var village = BuiltBarricade(sim, new(30, 104));
        sim.AdvanceTo(TimeSpan.FromHours(10));
        sim.Scenario.SetMaterials(village.Id, MaterialKind.Metal, 2);
        var piece = village.Walls[0].Slots[0].FurnitureId!.Value;
        sim.Scenario.StrikeWall(village.Id, 0, 0);
        sim.Scenario.StrikeWall(village.Id, 0, 0);
        Assert.Equal(0.5f, sim.Snapshot.Furniture.First(p => p.Id == piece).Damage);

        var repaired = new List<WallPieceRepaired>();
        using var sub = sim.Subscribe(e => { if (e is WallPieceRepaired r) repaired.Add(r); });
        Run(sim, () => sim.Snapshot.Furniture.First(p => p.Id == piece).Damage == 0f, 40000);
        Assert.NotEmpty(repaired);

        Run(sim, () => sim.Snapshot.Villages[village.Id].Walls[0].Slots.Any(s => s.Strapped), 40000);
        sim.Scenario.StrikeWall(village.Id, 0, 3);
        using var save = new MemoryStream();
        sim.Save(save);
        save.Position = 0;
        using var loaded = Simulation.Load(save);
        Assert.Equal(sim.Snapshot.Villages[village.Id].Walls[0].Slots, loaded.Snapshot.Villages[village.Id].Walls[0].Slots);
        Assert.Equal(sim.Snapshot.Furniture.Select(p => p.Damage), loaded.Snapshot.Furniture.Select(p => p.Damage));
    }
}
