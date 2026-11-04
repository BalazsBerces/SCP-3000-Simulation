namespace Scp3008.Core;

/// <summary>A Guard of an allied Village who came to defend another; he goes home when the danger is over.</summary>
public sealed record Reinforcement(int VillagerId, int FromVillageId);

/// <summary>Two Villages swore an alliance.</summary>
public sealed record AllianceFormed(StoreClock At, int VillageId, int OtherVillageId) : DomainEvent(At);

/// <summary>An ally sent Guards to a Village under Siege or Raid.</summary>
public sealed record GuardsSent(StoreClock At, int FromVillageId, int ToVillageId, IReadOnlyList<int> GuardIds) : DomainEvent(At);

/// <summary>Allies exchanged Recipes or Beliefs; <paramref name="VillageId"/> learnt something.</summary>
public sealed record AllianceShared(StoreClock At, int VillageId, int OtherVillageId, IReadOnlyList<GearKind> Recipes, bool Belief) : DomainEvent(At);

/// <summary>The survivors of a fallen Village were taken in by its ally instead of wandering as a Band.</summary>
public sealed record RefugeesTakenIn(StoreClock At, int FallenVillageId, int AllyVillageId, IReadOnlyList<int> VillagerIds) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Allies two Villages at once, as if an Envoy had carried the oath.</summary>
    public void FormAlliance(int villageId, int otherVillageId) => _simulation.FormAlliance(villageId, otherVillageId);
}

public sealed partial class Simulation
{
    // The hidden alliance rules (tuning).
    private const int AllianceEvery = 50, GuardsPerAlly = 2;
    private const float ReinforceReach = 12;
    private const double AllyBarterBonus = 1.25;

    internal void FormAlliance(int a, int b) => Publish(() =>
    {
        if (a == b || a < 0 || b < 0 || a >= _villages.Count || b >= _villages.Count) throw new ArgumentException("An alliance is between two different Villages.");
        FormAlliance(a, b, new StoreClock(_tick));
        return true;
    });

    private void FormAlliance(int a, int b, StoreClock clock)
    {
        if (_villages[a].Allies.Contains(b)) return;
        _villages[a] = _villages[a] with { Allies = [.. _villages[a].Allies, b] };
        _villages[b] = _villages[b] with { Allies = [.. _villages[b].Allies.Where(x => x != a), a] };
        Emit(new AllianceFormed(clock, a, b));
    }

    // The reinforcing Guards that have reached the Village.
    private IEnumerable<int> ArrivedReinforcements(Village village) => village.Reinforcements.Select(r => r.VillagerId)
        .Where(id => !_wounds.ContainsKey(id) && _villagers.ViewOf(id) is { } g && _space.Distance(g.Position, village.Position) <= ReinforceReach);

    // Each ally that is not itself in danger sends up to two Guards, keeping one at home.
    private void SendGuards(int villageId, StoreClock clock)
    {
        foreach (var ally in _villages[villageId].Allies.Select(id => _villages[id]).ToArray())
        {
            var target = _villages[villageId];
            if (ally.Fallen || target.Reinforcements.Any(r => r.FromVillageId == ally.Id) || _hordes.Any(h => h.SiegeVillageId == ally.Id) ||
                _raids.Any(r => r.ToVillageId == ally.Id)) continue;
            var guards = ally.VillagerIds.Select(_villagers.ViewOf).OfType<Villager>()
                .Where(v => v.Role == Role.Guard && !_wounds.ContainsKey(v.Id) && IsAvailableWorker(v) && v.Action != VillagerAction.Trading &&
                    ally.Captives.All(c => c.VillagerId != v.Id)).ToArray();
            var sent = guards.Take(Math.Min(GuardsPerAlly, guards.Length - 1)).Select(v => v.Id).ToArray();
            if (sent.Length == 0) continue;
            var stand = _navigation.NearestWalkable(target.Position);
            foreach (var id in sent) _villagers.Engage(id, VillagerAction.Trading, stand);
            _villages[villageId] = target with { Reinforcements = [.. target.Reinforcements, .. sent.Select(id => new Reinforcement(id, ally.Id))] };
            Emit(new GuardsSent(clock, ally.Id, villageId, sent));
        }
    }

    private void AdvanceAlliances(StoreClock clock)
    {
        if (_tick % AllianceEvery != 7) return;
        foreach (var village in _villages.ToArray())
        {
            if (village.Fallen && village.Reinforcements.Count == 0) continue;
            // Allies march as soon as a Siege begins or Raiders set out, so they can be there when the blows fall.
            if (!village.Fallen && (_hordes.Any(h => h.SiegeVillageId == village.Id) || _raids.Any(r => r.ToVillageId == village.Id && !r.Returning)))
                SendGuards(village.Id, clock);
            else if (village.Reinforcements.Count > 0 && !_raids.Any(r => r.ToVillageId == village.Id)) SendHome(village);
        }
        foreach (var a in _villages.Where(v => !v.Fallen && v.Allies.Count > 0).ToArray())
            foreach (var other in a.Allies.Where(id => id > a.Id).Select(id => _villages[id]).Where(v => !v.Fallen))
                Share(a.Id, other.Id, clock);
    }

    private void SendHome(Village village)
    {
        foreach (var r in village.Reinforcements)
        {
            _villagers.Release(r.VillagerId, VillagerAction.Trading);
            if (_villagers.ViewOf(r.VillagerId) is not null) _villagers.Send(r.VillagerId, _navigation.NearestWalkable(_villages[r.FromVillageId].Position));
        }
        _villages[village.Id] = village with { Reinforcements = [] };
    }

    // Allies learn each other's Recipes, and an ally without a Horde Belief takes the other's.
    private void Share(int aId, int bId, StoreClock clock)
    {
        var a = _villages[aId];
        var b = _villages[bId];
        var union = a.Recipes.Union(b.Recipes).ToArray();
        var common = a.HordeBelief ?? b.HordeBelief;
        if (union.Length == a.Recipes.Count && union.Length == b.Recipes.Count && (a.HordeBelief is null) == (b.HordeBelief is null)) return;
        foreach (var (v, other) in new[] { (a, b), (b, a) })
        {
            var learnt = union.Except(v.Recipes).ToArray();
            var gotBelief = v.HordeBelief is null && common is not null;
            _villages[v.Id] = v with { Recipes = Array.AsReadOnly(union), HordeBelief = v.HordeBelief ?? common };
            if (learnt.Length > 0 || gotBelief) Emit(new AllianceShared(clock, v.Id, other.Id, learnt, gotBelief));
        }
    }

    // An ally takes the survivors in, with what they know; true when one did.
    private bool TakeInRefugees(Village fallen, int[] survivors, StoreClock clock)
    {
        if (survivors.Length == 0 || fallen.Allies.Select(id => _villages[id]).FirstOrDefault(v => !v.Fallen && v.VillagerIds.Count > 0) is not { } ally) return false;
        var joined = ally with
        {
            VillagerIds = Array.AsReadOnly(ally.VillagerIds.Concat(survivors).ToArray()),
            KnownMap = MergeMaps([ally.KnownMap, fallen.KnownMap]),
            Recipes = Array.AsReadOnly(ally.Recipes.Union(fallen.Recipes).ToArray()),
        };
        _villages[ally.Id] = joined;
        _villagers.JoinVillage(joined);
        _villagers.ShareKnowledge(survivors, joined.KnownMap);
        foreach (var id in survivors) _villagers.Send(id, _navigation.NearestWalkable(ally.Position));
        Emit(new RefugeesTakenIn(clock, fallen.Id, ally.Id, Array.AsReadOnly(survivors)));
        return true;
    }
}
