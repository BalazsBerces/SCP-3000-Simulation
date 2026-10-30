using System.Numerics;

namespace Scp3008.Core;

public enum FurnitureWork { Carry, Drag, TeamCarry, CartHaul, Disassemble }

/// <summary>A reserved piece and its workers, including approach, route and work progress.</summary>
public sealed record FurnitureTask(int FurnitureId, IReadOnlyList<int> VillagerIds, FurnitureWork Work,
    Vector2 Destination, IReadOnlyList<Vector2> Route, bool Started = false, double Progress = 0);

public sealed record FurnitureDisassembled(StoreClock At, int FurnitureId, int VillagerId, int VillageId) : DomainEvent(At);

public sealed record NoiseEmitted(StoreClock At, Vector2 Position, float Radius, int FurnitureId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public void DisassembleFurniture(int furnitureId, int villagerId) =>
        _simulation.StartFurnitureTask(furnitureId, [villagerId], Vector2.Zero, true);

    public Village PlaceVillage(Vector2 position, IReadOnlyList<int> villagerIds, Personality personality = Personality.Cautious) =>
        _simulation.PlaceVillage(position, villagerIds, personality);

    public void CarryFurniture(int furnitureId, IReadOnlyList<int> villagerIds, Vector2 destination) =>
        _simulation.StartFurnitureTask(furnitureId, villagerIds, destination, false);
}

internal sealed partial class Villagers
{
    public void PracticeBuilding(int id, bool usedTool)
    {
        var w = _walkers.Single(w => w.Id == id);
        w.Skills[Role.Builder] = w.Skills.GetValueOrDefault(Role.Builder) + 0.1;
        if (usedTool && w.Hand is { } tool) w.Hand = Wear(tool, 1);
    }

    public void WearFurnitureCart(int id)
    {
        var w = _walkers.Single(w => w.Id == id);
        if (w.Hand is { Kind: GearKind.ShoppingCart or GearKind.PlatformCart } cart) w.Hand = Wear(cart, 1);
    }

    public void ReserveFurniture(int id, int furnitureId, Vector2 position)
    {
        Send(id, position);
        _walkers.Single(w => w.Id == id).FurnitureTaskId = furnitureId;
    }

    public void FurniturePose(int id, Vector2? position, VillagerAction action)
    {
        var w = _walkers.Single(w => w.Id == id);
        if (position is { } p)
        {
            var delta = space.Delta(w.Position, p);
            if (delta.LengthSquared() > 0.0001f) w.Heading = MathF.Atan2(delta.Y, delta.X);
            w.Position = space.Wrap(p);
        }
        w.Waypoints.Clear();
        w.Destination = null;
        w.Target = null;
        w.Action = action;
        if (action == VillagerAction.Idle) w.FurnitureTaskId = null;
    }
}

public sealed partial class Simulation
{
    private const float CarriedElevation = 0.7f;
    private readonly List<FurnitureTask> _furnitureTasks = [];

