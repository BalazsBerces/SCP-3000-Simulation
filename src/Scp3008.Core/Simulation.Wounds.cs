namespace Scp3008.Core;

/// <summary>A Wounded Villager: <see cref="Healed"/> runs from 0 (just hit) to 1 (recovered).</summary>
/// <param name="Bandaged">Whether a bandage was used on the wound, which doubles the healing.</param>
public sealed record Wound(int VillagerId, double Healed = 0, bool Bandaged = false);

/// <summary>A Staff hit a Wounded Villager: they vanish without a trace, leaving no Body.</summary>
public sealed record VillagerTaken(StoreClock At, int VillagerId, System.Numerics.Vector2 Position) : DomainEvent(At);

public sealed record VillagerWounded(StoreClock At, int VillagerId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>A Staff hit on a Villager: the first makes them Wounded, a hit while Wounded means being Taken. Whether they were Taken.</summary>
    public bool StaffHitsVillager(int villagerId) => _simulation.StaffHitsVillager(villagerId);
}

public sealed partial class Simulation
{
    // The hidden healing rules (tuning), per Store hour: about four Store days of bed rest.
    private const double HealPerHourResting = 1.0 / 96;
    private const double HealingAwake = 0.25;
    private const double HealingBandaged = 2;
    private const double HealingWellFed = 1.5;
    private const double HealingOnMattress = 1.5;

    private readonly Dictionary<int, Wound> _wounds = [];

    // shortcut: nothing makes the Staff hit Villagers by themselves yet (Sieges and patrol contact come later); only this seam does.
    internal bool StaffHitsVillager(int villagerId) => Publish(() =>
    {
        var villager = _villagers.ViewOf(villagerId) ?? throw new ArgumentException($"No Villager {villagerId}.", nameof(villagerId));
        var clock = new StoreClock(_tick);
        if (!_wounds.Remove(villagerId))
        {
            _wounds[villagerId] = new(villagerId);
            Emit(new VillagerWounded(clock, villagerId));
            return false;
        }
        CancelFurnitureTasks(t => t.VillagerIds.Contains(villagerId));
        _craftingTasks.RemoveAll(t => t.VillagerId == villagerId);
        _villagers.Remove(villagerId);
        for (var i = 0; i < _villages.Count; i++)
            if (_villages[i].VillagerIds.Contains(villagerId))
                _villages[i] = _villages[i] with
                {
                    VillagerIds = Array.AsReadOnly(_villages[i].VillagerIds.Where(id => id != villagerId).ToArray()),
                    Outposts = Array.AsReadOnly(_villages[i].Outposts.Select(o => o with { VillagerIds = Array.AsReadOnly(o.VillagerIds.Where(id => id != villagerId).ToArray()) }).ToArray()),
                };
        for (var i = _bands.Count - 1; i >= 0; i--)
            if (_bands[i].VillagerIds.Contains(villagerId))
                _bands[i] = _bands[i] with { VillagerIds = Array.AsReadOnly(_bands[i].VillagerIds.Where(id => id != villagerId).ToArray()) };
        Emit(new VillagerTaken(clock, villagerId, villager.Position));
        return true;
    });

    private void AdvanceWounds()
    {
        if (_wounds.Count == 0) return;
        foreach (var wound in _wounds.Values.ToArray())
        {
            if (_villagers.ViewOf(wound.VillagerId) is not { } v) { _wounds.Remove(wound.VillagerId); continue; }
            var resting = v.Action == VillagerAction.Resting;
            var rate = HealPerHourResting * (resting ? 1 : HealingAwake) * (wound.Bandaged ? HealingBandaged : 1) *
                (v.Needs.Hunger < 0.5 && v.Needs.Thirst < 0.5 ? HealingWellFed : 1) *
                (resting && v.SleepingPlace?.FurnitureId is { } id && _furniture.Pieces.Any(p => p.Id == id && p.Type == FurnitureType.Mattress) ? HealingOnMattress : 1);
            var healed = wound.Healed + rate * StoreClock.TickDuration.TotalHours;
            if (healed >= 1) _wounds.Remove(wound.VillagerId);
            else _wounds[wound.VillagerId] = wound with { Healed = healed };
        }
    }
}

internal sealed partial class Villagers
{
    /// <summary>Removes a Villager without a Body (Taken by the Staff).</summary>
    public void Remove(int id) => _walkers.RemoveAll(w => w.Id == id);
}
