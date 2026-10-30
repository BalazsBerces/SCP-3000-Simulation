namespace Scp3008.Core;

public sealed record AssemblyInstructions(int FurnitureId, GearKind Recipe);
public sealed record Experiment(int VillagerId, int VillageId, GearKind Gear, double Progress = 0);
public sealed record RecipeLearned(StoreClock At, int VillageId, GearKind Gear, string Source) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public void PlaceAssemblyInstructions(int furnitureId, GearKind recipe) => _simulation.PlaceAssemblyInstructions(furnitureId, recipe);
}

public sealed partial class Simulation
{
    private readonly List<AssemblyInstructions> _instructions = [];
    private readonly List<Experiment> _experiments = [];

    internal void PlaceAssemblyInstructions(int furnitureId, GearKind recipe) => Publish(() =>
    {
        var piece = Piece(furnitureId);
        if (piece.Type is not (FurnitureType.FlatPackBox or FurnitureType.LabelledBox) ||
            !Recipes.Middle.Any(r => r.Gear == recipe) || _instructions.Any(i => i.FurnitureId == furnitureId))
            throw new ArgumentException("Assembly instructions need a box and a middle Recipe.");
        _instructions.Add(new(furnitureId, recipe));
        return true;
    });

    private void LearnRecipe(int villageId, GearKind gear, StoreClock clock, string source)
    {
        var village = _villages[villageId];
        if (village.Recipes.Contains(gear)) return;
        _villages[villageId] = village with { Recipes = Array.AsReadOnly(village.Recipes.Append(gear).ToArray()) };
        Emit(new RecipeLearned(clock, villageId, gear, source));
    }

    private void AdvanceKnowHow(StoreClock clock)
    {
        foreach (var experiment in _experiments.ToArray())
        {
            var worker = _villagers.Snapshot().SingleOrDefault(v => v.Id == experiment.VillagerId);
            var village = _villages[experiment.VillageId];
            var recipe = Recipes.Middle.Single(r => r.Gear == experiment.Gear);
            if (worker is null || worker.VillageId != village.Id || worker.Hand?.Kind != recipe.Tool ||
                !village.Workshops.Any(w => w.Kind == recipe.Workshop && w.Complete) ||
                worker.Needs.Hunger >= 0.9 || worker.Needs.Thirst >= 0.9)
            {
                _experiments.Remove(experiment);
                if (worker is not null) _villagers.CraftingPose(worker.Id, false);
                continue;
            }
            var progress = experiment.Progress + StoreClock.TickDuration.TotalHours *
                (1 + worker.Skills.GetValueOrDefault(Role.Builder)) / 8;
            if (progress < 1)
                _experiments[_experiments.IndexOf(experiment)] = experiment with { Progress = progress };
            else
            {
                _experiments.Remove(experiment);
                _villagers.FinishCrafting(worker.Id, true);
                LearnRecipe(village.Id, experiment.Gear, clock, "experimenting");
            }
        }
        if (_tick % 50 != 1) return;
        var people = _villagers.Snapshot();
        if (people.Length == 0) return;
        var nearby = _instructions.Count == 0 ? new Dictionary<int, FurniturePiece>() : people
            .SelectMany(v => _villagers.FurnitureNear(v.Position, 3)).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        foreach (var instructions in _instructions.ToArray())
        {
            if (!nearby.TryGetValue(instructions.FurnitureId, out var piece)) continue;
            var finder = people.FirstOrDefault(v => _space.Distance(v.Position, piece.Position) <= 3 &&
                (v.VillageId is { } home ? !_villages[home].Recipes.Contains(instructions.Recipe) :
                    _bands.Any(b => b.VillagerIds.Contains(v.Id) && !b.Recipes.Contains(instructions.Recipe))));
            if (finder is null) continue;
            if (finder.VillageId is { } villageId) LearnRecipe(villageId, instructions.Recipe, clock, "assembly instructions");
            else
            {
                var band = _bands.Single(b => b.VillagerIds.Contains(finder.Id));
                _bands[_bands.IndexOf(band)] = band with { Recipes = Array.AsReadOnly(band.Recipes.Append(instructions.Recipe).ToArray()) };
            }
            _instructions.Remove(instructions);
        }
        foreach (var worker in people.Where(v => v.Role == Role.Builder && v.VillageId is not null))
        {
            var village = _villages[worker.VillageId!.Value];
            var recipe = Recipes.Middle.FirstOrDefault(r => !village.Recipes.Contains(r.Gear) &&
                !_experiments.Any(e => e.VillageId == village.Id && e.Gear == r.Gear) && CanMake(worker, village, r));
            if (recipe is null) continue;
            SetMaterialsCore(village.Id, recipe.Material!.Value, village.Materials[recipe.Material.Value] - recipe.Amount);
            if (recipe.ExtraMaterial is { } extra) SetMaterialsCore(village.Id, extra, village.Materials[extra] - recipe.ExtraAmount);
            _experiments.Add(new(worker.Id, village.Id, recipe.Gear));
            _villagers.CraftingPose(worker.Id, true);
        }
    }

