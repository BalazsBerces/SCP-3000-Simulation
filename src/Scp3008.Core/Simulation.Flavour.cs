using System.Numerics;

namespace Scp3008.Core;

/// <summary>A Village's Personality turned, e.g. from a long famine toward raiding.</summary>
public sealed record PersonalityChanged(StoreClock At, int VillageId, Personality From, Personality To) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Overrides the type of Department a Village stands in (tests; the sandbox is all one Department).</summary>
    public void SetDepartment(int villageId, DepartmentType type) => _simulation.SetDepartment(villageId, type);
}

/// <summary>Village Personality drift and Department flavour: what a Village's Department makes it good (or bad) at.</summary>
public sealed partial class Simulation
{
    // Ticks of famine (less food than people) after which a Village turns to raiding: one Store day.
    internal const int FamineDrift = 12000;
    // Material-rich, trade hub and morale flavour pay out this often (ticks).
    private const int FlavourEvery = 500;
    private const double BedroomMorale = 0.9;
    private const float GlowFactor = 1.5f;
    private const float GlowRange = 30;

    internal void SetDepartment(int villageId, DepartmentType type) => Publish(() =>
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        _villages[villageId] = _villages[villageId] with { Department = type };
        return true;
    });

    // Lighting Villages glow: lamps and held lights near them reach further (light has no effect on the Staff).
    private float Glow(Vector2 position) =>
        _villages.Any(v => v is { Department: DepartmentType.LightingAndElectrical, Fallen: false } && _space.Distance(v.Position, position) <= GlowRange)
            ? GlowFactor : 1;

    // Plant section Villages put half their people on the Crops; others a single farmer.
    private static int WantedFarmers(Village village, int members) =>
        village.Department == DepartmentType.PlantSection ? Math.Max(1, members / 2) : 1;

    private void AdvanceFlavour(StoreClock clock)
    {
        if (_villages.Count == 0 || _tick % 50 != 1) return;
        var periodic = _tick % FlavourEvery == 1;
        foreach (var village in _villages.ToArray())
        {
            if (village.Fallen || village.VillagerIds.Count == 0) continue;
            var famine = village.Stores.Where(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).Sum(g => g.Value) < village.VillagerIds.Count;
            var ticks = famine ? village.FamineTicks + 50 : 0;
            var personality = ticks >= FamineDrift ? Personality.Raiders : village.Personality;
            _villages[village.Id] = village with { FamineTicks = ticks, Personality = personality };
            if (personality != village.Personality) Emit(new PersonalityChanged(clock, village.Id, village.Personality, personality));
            if (!periodic) continue;
            switch (village.Department)
            {
                case DepartmentType.SelfServeWarehouse:
                    var materials = village.Materials.ToDictionary();
                    materials[MaterialKind.WoodPanel] = materials.GetValueOrDefault(MaterialKind.WoodPanel) + 1;
                    _villages[village.Id] = _villages[village.Id] with { Materials = new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(materials) };
                    break;
                case DepartmentType.RestaurantAndFoodMarket when _villages.Any(o => o.Id != village.Id && !o.Fallen && o.VillagerIds.Count > 0):
                    ChangeStores(village.Id, GoodsKind.TinnedFood, 1);
                    break;
                case DepartmentType.Showrooms:
                    foreach (var id in village.VillagerIds)
                        if (_villagers.ViewOf(id) is { } view && view.Needs.Morale < BedroomMorale)
                            _villagers.SetNeeds(id, view.Needs with { Morale = Math.Min(BedroomMorale, view.Needs.Morale + 0.05) });
                    break;
            }
        }
    }
}
