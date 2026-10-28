using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

/// <summary>A Furniture Blueprint beside the Village centre: table, two seats and cooking surface.</summary>
public sealed record MessHall(Vector2 Position, IReadOnlyList<BlueprintSlot> Slots)
{
    [JsonIgnore] public bool Complete => Slots.Count == 4 && Slots.All(s => s.FurnitureId is not null);
    public long LastMealTick { get; init; } = -1;

    /// <summary>The hours each daily shared meal starts gathering.</summary>
    public static IReadOnlyList<int> MealHours { get; } = [9, 14, 20];

    // Table, a seat at each end and the cooking surface south of it, all clear of the first barricade ring (radius 5 m and more).
    internal static readonly Vector2 CentreOffset = new(0, -1.8f);
    internal static readonly (Vector2 Offset, float Rotation)[] Layout =
        [(Vector2.Zero, 0), (new(-1.7f, 0), MathF.PI / 2), (new(1.7f, 0), -MathF.PI / 2), (new(0, -1.5f), 0)];
    internal static BlueprintSlot Slot(int index, Vector2 position) =>
        new(position, Layout[index].Rotation, 0.4f, WallRun.Straight, 0.4f, 1);
}

public sealed partial class Simulation
{
    private bool BuildMessHall(Village village, Villager worker)
    {
        if (village.MessHall is null)
        {
            var centre = _space.Wrap(village.Position + MessHall.CentreOffset);
            var hall = new MessHall(centre, Array.AsReadOnly(MessHall.Layout.Select((l, i) =>
                MessHall.Slot(i, _space.Wrap(centre + l.Offset))).ToArray()));
            _villages[village.Id] = village = village with { MessHall = hall };
        }
        var pending = village.MessHall;
        if (pending.Complete) return false;
        if (pending.Slots.Any(s => s.ReservedFurnitureId is not null)) return true;
        var index = pending.Slots.ToList().FindIndex(s => s.FurnitureId is null);
        var slot = pending.Slots[index];
        foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
            MessHallFits(slot, index, p) && !IsBuiltFurniture(p.Id) && _space.Distance(village.Position, p.Position) <= 48 &&
            !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).OrderBy(p => InYard(village, p) ? 0 : 1).ThenBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(piece.Id, [worker.Id], slot.Position, false, construction: true);
                SetMessHallSlot(village.Id, index, slot with { ReservedFurnitureId = piece.Id });
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
        }
        return false;
    }

    // Blueprint.Fits is the barricade's size rule; tables and counters are wider, so only height and weight apply here.
    private static bool MessHallFits(BlueprintSlot slot, int index, FurniturePiece p) =>
        p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && !FurnitureCatalog.Spec(p).IsFixed &&
        FurnitureCatalog.Spec(p).Height >= slot.MinimumHeight && FurnitureResources.Weight(p) >= slot.MinimumWeight && (index switch
        {
            0 => p.Type is FurnitureType.DiningTable or FurnitureType.GardenTable or FurnitureType.MeetingTable,
            1 or 2 => p.Type is FurnitureType.Chair or FurnitureType.GardenChair or FurnitureType.HallBench or FurnitureType.BarStool,
            _ => p.Type is FurnitureType.KitchenCabinet or FurnitureType.Counter or FurnitureType.Stove or FurnitureType.Grill,
        });

    private void SetMessHallSlot(int villageId, int index, BlueprintSlot slot)
    {
        var village = _villages[villageId];
        var hall = village.MessHall!;
        _villages[villageId] = village with { MessHall = hall with
            { Slots = Array.AsReadOnly(hall.Slots.Select((s, i) => i == index ? slot : s).ToArray()) } };
    }

    private bool CompleteMessHall(FurnitureTask task)
    {
        foreach (var village in _villages.ToArray())
            if (village.MessHall is { } hall)
                for (var i = 0; i < hall.Slots.Count; i++)
                    if (hall.Slots[i].ReservedFurnitureId == task.FurnitureId)
                    {
                        var slot = hall.Slots[i];
                        MoveFurnitureCore(task.FurnitureId, slot.Position, slot.Rotation, 0);
                        SetMessHallSlot(village.Id, i, slot with { FurnitureId = task.FurnitureId, ReservedFurnitureId = null });
                        foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                        return true;
                    }
        return false;
    }

    private void ClearMessHallFurniture(int id, bool built)
    {
        foreach (var village in _villages.ToArray())
            if (village.MessHall is { } hall)
                for (var i = 0; i < hall.Slots.Count; i++)
                    if (built ? hall.Slots[i].FurnitureId == id : hall.Slots[i].ReservedFurnitureId == id)
                        SetMessHallSlot(village.Id, i, built ? hall.Slots[i] with { FurnitureId = null } : hall.Slots[i] with { ReservedFurnitureId = null });
    }

    private void ValidateMessHalls()
    {
        foreach (var village in _villages)
        {
            if (village.MessHall is not { } hall) continue;
            if (!float.IsFinite(hall.Position.X) || !float.IsFinite(hall.Position.Y) ||
                hall.Position != _space.Wrap(village.Position + MessHall.CentreOffset) ||
                hall.Slots.Count != 4 || hall.LastMealTick < -1 || hall.LastMealTick > _tick ||
                hall.LastMealTick >= 0 && MealStart(new StoreClock(hall.LastMealTick)) != hall.LastMealTick)
                throw new InvalidDataException("Invalid world save: inconsistent Mess hall.");
            for (var i = 0; i < hall.Slots.Count; i++)
            {
                var slot = hall.Slots[i];
                if (slot with { FurnitureId = null, ReservedFurnitureId = null } != MessHall.Slot(i, _space.Wrap(hall.Position + MessHall.Layout[i].Offset)) ||
                    slot.FurnitureId is not null && slot.ReservedFurnitureId is not null ||
                    slot.FurnitureId is { } f && !_furniture.Pieces.Any(p => p.Id == f && MessHallFits(slot, i, p) && p.Position == slot.Position && p.Rotation == slot.Rotation) ||
                    slot.ReservedFurnitureId is { } r && (!_furniture.Pieces.Any(p => p.Id == r && MessHallFits(slot, i, p with { Elevation = 0 }) &&
                        (p.Elevation == 0 || p.Elevation == CarriedElevation && _furnitureTasks.Any(t => t.FurnitureId == r && t.Started))) ||
                        !_furnitureTasks.Any(t => t.FurnitureId == r && t.Work != FurnitureWork.Disassemble && t.VillagerIds.All(village.VillagerIds.Contains))))
                    throw new InvalidDataException("Invalid world save: inconsistent Mess hall Blueprint.");
            }
        }
    }
}
