using System.Numerics;

namespace Scp3008.Core;

public sealed partial class ScenarioBuilder
{
    /// <summary>Puts a piece of Gear (a pulley or winch, say) into a Village's own stock, as if made.</summary>
    public void GiveVillageGear(int villageId, GearKind kind) => _simulation.GiveVillageGear(villageId, kind);
}

internal sealed partial class Villagers
{
    private readonly Dictionary<int, float> _pace = [];

    /// <summary>Scales a Villager's walking speed (a cart train slows its Caravan); 1 clears it. Not saved: Caravans reapply it.</summary>
    public void SetPace(int id, float pace)
    {
        if (pace >= 1) _pace.Remove(id); else _pace[id] = pace;
    }
}

public sealed partial class Simulation
{
    private const float PulleyFactor = 2, WinchFactor = 4, CarryPerPerson = 25;

    /// <summary>How many times more a Village's workers can carry or lift: a pulley doubles it, a winch quadruples it.</summary>
    private float HoistFactor(int? villageId) =>
        villageId is not { } id || id < 0 || id >= _villages.Count ? 1 :
        _villages[id].Gear.Any(g => g.Kind == GearKind.Winch) ? WinchFactor :
        _villages[id].Gear.Any(g => g.Kind == GearKind.Pulley) ? PulleyFactor : 1;

    /// <summary>How many of the Village's Villagers it takes to lift the piece onto a tower or Pillar scaffold.</summary>
    public int LiftersNeeded(int villageId, int furnitureId) => Publish(() =>
        Math.Max(1, (int)Math.Ceiling(FurnitureResources.Weight(PieceById(furnitureId) ?? throw new ArgumentException("No such Furniture.")) /
            (CarryPerPerson * HoistFactor(villageId)))));

    internal void GiveVillageGear(int villageId, GearKind kind) => Publish(() =>
    {
        var village = _villages[villageId];
        var gear = new Gear(NextGearId(), kind, village.Position);
        gear.Validate();
        _villages[villageId] = village with { Gear = Array.AsReadOnly(village.Gear.Append(gear).ToArray()) };
        return true;
    });
}
