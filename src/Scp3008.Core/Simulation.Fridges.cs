using System.Numerics;

namespace Scp3008.Core;

/// <summary>Power retained by a Fridge after it leaves its display; Home is the Village it last settled in.</summary>
public sealed record FridgePower(int FurnitureId, double BatteryHours = 0, bool GeneratorPowered = false, int? Home = null);

public sealed record FridgeBroughtHome(StoreClock At, int VillageId, int FurnitureId) : DomainEvent(At);

public sealed partial class Simulation
{
    private readonly Dictionary<int, FridgePower> _fridgePower = [];
    private IReadOnlyList<FurniturePiece>? _fridgeFurniture;
    private FurniturePiece[] _fridges = [];
    // A Fridge holds as many food units as one Storehouse shelf.
    private const int FridgeCapacity = Storehouse.PerShelf;

    private FurniturePiece[] Fridges()
    {
        if (!ReferenceEquals(_fridgeFurniture, _furniture.Pieces) && UntouchedSince(_fridgeFurniture, p => p.Type == FurnitureType.Fridge))
            _fridgeFurniture = _furniture.Pieces;
        if (!ReferenceEquals(_fridgeFurniture, _furniture.Pieces))
        {
            _fridgeFurniture = _furniture.Pieces;
            _fridges = _furniture.Pieces.Where(p => p.Type == FurnitureType.Fridge).ToArray();
            var present = _fridges.Select(p => p.Id).ToHashSet();
            foreach (var id in _fridgePower.Keys.Where(id => !present.Contains(id)).ToArray()) _fridgePower.Remove(id);
        }
        return _fridges;
    }

    private Village? FridgeHome(FurniturePiece fridge) => _villages
        .Where(v => _space.Distance(v.Position, fridge.Position) <= 8)
        .OrderBy(v => _space.Distance(v.Position, fridge.Position)).FirstOrDefault();

    // A Village's own Fridge, standing at home and not being worked on, counts as built furniture.
    private bool IsHomeFridge(int id) =>
        _fridgePower.TryGetValue(id, out var power) && power.Home is not null && !_furnitureTasks.Any(t => t.FurnitureId == id);

    private bool FridgeUsable(FurniturePiece fridge) => fridge.Damage < 1 && fridge.Elevation == 0 &&
        fridge.Tilt == 0 && fridge.Roll == 0 && !_furnitureTasks.Any(t => t.FurnitureId == fridge.Id) && !IsSlotFurniture(fridge.Id) &&
        !_snapshot.GroundAt(fridge.Position).HasFlag(GroundProperty.Darkness);

    private bool FridgePowered(FurniturePiece fridge) => FridgeUsable(fridge) &&
        (!_fridgePower.TryGetValue(fridge.Id, out var power) || power.GeneratorPowered || power.BatteryHours > 1e-9);

    // Power is only spent while there is food to keep cold.
    private bool FridgeHasLoad(FurniturePiece fridge, Village? home) =>
        _stock.Any(g => g.FurnitureId == fridge.Id && !g.LeftBehind && g.Amount > 0) ||
        home is not null && home.Stores.Any(g => g.Value > 0 && GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food);

    private void AdvanceFridges(StoreClock clock)
    {
        if (_fridgePower.Count == 0) return;
        var people = _villagers.Snapshot();
        foreach (var fridge in Fridges())
        {
            if (!_fridgePower.TryGetValue(fridge.Id, out var power)) continue;
            // A Fridge built into a Blueprint has no home; one on its way keeps the home it had until it is set down.
            var home = IsSlotFurniture(fridge.Id) ? null : _furnitureTasks.Any(t => t.FurnitureId == fridge.Id)
                ? _villages.FirstOrDefault(v => v.Id == power.Home) : FridgeHome(fridge);
            if (home is not null && home.Id != power.Home) Emit(new FridgeBroughtHome(clock, home.Id, fridge.Id));
            var usable = FridgeUsable(fridge);
            var generator = usable && home is not null && people.Any(v => v.VillageId == home.Id && v.Action == VillagerAction.Cranking);
            var hours = power.BatteryHours;
            if (usable && !generator && FridgeHasLoad(fridge, home))
            {
                if (hours <= 1e-9 && home is not null && home.Stores.GetValueOrDefault(GoodsKind.Batteries) > 0)
                {
                    ChangeStores(home.Id, GoodsKind.Batteries, -1);
                    hours = 4;
                }
                hours = Math.Max(0, hours - StoreClock.TickDuration.TotalHours);
            }
            _fridgePower[fridge.Id] = power with { BatteryHours = hours, GeneratorPowered = generator, Home = home?.Id };
        }
    }

    private bool GatherFridge(Village village, Villager[] workers)
    {
        if (workers.FirstOrDefault(w => w.Hand?.Kind is GearKind.ShoppingCart or GearKind.PlatformCart) is not { } worker ||
            Fridges().Any(p => p.Damage < 1 && _fridgePower.GetValueOrDefault(p.Id)?.Home == village.Id) ||
            _furnitureTasks.Any(t => _fridges.Any(p => p.Id == t.FurnitureId) && _space.Distance(t.Destination, village.Position) <= 8) ||
            village.Stores.GetValueOrDefault(GoodsKind.Batteries) <= 1 &&
                !_villagers.Snapshot().Any(v => v.VillageId == village.Id && v.Hand?.Kind == GearKind.HandCrankedGenerator)) return false;
        foreach (var fridge in _fridges.Where(p => p.Damage < 1 && p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 &&
            _space.Distance(village.Position, p.Position) <= 48 && FridgeHome(p) is null && !IsBuiltFurniture(p.Id) &&
            !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).OrderBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(fridge.Id, [worker.Id], _space.Wrap(village.Position + new Vector2(-2, 2)), false, construction: true);
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable Fridge. */ }
        }
        return false;
    }

    // Crank for the Fridge only when it holds food and no Battery is left to run it.
    private bool NeedsFridgeCranking(int villageId)
    {
        var village = _villages.FirstOrDefault(v => v.Id == villageId);
        if (village is null || village.Stores.GetValueOrDefault(GoodsKind.Batteries) > 0) return false;
        return Fridges().Any(p => _fridgePower.TryGetValue(p.Id, out var power) && power.Home == villageId &&
            power.BatteryHours <= 1e-9 && FridgeUsable(p) && FridgeHasLoad(p, village));
    }
}
