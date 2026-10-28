namespace Scp3008.Core;

/// <summary>A known way to make Gear. The basic Recipes need no Workshop.</summary>
public sealed record Recipe(GearKind Gear, string Name, MaterialKind? Material, int Amount,
    GearKind? Tool = null, GoodsKind? Goods = null, double Hours = 0.5, WorkshopKind? Workshop = null, MaterialKind? ExtraMaterial = null, int ExtraAmount = 0, bool ConsumesTool = false);

public static class Recipes
{
    public static IReadOnlyList<Recipe> Middle { get; } = Array.AsReadOnly(new Recipe[]
    {
        new(GearKind.LeatherBackpack, "Leather backpack", MaterialKind.Leather, 4, GearKind.KitchenKnife, Workshop: WorkshopKind.SewingTable),
        new(GearKind.Saw, "Saw", MaterialKind.Metal, 3, GearKind.HexKey, Workshop: WorkshopKind.Workbench),
        new(GearKind.Crowbar, "Crowbar", MaterialKind.Metal, 3, GearKind.HexKey, Workshop: WorkshopKind.Workbench),
        new(GearKind.Spear, "Spear", MaterialKind.Metal, 2, GearKind.KitchenKnife, Workshop: WorkshopKind.Workbench, ConsumesTool: true),
        new(GearKind.TabletopShield, "Tabletop shield", MaterialKind.WoodPanel, 4, GearKind.HexKey, Workshop: WorkshopKind.Workbench),
        new(GearKind.PlatformCart, "Platform cart", MaterialKind.Metal, 6, GearKind.HexKey, Workshop: WorkshopKind.Workbench, ExtraMaterial: MaterialKind.WoodPanel, ExtraAmount: 4),
        new(GearKind.QuiltedArmour, "Quilted armour", MaterialKind.Fabric, 4, GearKind.Scissors, Workshop: WorkshopKind.SewingTable, ExtraMaterial: MaterialKind.Foam, ExtraAmount: 4),
        new(GearKind.Lantern, "Lantern", MaterialKind.Electrics, 2, GearKind.Screwdriver, Workshop: WorkshopKind.ElectricsBench, ExtraMaterial: MaterialKind.Glass, ExtraAmount: 2),
        new(GearKind.HandCrankedGenerator, "Hand-cranked generator", MaterialKind.Electrics, 4, GearKind.Screwdriver, Hours: 1, Workshop: WorkshopKind.ElectricsBench, ExtraMaterial: MaterialKind.Metal, ExtraAmount: 4),
    });

    public static IReadOnlyList<Recipe> Basic { get; } = Array.AsReadOnly(new Recipe[]
    {
        new(GearKind.TableLegClub, "Table-leg club", MaterialKind.WoodPanel, 2),
        new(GearKind.CanvasBag, "Canvas bag", MaterialKind.Fabric, 4, GearKind.Scissors),
        new(GearKind.Candle, "Candle", null, 1, Goods: GoodsKind.Candles, Hours: 0.1),
        new(GearKind.CardboardArmour, "Cardboard armour", MaterialKind.WoodPanel, 2),
        new(GearKind.Bandage, "Bandage", MaterialKind.Fabric, 1, Hours: 0.1),
    });
    public static IReadOnlyList<Recipe> All { get; } = Array.AsReadOnly(Basic.Concat(Middle).ToArray());
}

public sealed record CraftingTask(int VillagerId, int VillageId, GearKind Gear, double Progress = 0);
public sealed record GearMade(StoreClock At, int VillagerId, int VillageId, Gear Gear, bool First) : DomainEvent(At);
public sealed record BandageUsed(StoreClock At, int VillagerId, int GearId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public void SetMaterials(int villageId, MaterialKind kind, int amount) => _simulation.SetMaterials(villageId, kind, amount);
    public void SetStores(int villageId, GoodsKind kind, int amount) => _simulation.SetStores(villageId, kind, amount);
    public void SetSkill(int villagerId, Role role, double skill) => _simulation.SetSkill(villagerId, role, skill);
    public void MakeGear(int villagerId, GearKind kind) => _simulation.MakeGear(villagerId, kind);
    public bool UseBandage(int villagerId) => _simulation.UseBandage(villagerId);
}

internal sealed partial class Villagers
{
    public void SetSkill(int id, Role role, double skill)
    {
        if (!Enum.IsDefined(role) || !double.IsFinite(skill) || skill < 0)
            throw new ArgumentOutOfRangeException(nameof(skill));
        _walkers.Single(w => w.Id == id).Skills[role] = skill;
    }

