using System.Numerics;

namespace Scp3008.Core;

public enum GearKind { HexKey, Screwdriver, KitchenKnife, Scissors, CanvasBag, ShoppingCart, Flashlight, QuiltedArmour, Crowbar, TableLegClub, Candle, CardboardArmour, Bandage }
public enum GearSlot { Hand, Bag, Armour }

/// <summary>Found or carried Gear. Unclaimed Gear stays at its source Furniture until taken.</summary>
public sealed record Gear(int Id, GearKind Kind, Vector2 Position)
{
    public double Durability { get; init; } = 100;
    public double BatteryHours { get; init; }
    public int FurnitureId { get; init; } = -1;
    public GearSlot Slot => Kind == GearKind.CanvasBag ? GearSlot.Bag : Kind is GearKind.QuiltedArmour or GearKind.CardboardArmour ? GearSlot.Armour : GearSlot.Hand;

    internal void Validate()
    {
        if (Id < 0 || !Enum.IsDefined(Kind) || !float.IsFinite(Position.X) || !float.IsFinite(Position.Y) ||
            !double.IsFinite(Durability) || Durability <= 0 || Durability > 100 ||
            !double.IsFinite(BatteryHours) || BatteryHours < 0 || BatteryHours > 4)
            throw new ArgumentException("Invalid Gear state.");
    }
}

public sealed partial class ScenarioBuilder
{
    public Gear PlaceGear(GearKind kind, Vector2 position)
    {
        new Gear(0, kind, position).Validate();
        return _simulation.PlaceGear(kind, position);
    }

    /// <summary>Gives a scenario Villager Gear in its matching slot; an occupied slot rejects it.</summary>
    public void EquipGear(int villagerId, GearKind kind, double durability = 100) =>
        _simulation.EquipGear(villagerId, kind, durability);

    /// <summary>Sets an abstract Goods/Material load, bounded by the Villager's capacity.</summary>
    public void SetCarriedLoad(int villagerId, GoodsKind kind, int goods, int materials = 0) =>
        _simulation.SetCarriedLoad(villagerId, kind, goods, materials);
}

internal sealed partial class Villagers
{
    private bool SeekGear(Walker w, WorldSnapshot world)
    {
        if (w.CarriedAmount > 0 || w.CarriedMaterials > 0) return false;
        var homeGear = world.Villages.FirstOrDefault(v => v.Id == w.VillageId)?.Gear ?? [];
        var chosen = world.Gear.Concat(homeGear).Where(g => CanEquip(w, g) &&
                space.Distance(w.Position, g.Position) <= 12 * (1 - world.FogDensityAt(w.Position) * 0.8f) &&
                !w.Unreachable.Contains(g.Position))
            .OrderBy(g => space.Distance(w.Position, g.Position))
            .Select(g => new NeedTarget(g.Position, Gear: g)).FirstOrDefault(t => TryPlan(w, t));
        if (chosen is null) return false;
        w.Target = chosen;
        w.Action = VillagerAction.Scavenging;
        return true;
    }

    public void AdvanceGear(double hours, StoreClock clock, Func<int?, Vector2, bool> reload)
    {
        foreach (var w in _walkers)
        {
            if (w.Hand is not { Kind: GearKind.Flashlight } light || clock.Phase != DayPhase.ClosingTime) continue;
            if (light.BatteryHours <= 1e-9)
            {
                if (w.CarriedGoods == GoodsKind.Batteries && w.CarriedAmount > 0)
                {
                    w.CarriedAmount--;
                    if (w.CarriedAmount == 0) w.CarriedGoods = null;
                }
                else if (!reload(w.VillageId, w.Position)) continue;
                light = light with { BatteryHours = 4 };
            }
            w.Hand = Wear(light with { BatteryHours = Math.Max(0, light.BatteryHours - hours) }, hours);
        }
    }