    private void RestoreFurnitureTasks(IReadOnlyList<FurnitureTask> tasks, IReadOnlyList<Village> villages)
    {
        var workers = _villagers.Snapshot();
        if (tasks.Select(t => t.FurnitureId).Distinct().Count() != tasks.Count ||
            tasks.SelectMany(t => t.VillagerIds).Distinct().Count() != tasks.Sum(t => t.VillagerIds.Count) ||
            tasks.Any(t => !Enum.IsDefined(t.Work) || t.VillagerIds.Count is < 1 or > 4 ||
                (t.Work == FurnitureWork.Disassemble && (t.VillagerIds.Count != 1 ||
                    !workers.Any(v => v.Id == t.VillagerIds[0] && villages.Any(village => village.Id == v.VillageId)))) ||
                !double.IsFinite(t.Progress) || t.Progress < 0 || t.Progress >= 1 ||
                !float.IsFinite(t.Destination.X) || !float.IsFinite(t.Destination.Y) ||
                t.Route.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)) ||
                !_furniture.Pieces.Any(p => p.Id == t.FurnitureId && !FurnitureCatalog.Spec(p).IsFixed) ||
                t.VillagerIds.Any(id => !workers.Any(v => v.Id == id && v.FurnitureTaskId == t.FurnitureId))) ||
            workers.Any(v => v.FurnitureTaskId is { } id && !tasks.Any(t => t.FurnitureId == id && t.VillagerIds.Contains(v.Id))))
            throw new InvalidDataException("Invalid world save: inconsistent Furniture tasks.");
        _furnitureTasks.AddRange(tasks);
    }

    internal void StartFurnitureTask(int furnitureId, IReadOnlyList<int> ids, Vector2 destination, bool disassemble, bool construction = false) => Publish(() =>
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is < 1 or > 4 || ids.Distinct().Count() != ids.Count ||
            !float.IsFinite(destination.X) || !float.IsFinite(destination.Y))
            throw new ArgumentException("Furniture work needs one to four distinct Villagers and a finite destination.");
        var piece = _furniture.Pieces.Where(p => p.Id == furnitureId).Select(p => (FurniturePiece?)p).SingleOrDefault()
            ?? throw new ArgumentException("No such Furniture.");
        if (FurnitureCatalog.Spec(piece).IsFixed || IsSlotFurniture(furnitureId) || construction && IsHomeFridge(furnitureId) || _furnitureTasks.Any(t => t.FurnitureId == furnitureId))
            throw new InvalidOperationException("Furniture is fixed or already reserved.");
        var people = _villagers.Snapshot();
        var workers = ids.Select(id => people.SingleOrDefault(v => v.Id == id)
            ?? throw new ArgumentException("No such Villager.")).ToArray();
        if (workers.Any(v => v.Crafting || v.FurnitureTaskId is not null || v.CarriedAmount + v.CarriedMaterials > 0))
            throw new InvalidOperationException("A worker is already carrying a load or working on Furniture.");
        if (disassemble && (workers.Length != 1 || workers[0].VillageId is null || workers[0].Hand?.Kind is GearKind.ShoppingCart or GearKind.PlatformCart))
            throw new InvalidOperationException("Disassembly needs one Village member with hands available.");
        var weight = FurnitureResources.Weight(piece);
        var cart = workers.Length == 1 && workers[0].Hand?.Kind is GearKind.ShoppingCart or GearKind.PlatformCart;
        if (!disassemble && !cart && weight > 100)
            throw new InvalidOperationException("This piece needs a cart or disassembly.");
        var work = disassemble ? FurnitureWork.Disassemble : cart ? FurnitureWork.CartHaul :
            workers.Length > 1 && weight <= 25 * workers.Length ? FurnitureWork.TeamCarry :
            workers.Length == 1 && weight <= 25 ? FurnitureWork.Carry : FurnitureWork.Drag;
        if (!disassemble && workers.Any(v => v.Hand is not null) && !cart)
            throw new InvalidOperationException("Carrying Furniture needs free hands.");
        if (workers.Any(v => _navigation.FindPath(v.Position, _navigation.NearestWalkable(piece.Position)) is null))
            throw new InvalidOperationException("A worker cannot reach the Furniture.");
        List<Vector2> route = [];
        if (!disassemble)
        {
            _navigation.Update([piece], []);
            try
            {
                if (construction) destination = _navigation.NearestWalkable(destination);
                if (!_navigation.IsWalkable(destination)) throw new InvalidOperationException("Destination is blocked.");
                // shortcut: hauling uses pedestrian clearance; widen routes when narrow-aisle load clearance is added.
                route = _navigation.FindPath(piece.Position, _space.Wrap(destination))
                    ?? throw new InvalidOperationException("No carrying route.");
            }
            finally { _navigation.Update([], [piece]); }
        }
        _furnitureTasks.Add(new(furnitureId, Array.AsReadOnly(ids.ToArray()), work, _space.Wrap(destination), route.AsReadOnly()));
        foreach (var id in ids) _villagers.ReserveFurniture(id, furnitureId, piece.Position);
        return true;
    });

    private void AdvanceFurnitureTasks(StoreClock clock)
    {
        foreach (var task in _furnitureTasks.ToArray())
        {
            var found = _furniture.Pieces.Where(p => p.Id == task.FurnitureId).Select(p => (FurniturePiece?)p).SingleOrDefault();
            var piece = found.GetValueOrDefault();
            var people = _villagers.Snapshot();
            var workers = people.Where(v => task.VillagerIds.Contains(v.Id)).ToArray();
            if (found is null || workers.Length != task.VillagerIds.Count ||
                workers.Any(v => v.Needs.Hunger >= 0.9 || v.Needs.Thirst >= 0.9) ||
                (task.Work == FurnitureWork.CartHaul && workers[0].Hand?.Kind is not (GearKind.ShoppingCart or GearKind.PlatformCart)))
            {
                FinishFurnitureTask(task, workers);
                continue;
            }
            if (!task.Started && workers.Any(v => v.Destination is not null)) continue;
            if (!task.Started && workers.Any(v => _space.Distance(v.Position, piece.Position) >
                Math.Max(FurnitureCatalog.Spec(piece).Width, FurnitureCatalog.Spec(piece).Depth) + 3))
            {
                FinishFurnitureTask(task, workers);
                continue;
            }
            if (task.Work == FurnitureWork.Disassemble)
            {
                AdvanceDisassembly(task, piece, workers[0], clock);
                continue;
            }
            var route = task.Route.ToList();
            var position = piece.Position;
            var speed = Villager.WalkingSpeed * (task.Work == FurnitureWork.Drag ? 0.3f : task.Work == FurnitureWork.CartHaul ? 1.5f : 1);
            var left = speed * (float)StoreClock.TickDuration.TotalHours;
            while (left > 0 && route.Count > 0)
            {
                var delta = _space.Delta(position, route[0]);
                var distance = delta.Length();
                if (distance <= left) { position = route[0]; route.RemoveAt(0); left -= distance; }
                else { position = _space.Wrap(position + delta / distance * left); left = 0; }
            }
            MoveFurnitureCore(piece.Id, position, piece.Rotation,
                route.Count == 0 || task.Work == FurnitureWork.Drag ? 0 : CarriedElevation);
            var action = task.Work switch
            {
                FurnitureWork.Drag => VillagerAction.DraggingFurniture,
                FurnitureWork.TeamCarry => VillagerAction.TeamCarrying,
                FurnitureWork.CartHaul => VillagerAction.CartHauling,
                _ => VillagerAction.CarryingFurniture,
            };
            for (var i = 0; i < workers.Length; i++)
                _villagers.FurniturePose(workers[i].Id, position + new Vector2(
                    (i % 2 == 0 ? -1 : 1) * (FurnitureCatalog.Spec(piece).Width / 2 + 0.6f), i / 2 * 1.5f), action);
            if (task.Work == FurnitureWork.Drag && _tick % 20 == 0)
                Emit(new NoiseEmitted(clock, position, 30, piece.Id));
            if (route.Count == 0)
            {
                if (task.Work == FurnitureWork.CartHaul) _villagers.WearFurnitureCart(workers[0].Id);
                FinishFurnitureTask(task, workers, completed: true);
                CompleteBlueprintPiece(task, clock);
            }
            else _furnitureTasks[_furnitureTasks.IndexOf(task)] = task with { Started = true, Route = route.AsReadOnly() };
        }
    }

    internal Village PlaceVillage(Vector2 position, IReadOnlyList<int> ids, Personality personality) => Publish(() =>
    {
        ArgumentNullException.ThrowIfNull(ids);
        var people = _villagers.Snapshot();
        if (!Enum.IsDefined(personality) || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || ids.Count == 0 || ids.Distinct().Count() != ids.Count ||
            ids.Any(id => !people.Any(v => v.Id == id && v.VillageId is null)) ||
            _bands.Any(b => b.VillagerIds.Intersect(ids).Any()))
            throw new ArgumentException("A Village needs distinct, unclaimed Villagers and a finite position.");
        var village = new Village(_villages.Count, _space.Wrap(position), Array.AsReadOnly(ids.ToArray())) { Personality = personality };
        _villages.Add(village);
        _villagers.JoinVillage(village);
        return village;
    });

    private void AdvanceDisassembly(FurnitureTask task, FurniturePiece piece, Villager worker, StoreClock clock)
    {
        if (worker.VillageId is not { } villageId) { FinishFurnitureTask(task, [worker]); return; }
        _villagers.FurniturePose(worker.Id, null, VillagerAction.Disassembling);
        var equipped = FurnitureResources.HasTool(piece, worker.Hand);
        var skill = worker.Skills.GetValueOrDefault(Role.Builder);
        var duration = FurnitureResources.Tool(piece) == ToolLevel.BareHands ? 0.1 : 0.5;
        var hours = StoreClock.TickDuration.TotalHours;
        var progress = task.Progress + hours * (1 + skill) / (duration * (equipped ? 1 : 3));
        if (!equipped && _tick % 20 == 0) Emit(new NoiseEmitted(clock, piece.Position, 45, piece.Id));
        if (progress < 1)
        {
            _furnitureTasks[_furnitureTasks.IndexOf(task)] = task with { Started = true, Progress = progress };
            return;
        }
        var village = _villages[villageId];
        var materials = village.Materials.ToDictionary();
        foreach (var (kind, amount) in FurnitureResources.Yield(piece))
            materials[kind] = materials.GetValueOrDefault(kind) + Math.Max(1, (int)Math.Floor(amount * (equipped ? 1 : 0.5) * (1 + skill * 0.25)));
        _villages[villageId] = village with { Materials = new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(materials) };
        _villagers.PracticeBuilding(worker.Id, equipped);
        RemoveFurniture(p => p.Id == piece.Id);
        Emit(new FurnitureDisassembled(clock, piece.Id, worker.Id, villageId));
        FinishFurnitureTask(task, [worker]);
    }

    private void CancelFurnitureTasks(Func<FurnitureTask, bool> match)
    {
        var people = _villagers.Snapshot();
        foreach (var task in _furnitureTasks.Where(match).ToArray())
            FinishFurnitureTask(task, people.Where(v => task.VillagerIds.Contains(v.Id)).ToArray());
    }

    private void FinishFurnitureTask(FurnitureTask task, Villager[] workers, bool completed = false)
    {
        if (task.Started && task.Work != FurnitureWork.Disassemble &&
            _furniture.Pieces.Where(p => p.Id == task.FurnitureId).Select(p => (FurniturePiece?)p).SingleOrDefault() is { } piece && piece.Elevation != 0)
            MoveFurnitureCore(piece.Id, piece.Position, piece.Rotation, 0);
        _furnitureTasks.Remove(task);
        if (!completed) ReleaseBlueprintReservation(task.FurnitureId);
        foreach (var worker in workers) _villagers.FurniturePose(worker.Id, _navigation.NearestWalkable(worker.Position), VillagerAction.Idle);
    }
}