    public void CraftingPose(int id, bool crafting)
    {
        FurniturePose(id, null, crafting ? VillagerAction.Crafting : VillagerAction.Idle);
        _walkers.Single(w => w.Id == id).Crafting = crafting;
    }

    public void ConsumeCraftingTool(int id) => _walkers.Single(w => w.Id == id).Hand = null;

    public void FinishCrafting(int id, bool usedTool)
    {
        PracticeBuilding(id, usedTool);
        CraftingPose(id, false);
    }

    public bool ConsumeBandage(int id)
    {
        var w = _walkers.Single(w => w.Id == id);
        if (w.Hand?.Kind != GearKind.Bandage) return false;
        w.Hand = null;
        return true;
    }
}

public sealed partial class Simulation
{
    private readonly List<CraftingTask> _craftingTasks = [];

    private void RestoreCrafting(WorldSnapshot world)
    {
        if (world.Villages.Any(v => v.Recipes.Distinct().Count() != v.Recipes.Count ||
                v.Recipes.Any(k => !Recipes.All.Any(r => r.Gear == k))) ||
            world.Bands.Any(b => b.Recipes.Distinct().Count() != b.Recipes.Count || b.Recipes.Any(k => !Recipes.All.Any(r => r.Gear == k))) ||
            world.CraftingTasks.Select(t => t.VillagerId).Concat(world.Experiments.Select(e => e.VillagerId)).Distinct().Count() != world.CraftingTasks.Count + world.Experiments.Count ||
            world.CraftingTasks.Any(t => !double.IsFinite(t.Progress) || t.Progress < 0 || t.Progress >= 1 ||
                !world.Villages.Any(v => v.Id == t.VillageId && v.Recipes.Contains(t.Gear)) ||
                !world.Villagers.Any(v => v.Id == t.VillagerId && v.VillageId == t.VillageId && v.Crafting && v.FurnitureTaskId is null)) ||
            world.Villagers.Any(v => v.Crafting != (world.CraftingTasks.Any(t => t.VillagerId == v.Id) || world.Experiments.Any(e => e.VillagerId == v.Id))))
            throw new InvalidDataException("Invalid world save: inconsistent Recipes or crafting tasks.");
        _craftingTasks.AddRange(world.CraftingTasks);
    }

