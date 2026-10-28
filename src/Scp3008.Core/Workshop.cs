using System.Numerics;

namespace Scp3008.Core;

public enum WorkshopKind { Workbench, SewingTable, ElectricsBench }

/// <summary>A one-slot Furniture Blueprint for a Village's working surface.</summary>
public sealed record Workshop(WorkshopKind Kind, BlueprintSlot Slot)
{
    public bool Complete => Slot.FurnitureId is not null;
}

public sealed partial class ScenarioBuilder
{
    public Workshop PlanWorkshop(int villageId, WorkshopKind kind) => _simulation.PlanWorkshop(villageId, kind);
}

public sealed partial class Simulation
{
    internal Workshop PlanWorkshop(int villageId, WorkshopKind kind) => Publish(() =>
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var village = _villages.Single(v => v.Id == villageId);
        var existing = village.Workshops.FirstOrDefault(w => w.Kind == kind);
        if (existing is not null) return existing;
        var position = _space.Wrap(village.Position + new Vector2(-3 + (int)kind * 3, 3));
        var workshop = new Workshop(kind, new BlueprintSlot(position, 0, 0.8f, WallRun.Straight, 0.6f, 10));
        _villages[villageId] = village with { Workshops = Array.AsReadOnly(village.Workshops.Append(workshop).ToArray()) };
        return workshop;
    });

    private void SetWorkshopSlot(int villageId, WorkshopKind kind, BlueprintSlot slot)
    {
        var village = _villages[villageId];
        _villages[villageId] = village with { Workshops = Array.AsReadOnly(village.Workshops.Select(w =>
            w.Kind == kind ? w with { Slot = slot } : w).ToArray()) };
    }

    private bool BuildWorkshop(Village village, Villager worker)
    {
        // Working surfaces are useful before a Village knows any middle Recipes.
        foreach (var kind in Enum.GetValues<WorkshopKind>())
            if (!village.Workshops.Any(w => w.Kind == kind)) PlanWorkshop(village.Id, kind);
        var pending = _villages[village.Id].Workshops.FirstOrDefault(w => !w.Complete);
        if (pending is null) return false;
        if (pending.Slot.ReservedFurnitureId is not null) return true;
        foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
            WorkshopFits(pending.Slot, p) && !IsBuiltFurniture(p.Id) &&
            !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).OrderBy(p => InYard(village, p) ? 0 : 1).ThenBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(piece.Id, [worker.Id], pending.Slot.Position, false, construction: true);
                SetWorkshopSlot(village.Id, pending.Kind, pending.Slot with { ReservedFurnitureId = piece.Id });
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable working surface. */ }
        }
        return false;
    }

    private static bool WorkshopFits(BlueprintSlot slot, FurniturePiece piece) => slot.Fits(piece) &&
        piece.Type is FurnitureType.DiningTable or FurnitureType.Desk or FurnitureType.GardenTable &&
        piece.Elevation == 0 && piece.Tilt == 0 && piece.Roll == 0;

    private bool CompleteWorkshop(FurnitureTask task)
    {
        foreach (var village in _villages.ToArray())
            foreach (var workshop in village.Workshops)
                if (workshop.Slot.ReservedFurnitureId == task.FurnitureId)
                {
                    MoveFurnitureCore(task.FurnitureId, workshop.Slot.Position, workshop.Slot.Rotation, 0);
                    SetWorkshopSlot(village.Id, workshop.Kind, workshop.Slot with { FurnitureId = task.FurnitureId, ReservedFurnitureId = null });
                    foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                    return true;
                }
        return false;
    }

    private void ClearWorkshopFurniture(int id, bool built)
    {
        foreach (var village in _villages.ToArray())
            foreach (var workshop in village.Workshops)
                if (built ? workshop.Slot.FurnitureId == id : workshop.Slot.ReservedFurnitureId == id)
                    SetWorkshopSlot(village.Id, workshop.Kind, built ? workshop.Slot with { FurnitureId = null } :
                        workshop.Slot with { ReservedFurnitureId = null });
    }
}