    private static int Capacity(Walker w) => w.Hand?.Kind == GearKind.ShoppingCart ? 32 : w.Bag is not null ? 8 : 2;
    private static Gear? Wear(Gear gear, double amount) => gear.Durability <= amount + 1e-9 ? null : gear with { Durability = gear.Durability - amount };

    public void Equip(int id, Gear gear)
    {
        gear.Validate();
        var w = _walkers.Single(w => w.Id == id);
        if (!CanEquip(w, gear)) throw new InvalidOperationException("Gear slot is occupied.");
        if (gear.Slot == GearSlot.Bag) w.Bag = gear;
        else if (gear.Slot == GearSlot.Armour) w.Armour = gear;
        else w.Hand = gear;
    }

    private static bool CanEquip(Walker w, Gear gear) => gear.Slot switch
    {
        GearSlot.Bag => w.Bag is null, GearSlot.Armour => w.Armour is null, _ => w.Hand is null,
    };

    public void SetLoad(int id, GoodsKind kind, int goods, int materials)
    {
        var w = _walkers.Single(w => w.Id == id);
        if (!Enum.IsDefined(kind) || goods < 0 || materials < 0 || (long)goods + materials > Capacity(w))
            throw new ArgumentOutOfRangeException(nameof(goods));
        w.CarriedGoods = goods > 0 ? kind : null;
        w.CarriedAmount = goods;
        w.CarriedMaterials = materials;
    }
}

public sealed partial class Simulation
{
    private bool TakeGear(int id, Gear gear)
    {
        var found = _gear.FirstOrDefault(g => g.Id == gear.Id);
        if (found is null)
        {
            var worker = _villagers.Snapshot().Single(v => v.Id == id);
            var village = _villages.FirstOrDefault(v => v.Id == worker.VillageId && v.Gear.Any(g => g.Id == gear.Id));
            if (village is null) return false;
            found = village.Gear.Single(g => g.Id == gear.Id);
            _villagers.Equip(id, found);
            _villages[village.Id] = village with { Gear = Array.AsReadOnly(village.Gear.Where(g => g.Id != found.Id).ToArray()) };
            return true;
        }
        _villagers.Equip(id, found);
        _gear.Remove(found);
        if (found.Kind == GearKind.ShoppingCart && found.FurnitureId >= 0)
        {
            // The graveyard's visible cart becomes carried Gear, rather than leaving a second cart behind.
            var piece = _furniture.Pieces.Single(p => p.Id == found.FurnitureId);
            RemoveFurniture(p => p.Id == found.FurnitureId);
            _missingFurniture.RemoveAll(m => m.Piece.Id == piece.Id);
            var rate = (_config.Restock ?? new RestockConfig()).GoodsPerStoreHour;
            if (rate > 0) _missingFurniture.Add(new MissingFurniture(piece, [], _restockHours + 1 / rate));
            _nextFurnitureCheck = _missingFurniture.Count == 0 ? double.PositiveInfinity : _missingFurniture.Min(m => m.ReadyHour);
        }
        return true;
    }

    private bool ReloadFlashlight(int? villageId, Vector2 position)
    {
        var home = _villages.FirstOrDefault(village => village.Id == villageId);
        if (home is null || _space.Distance(position, home.Position) > 2 || home.Stores.GetValueOrDefault(GoodsKind.Batteries) == 0)
            return false;
        ChangeStores(home.Id, GoodsKind.Batteries, -1);
        return true;
    }

    private int _nextGearId;
    private int NextGearId() => _nextGearId++;

    internal void EquipGear(int id, GearKind kind, double durability) => Publish(() =>
    {
        _villagers.Equip(id, new Gear(NextGearId(), kind, Vector2.Zero) { Durability = durability });
        return true;
    });

    internal void SetCarriedLoad(int id, GoodsKind kind, int goods, int materials) => Publish(() =>
    {
        _villagers.SetLoad(id, kind, goods, materials);
        return true;
    });
}