    internal void SetMaterials(int id, MaterialKind kind, int amount) => Publish(() =>
    {
        if (!Enum.IsDefined(kind) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        SetMaterialsCore(id, kind, amount);
        return true;
    });

    private void SetMaterialsCore(int id, MaterialKind kind, int amount)
    {
        var village = _villages.Single(v => v.Id == id);
        var materials = village.Materials.ToDictionary();
        if (amount == 0) materials.Remove(kind); else materials[kind] = amount;
        _villages[id] = village with { Materials = new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(materials) };
    }

    internal void SetStores(int id, GoodsKind kind, int amount) => Publish(() =>
    {
        if (!Enum.IsDefined(kind) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var village = _villages.Single(v => v.Id == id);
        ChangeStores(id, kind, amount - village.Stores.GetValueOrDefault(kind));
        return true;
    });

    internal void SetSkill(int id, Role role, double skill) => Publish(() =>
    {
        _villagers.SetSkill(id, role, skill);
        return true;
    });

    private bool CanMake(Villager worker, Village village, Recipe recipe) =>
        worker.FurnitureTaskId is null && !worker.Crafting && worker.Action is not (VillagerAction.GoingToMeal or VillagerAction.SharedMeal) &&
        worker.CarriedAmount + worker.CarriedMaterials == 0 &&
        worker.Hand?.Kind is not (GearKind.ShoppingCart or GearKind.PlatformCart) && (recipe.Tool is null || worker.Hand?.Kind == recipe.Tool) &&
        _space.Distance(worker.Position, village.Position) <= 2 &&
        worker.Needs.Hunger < 0.9 && worker.Needs.Thirst < 0.9 &&
        (recipe.Workshop is null || village.Workshops.Any(w => w.Kind == recipe.Workshop && w.Complete)) &&
        (recipe.ExtraMaterial is not { } extra || village.Materials.GetValueOrDefault(extra) >= recipe.ExtraAmount) &&
        (recipe.Material is not { } material || village.Materials.GetValueOrDefault(material) >= recipe.Amount) &&
        (recipe.Goods is not { } goods || village.Stores.GetValueOrDefault(goods) >= recipe.Amount);

    internal void MakeGear(int id, GearKind kind) => Publish(() =>
    {
        var worker = _villagers.Snapshot().Single(v => v.Id == id);
        var village = _villages.Single(v => v.Id == worker.VillageId);
        var recipe = Recipes.All.SingleOrDefault(r => r.Gear == kind && village.Recipes.Contains(kind))
            ?? throw new InvalidOperationException("Recipe is not known.");
        if (!CanMake(worker, village, recipe)) throw new InvalidOperationException("Crafting needs a free worker at home, Materials and the required tool.");
        StartCrafting(worker, village, recipe);
        return true;
    });

    private void StartCrafting(Villager worker, Village village, Recipe recipe)
    {
        if (recipe.Material is { } material) SetMaterialsCore(village.Id, material, village.Materials[material] - recipe.Amount);
        if (recipe.ExtraMaterial is { } extra) SetMaterialsCore(village.Id, extra, village.Materials[extra] - recipe.ExtraAmount);
        if (recipe.Goods is { } goods) ChangeStores(village.Id, goods, -recipe.Amount);
        _craftingTasks.Add(new(worker.Id, village.Id, recipe.Gear));
        _villagers.CraftingPose(worker.Id, true);
    }

    private void AdvanceCrafting(StoreClock clock)
    {
        if (_craftingTasks.Count == 0 && (_villages.Count == 0 || _tick % 50 != 1)) return;
        var people = _villagers.Snapshot();
        foreach (var task in _craftingTasks.ToArray())
        {
            var worker = people.SingleOrDefault(v => v.Id == task.VillagerId);
            var recipe = Recipes.All.Single(r => r.Gear == task.Gear);
            if (worker is null || worker.VillageId != task.VillageId ||
                (recipe.Tool is { } tool && worker.Hand?.Kind != tool) ||
                (recipe.Workshop is { } workshop && !_villages[task.VillageId].Workshops.Any(w => w.Kind == workshop && w.Complete)) || worker.Needs.Hunger >= 0.9 || worker.Needs.Thirst >= 0.9)
            {
                _craftingTasks.Remove(task);
                if (worker is not null) _villagers.CraftingPose(worker.Id, false);
                continue;
            }
            var skill = worker.Skills.GetValueOrDefault(Role.Builder);
            var progress = task.Progress + StoreClock.TickDuration.TotalHours * (1 + skill) / recipe.Hours;
            if (progress < 1)
            {
                _craftingTasks[_craftingTasks.IndexOf(task)] = task with { Progress = progress };
                continue;
            }
            var village = _villages[task.VillageId];
            var gear = new Gear(NextGearId(), task.Gear, village.Position) { Durability = 60 + 40 * (skill / (1 + skill)) };
            _villages[village.Id] = village with { Gear = Array.AsReadOnly(village.Gear.Append(gear).ToArray()), HasMadeGear = true };
            _craftingTasks.Remove(task);
            _villagers.FinishCrafting(worker.Id, recipe.Tool is not null);
            if (recipe.ConsumesTool) _villagers.ConsumeCraftingTool(worker.Id);
            Emit(new GearMade(clock, worker.Id, village.Id, gear, !village.HasMadeGear));
        }
        if (_tick % 50 != 1) return;
        foreach (var worker in _villagers.Snapshot().Where(v => v.Role == Role.Builder && v.VillageId is not null))
        {
            var village = _villages[worker.VillageId!.Value];
            var recipe = Recipes.All.FirstOrDefault(r => village.Recipes.Contains(r.Gear) && CanMake(worker, village, r) &&
                village.Gear.Count(g => g.Kind == r.Gear) + people.Count(v => v.VillageId == village.Id &&
                    new[] { v.Hand, v.Bag, v.Armour }.Any(g => g?.Kind == r.Gear)) +
                    _craftingTasks.Count(t => t.VillageId == village.Id && t.Gear == r.Gear) < village.VillagerIds.Count);
            if (recipe is not null) StartCrafting(worker, village, recipe);
        }
    }

    internal bool UseBandage(int id) => Publish(() =>
    {
        var worker = _villagers.Snapshot().Single(v => v.Id == id);
        if (worker.Crafting || worker.FurnitureTaskId is not null) return false;
        var gear = worker.Hand;
        if (gear?.Kind == GearKind.Bandage) _villagers.ConsumeBandage(id);
        else
        {
            var village = _villages.FirstOrDefault(v => v.Id == worker.VillageId && _space.Distance(v.Position, worker.Position) <= 2);
            gear = village?.Gear.FirstOrDefault(g => g.Kind == GearKind.Bandage);
            if (village is null || gear is null) return false;
            _villages[village.Id] = village with { Gear = Array.AsReadOnly(village.Gear.Where(g => g.Id != gear.Id).ToArray()) };
        }
        Emit(new BandageUsed(new StoreClock(_tick), id, gear.Id));
        return true;
    });
}
