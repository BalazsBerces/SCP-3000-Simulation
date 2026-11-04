using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class HaulingTests
{
    private static Simulation Sandbox() => Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
        RuntimeRngSeed: 17, Restock: new RestockConfig { FurnitureHours = 0 }));

    private static Village Place(Simulation sim, float x)
    {
        var people = Enumerable.Range(0, 3).Select(i => sim.Scenario.PlaceVillager(new(x - 1 + i, 104))).ToArray();
        return sim.Scenario.PlaceVillage(new(x, 104), people.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void With_a_winch_fewer_Villagers_lift_a_heavy_piece_onto_a_tower()
    {
        using var sim = Sandbox();
        var plain = Place(sim, 40);
        var rigged = Place(sim, 100);
        sim.Scenario.GiveVillageGear(rigged.Id, GearKind.Winch);
        var pulleyOnly = Place(sim, 160);
        sim.Scenario.GiveVillageGear(pulleyOnly.Id, GearKind.Pulley);
        var wardrobe = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(60, 110));
        var without = sim.LiftersNeeded(plain.Id, wardrobe.Id);
        var pulley = sim.LiftersNeeded(pulleyOnly.Id, wardrobe.Id);
        var winch = sim.LiftersNeeded(rigged.Id, wardrobe.Id);
        Assert.True(winch < pulley && pulley < without, $"{without} {pulley} {winch}");
        Assert.Contains(sim.Snapshot.Villages[rigged.Id].Gear, g => g.Kind == GearKind.Winch);
    }

    [Fact]
    public void A_winch_lets_two_Villagers_carry_what_two_could_not_without_one()
    {
        using var sim = Sandbox();
        var plain = Place(sim, 40);
        var rigged = Place(sim, 100);
        sim.Scenario.GiveVillageGear(rigged.Id, GearKind.Winch);
        var wardrobe = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(100, 110));
        var other = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new(40, 110));
        Assert.Throws<InvalidOperationException>(() => sim.Scenario.CarryFurniture(other.Id, rigged.VillagerIds.Take(0).Concat(plain.VillagerIds.Take(2)).ToArray(), new(40, 120)));
        sim.Scenario.CarryFurniture(wardrobe.Id, rigged.VillagerIds.Take(2).ToArray(), new(100, 120));
    }

    [Fact]
    public void A_cart_train_carries_more_moves_slower_and_is_louder_than_a_single_cart()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.SetStores(a.Id, GoodsKind.TinnedFood, 60);
        var load = new Dictionary<GoodsKind, int> { [GoodsKind.TinnedFood] = 25 };
        Assert.Throws<ArgumentException>(() => sim.Scenario.SendCaravan(a.Id, b.Id, load));
        var events = new List<DomainEvent>();
        using var sub = sim.Subscribe(events.Add);
        var train = sim.Scenario.SendCaravan(a.Id, b.Id, load, carts: 3);
        Assert.Equal(25, sim.Snapshot.Caravans.Single().Cargo.Goods[GoodsKind.TinnedFood]);
        var single = new Caravan(0, a.Id, b.Id, [], new CaravanCargo(), [], 0, false, default);
        Assert.True(train.LoadLimit > single.LoadLimit && train.Pace < single.Pace && train.NoiseRadius > single.NoiseRadius);
        var start = sim.Snapshot.Caravans.Single().Position;
        sim.AdvanceTicks(300);
        Assert.Contains(events, e => e is NoiseEmitted { FurnitureId: -1 });
        Assert.True(sim.Snapshot.Caravans.Single().Position != start);
    }

    [Fact]
    public void A_cart_train_and_a_winch_survive_a_save_and_load()
    {
        using var sim = Sandbox();
        var a = Place(sim, 40);
        var b = Place(sim, 100);
        sim.Scenario.GiveVillageGear(a.Id, GearKind.Winch);
        sim.Scenario.SendCaravan(a.Id, b.Id, carts: 2);
        using var stream = new MemoryStream();
        sim.Save(stream);
        stream.Position = 0;
        using var loaded = Simulation.Load(stream);
        Assert.Equal(2, loaded.Snapshot.Caravans.Single().Carts);
        Assert.Contains(loaded.Snapshot.Villages[a.Id].Gear, g => g.Kind == GearKind.Winch);
    }
}
