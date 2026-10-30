using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

/// <summary>A Furniture Blueprint of three shelving pieces west of the Village centre; each built shelf adds capacity.</summary>
public sealed record Storehouse(IReadOnlyList<BlueprintSlot> Slots)
{
    /// <summary>Units of stores (Goods, Materials, unused Gear) one built shelf holds.</summary>
    public const int PerShelf = 12;

    [JsonIgnore] public bool Complete => Slots.Count == 3 && Slots.All(s => s.FurnitureId is not null);
    [JsonIgnore] public int Capacity => Slots.Count(s => s.FurnitureId is not null) * PerShelf;

    internal static readonly Vector2[] Offsets = [new(-3.8f, -1.2f), new(-3.8f, 0), new(-3.8f, 1.2f)];
    internal static BlueprintSlot Slot(Vector2 position) => new(position, MathF.PI / 2, 0.4f, WallRun.Straight, 0.8f, 1);
}

public sealed partial class ScenarioBuilder
{
    public Storehouse PlanStorehouse(int villageId) => _simulation.PlanStorehouse(villageId);
}

public sealed partial class Simulation
{
    internal Storehouse PlanStorehouse(int villageId) => Publish(() =>
    {
        var village = _villages.Single(v => v.Id == villageId);
        if (village.Storehouse is { } existing) return existing;
        var storehouse = new Storehouse(Array.AsReadOnly(Storehouse.Offsets.Select(o => Storehouse.Slot(_space.Wrap(village.Position + o))).ToArray()));
        _villages[villageId] = village with { Storehouse = storehouse };
        return storehouse;
    });

    private static bool StorehouseFits(BlueprintSlot slot, FurniturePiece p) =>
        p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && !FurnitureCatalog.Spec(p).IsFixed && !FurnitureCatalog.Spec(p).IsFloorCovering &&
        FurnitureCatalog.Spec(p).Height >= slot.MinimumHeight && FurnitureResources.Weight(p) >= slot.MinimumWeight &&
        p.Type is FurnitureType.ShelfUnit or FurnitureType.Bookcase or FurnitureType.PalletRack or FurnitureType.GroceryShelf or FurnitureType.Wardrobe;

    private void SetStorehouseSlot(int villageId, int index, BlueprintSlot slot)
    {
        var village = _villages[villageId];
        var house = village.Storehouse!;
        _villages[villageId] = village with { Storehouse = house with
            { Slots = Array.AsReadOnly(house.Slots.Select((s, i) => i == index ? slot : s).ToArray()) } };
    }

    private bool BuildStorehouse(Village village, Villager worker)
    {
        var pending = PlanStorehouse(village.Id);
        if (pending.Complete) return false;
        if (pending.Slots.Any(s => s.ReservedFurnitureId is not null)) return true;
        var index = pending.Slots.ToList().FindIndex(s => s.FurnitureId is null);
        var slot = pending.Slots[index];
        foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
            StorehouseFits(slot, p) && !IsBuiltFurniture(p.Id) && _space.Distance(village.Position, p.Position) <= 48 &&
            !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).OrderBy(p => InYard(village, p) ? 0 : 1)
            .ThenBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(piece.Id, [worker.Id], slot.Position, false, construction: true);
                SetStorehouseSlot(village.Id, index, slot with { ReservedFurnitureId = piece.Id });
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
        }
        return false;
    }

    private bool CompleteStorehouse(FurnitureTask task)
    {
        foreach (var village in _villages.ToArray())
            if (village.Storehouse is { } house)
                for (var i = 0; i < house.Slots.Count; i++)
                    if (house.Slots[i].ReservedFurnitureId == task.FurnitureId)
                    {
                        var slot = house.Slots[i];
                        MoveFurnitureCore(task.FurnitureId, slot.Position, slot.Rotation, 0);
                        SetStorehouseSlot(village.Id, i, slot with { FurnitureId = task.FurnitureId, ReservedFurnitureId = null });
                        foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                        return true;
                    }
        return false;
    }

    private void ClearStorehouseFurniture(int id, bool built)
    {
        foreach (var village in _villages.ToArray())
            if (village.Storehouse is { } house)
                for (var i = 0; i < house.Slots.Count; i++)
                    if (built ? house.Slots[i].FurnitureId == id : house.Slots[i].ReservedFurnitureId == id)
                        SetStorehouseSlot(village.Id, i, built ? house.Slots[i] with { FurnitureId = null } : house.Slots[i] with { ReservedFurnitureId = null });
    }

    private void ValidateStorehouses()
    {
        foreach (var village in _villages)
        {
            if (village.Storehouse is not { } house) continue;
            if (house.Slots.Count != 3) throw new InvalidDataException("Invalid world save: inconsistent Storehouse.");
            for (var i = 0; i < 3; i++)
            {
                var slot = house.Slots[i];
                if (slot with { FurnitureId = null, ReservedFurnitureId = null } != Storehouse.Slot(_space.Wrap(village.Position + Storehouse.Offsets[i])) ||
                    slot.FurnitureId is not null && slot.ReservedFurnitureId is not null ||
                    slot.FurnitureId is { } f && !HasPiece(f, p => StorehouseFits(slot, p) && p.Position == slot.Position && p.Rotation == slot.Rotation) ||
                    slot.ReservedFurnitureId is { } r && (!HasPiece(r, p => StorehouseFits(slot, p with { Elevation = 0 }) &&
                        (p.Elevation == 0 || p.Elevation == CarriedElevation && _furnitureTasks.Any(t => t.FurnitureId == r && t.Started))) ||
                        !_furnitureTasks.Any(t => t.FurnitureId == r && t.Work != FurnitureWork.Disassemble && t.VillagerIds.All(village.VillagerIds.Contains))))
                    throw new InvalidDataException("Invalid world save: inconsistent Storehouse Blueprint.");
            }
        }
    }
}