    private void RestoreKnowHow(WorldSnapshot world)
    {
        var slots = world.Villages.SelectMany(v => v.Workshops.Select(w => w.Slot)).ToArray();
        var ids = slots.Concat(world.Villages.SelectMany(v => v.Dwellings).SelectMany(d => d.Slots))
            .Concat(world.Villages.SelectMany(v => v.Castle).SelectMany(c => c.Slots))
            .Concat(world.Villages.SelectMany(v => v.MessHall?.Slots ?? [])).Concat(world.Villages.SelectMany(v => v.Storehouse?.Slots ?? []))
            .Concat(world.Villages.SelectMany(v => v.Walls).SelectMany(w => w.Slots))
            .SelectMany(s => new[] { s.FurnitureId, s.ReservedFurnitureId }).OfType<int>().ToArray();
        if (ids.Distinct().Count() != ids.Length || world.Villages.Any(v =>
            v.Workshops.Select(w => w.Kind).Distinct().Count() != v.Workshops.Count || v.Workshops.Any(w =>
                !Enum.IsDefined(w.Kind) || !float.IsFinite(w.Slot.Position.X) || !float.IsFinite(w.Slot.Position.Y) ||
                !float.IsFinite(w.Slot.Rotation) || w.Slot.Width != 0.8f || w.Slot.MinimumHeight != 0.6f ||
                w.Slot.MinimumWeight != 10 || w.Slot.Run != WallRun.Straight ||
                w.Slot.FurnitureId is not null && w.Slot.ReservedFurnitureId is not null ||
                w.Slot.FurnitureId is { } f && !world.Furniture.Any(p => p.Id == f && WorkshopFits(w.Slot, p) && p.Position == w.Slot.Position && p.Rotation == w.Slot.Rotation) ||
                w.Slot.ReservedFurnitureId is { } r && (!world.Furniture.Any(p => p.Id == r && WorkshopFits(w.Slot, p)) ||
                    !world.FurnitureTasks.Any(t => t.FurnitureId == r && t.Work != FurnitureWork.Disassemble && t.VillagerIds.All(v.VillagerIds.Contains))))))
            throw new InvalidDataException("Invalid world save: inconsistent Workshops.");
        if (world.AssemblyInstructions.Select(i => i.FurnitureId).Distinct().Count() != world.AssemblyInstructions.Count ||
            world.AssemblyInstructions.Any(i => !Recipes.Middle.Any(r => r.Gear == i.Recipe) ||
                !world.Furniture.Any(p => p.Id == i.FurnitureId && p.Type is FurnitureType.FlatPackBox or FurnitureType.LabelledBox)) ||
            world.Experiments.Any(e => !double.IsFinite(e.Progress) || e.Progress < 0 || e.Progress >= 1 ||
                !Recipes.Middle.Any(r => r.Gear == e.Gear) ||
                !world.Villages.Any(v => v.Id == e.VillageId) ||
                !world.Villagers.Any(v => v.Id == e.VillagerId && v.VillageId == e.VillageId && v.Crafting && v.FurnitureTaskId is null)))
            throw new InvalidDataException("Invalid world save: inconsistent know-how.");
        _instructions.AddRange(world.AssemblyInstructions);
        _experiments.AddRange(world.Experiments);
    }
}
