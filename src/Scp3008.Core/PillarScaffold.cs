using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

/// <summary>A Blueprint of eight open rack stages climbing a Pillar; abandoned stages can be reused.</summary>
public sealed record PillarScaffold(int PillarId, IReadOnlyList<BlueprintSlot> Slots)
{
    public const int Stages = 8, MetalPerStage = 8;
    [JsonIgnore] public bool Complete => Slots.All(s => s.FurnitureId is not null);
    [JsonIgnore] public Vector3 Lookout => new(Slots[^1].Position + new Vector2(0, 0.55f), Slots[^1].Elevation + 4.6f);
    [JsonIgnore] public int MaterialsNeeded => Slots.Count(s => s.FurnitureId is null) * MetalPerStage;
}

public sealed record ScaffoldPiecePlaced(StoreClock At, int VillageId, int FurnitureId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public PillarScaffold PlanPillarScaffold(int villageId, int pillarId) => _simulation.PlanPillarScaffold(villageId, pillarId);
}

public sealed partial class Simulation
{
    internal PillarScaffold PlanPillarScaffold(int villageId, int pillarId) => Publish(() => PlanScaffold(villageId, pillarId));

    private PillarScaffold PlanScaffold(int villageId, int pillarId)
    {
        var village = _villages.Single(v => v.Id == villageId);
        var pillar = _pillars.Single(p => p.Id == pillarId);
        if (village.PillarScaffold is { } existing) return existing;
        if (_space.Distance(village.Position, pillar.Position) > 80 || _villages.Any(v => v.PillarScaffold?.PillarId == pillarId))
            throw new InvalidOperationException("A Pillar scaffold needs a nearby, unclaimed Pillar.");
        var position = _space.Wrap(pillar.Position + new Vector2(0, pillar.Width / 2 + 0.55f));
        var slots = Enumerable.Range(0, PillarScaffold.Stages).Select(i =>
        {
            var elevation = i * 4.6f;
            var old = _furniture.Pieces.Where(p => p.Type == FurnitureType.PalletRack && p.Damage == 0 &&
                !IsBuiltFurniture(p.Id) && p.Position == position && p.Elevation == elevation && p.Rotation == 0 && p.Tilt == 0)
                .Cast<FurniturePiece?>().FirstOrDefault();
            return new BlueprintSlot(position, 0, 2.7f, WallRun.Straight, Elevation: elevation, FurnitureId: old?.Id);
        }).ToArray();
        var plan = new PillarScaffold(pillarId, Array.AsReadOnly(slots));
        _villages[villageId] = village with { PillarScaffold = plan };
        return plan;
    }

    private bool BuildPillarScaffold(Village village, Villager worker, StoreClock clock)
    {
        var plan = village.PillarScaffold;
        if (plan is null || plan.Complete) return false;
        var index = plan.Slots.ToList().FindIndex(s => s.FurnitureId is null);
        var slot = plan.Slots[index];
        if (village.Materials.GetValueOrDefault(MaterialKind.Metal) < PillarScaffold.MetalPerStage) return false;
        if (_space.Distance(worker.Position, slot.Position) > 2)
        { _villagers.Send(worker.Id, slot.Position); return true; }
        var prototype = new FurniturePiece(-1, FurnitureType.PalletRack, slot.Position, slot.Elevation, 0, 0);
        // The full weight of the stages above must fit every support's remaining load capacity.
        if (plan.Slots.Take(index).Select(s => _furniture.Pieces.FirstOrDefault(p => p.Id == s.FurnitureId)).Any(p =>
            p.Type != FurnitureType.PalletRack || p.Damage > 0 || p.Position != slot.Position ||
            FurnitureResources.Weight(prototype) * (index + 1) > Stability.LoadCapacity(p))) return false;
        SetMaterialsCore(village.Id, MaterialKind.Metal, village.Materials[MaterialKind.Metal] - PillarScaffold.MetalPerStage);
        var piece = PlaceFurniture(FurnitureType.PalletRack, slot.Position, 0, 0, village.Id % 12, slot.Elevation);
        var slots = plan.Slots.ToArray(); slots[index] = slot with { FurnitureId = piece.Id };
        _villages[village.Id] = _villages[village.Id] with { PillarScaffold = plan with { Slots = Array.AsReadOnly(slots) } };
        _villagers.PracticeBuilding(worker.Id, false);
        Emit(new ScaffoldPiecePlaced(clock, village.Id, piece.Id));
        return true;
    }

    private void ClearScaffoldFurniture(int id)
    {
        foreach (var v in _villages.ToArray())
            if (v.PillarScaffold is { } s && s.Slots.Any(slot => slot.FurnitureId == id))
                _villages[v.Id] = v with { PillarScaffold = s with { Slots = Array.AsReadOnly(s.Slots.Select(slot =>
                    slot.FurnitureId == id ? slot with { FurnitureId = null } : slot).ToArray()) } };
    }

    private void ValidateScaffolds()
    {
        var plans = _villages.Select(v => v.PillarScaffold).OfType<PillarScaffold>().ToArray();
        if (plans.Select(p => p.PillarId).Distinct().Count() != plans.Length)
            throw new InvalidDataException("Invalid world save: inconsistent Pillar scaffold.");
        foreach (var plan in plans)
        {
            var pillar = _pillars.SingleOrDefault(p => p.Id == plan.PillarId);
            if (pillar is null || plan.Slots.Count != PillarScaffold.Stages)
                throw new InvalidDataException("Invalid world save: inconsistent Pillar scaffold.");
            var position = _space.Wrap(pillar.Position + new Vector2(0, pillar.Width / 2 + 0.55f));
            if (plan.Slots.Select((slot, i) => (slot, i)).Any(x => x.slot.Position != position || x.slot.Rotation != 0 ||
                x.slot.Elevation != x.i * 4.6f || x.slot.FurnitureId is { } id && !_furniture.Pieces.Any(f =>
                    f.Id == id && f.Type == FurnitureType.PalletRack && f.Position == position && f.Elevation == x.slot.Elevation &&
                    f.Rotation == 0 && f.Tilt == 0 && f.Roll == 0 && f.Damage < 1)))
                throw new InvalidDataException("Invalid world save: inconsistent Pillar scaffold.");
        }
    }
}
