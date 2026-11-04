using System.Numerics;

namespace Scp3008.Core;

/// <summary>Why a group left its Village, or an Outpost became a Village of its own.</summary>
public enum SplitReason { Overcrowded, LowMorale, OutpostGrew, OutpostDrifted }

/// <summary>A group of a Village founded a new one (from the Village itself or from one of its Outposts).</summary>
public sealed record VillageSplit(StoreClock At, int FromVillageId, Village Village, SplitReason Reason) : DomainEvent(At);

/// <summary>The last standing Village fell; Wanderers keep arriving and the Store enters a new era.</summary>
public sealed record EraBegan(StoreClock At, int Era, int LastVillageId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Settles some of a Village's members at an Outpost and plans its barricade (tests).</summary>
    public Outpost PlaceOutpost(int villageId, Vector2 position, IReadOnlyList<int> villagerIds, TimeSpan age = default) =>
        _simulation.PlaceOutpost(villageId, position, villagerIds, age);
}

/// <summary>Splits: overcrowded or discontented Villages and large or distant Outposts found new Villages; new eras.</summary>
public sealed partial class Simulation
{
    // A Village with this many more people than covered sleeping places is overcrowded; building Dwellings lifts the limit.
    private const int CrowdMargin = 12;
    // Below this average morale a Village is in internal conflict.
    private const double SplitMorale = 0.25;
    // Fewer people than this never split: each side must be able to found a Village.
    private const int SplitMinimum = 2 * FoundingGroup;
    // A Village splits at most once a Store day.
    private const long SplitCooldown = StoreClock.TicksPerStoreDay;
    // A new Village keeps this far from every standing Village, so its surroundings are its own.
    private const float SplitDistance = 48;
    // An Outpost with this many people is a Village of its own.
    private const int OutpostVillageSize = 5;
    // An Outpost this far from home (well beyond the 48 m an Outpost is first sent) that has stood a Store day drifts apart.
    private const float OutpostDriftDistance = 64;

    private int _era = 1;

    internal Outpost PlaceOutpost(int villageId, Vector2 position, IReadOnlyList<int> ids, TimeSpan age) => Publish(() =>
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (villageId < 0 || villageId >= _villages.Count || !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            age < TimeSpan.Zero || ids.Count == 0 || ids.Distinct().Count() != ids.Count ||
            ids.Any(id => !_villages[villageId].VillagerIds.Contains(id) ||
                _villages[villageId].Outposts.Any(o => !o.Abandoned && o.VillagerIds.Contains(id))))
            throw new ArgumentException("An Outpost needs distinct members of the Village not already at an Outpost and a finite position.");
        var village = _villages[villageId];
        var site = _navigation.NearestWalkable(_space.Wrap(position));
        var outpost = new Outpost(village.Outposts.Count, site, village.Walls.Count, Array.AsReadOnly(ids.ToArray()),
            Founded: _tick - StoreClock.TicksFor(age));
        _villages[villageId] = village with
        {
            Outposts = [.. village.Outposts, outpost],
            Walls = [.. village.Walls, CreateBarricade(new Village(villageId, site, outpost.VillagerIds))],
        };
        foreach (var id in ids) _villagers.Send(id, site);
        return outpost;
    });

    private void AdvanceSplits(StoreClock clock)
    {
        if (_tick % 50 != 1 || clock.Phase == DayPhase.ClosingTime || !(_config.Splits ?? !(_config.PillarLayout == PillarLayout.VillagerSandbox ||
            _config.LandmarkShowcase || _config.PointOfInterestShowcase))) return;
        var people = _villagers.Snapshot().ToDictionary(v => v.Id);
        // People away on Caravans, Envoys, Raids (and Captives being led home), fights or guard duty elsewhere stay with their Village.
        var away = _caravans.SelectMany(c => c.VillagerIds).Concat(_envoys.Select(e => e.VillagerId))
            .Concat(_raids.SelectMany(r => r.VillagerIds.Concat(r.CaptiveIds))).Concat(_fights.SelectMany(f => f.VillagerIds))
            .Concat(_villages.SelectMany(v => v.Reinforcements.Select(r => r.VillagerId))).ToHashSet();
        foreach (var original in _villages.ToArray())
        {
            if (original.Fallen || _hordes.Any(h => h.SiegeVillageId == original.Id)) continue;
            foreach (var post in original.Outposts.Where(o => !o.Abandoned))
            {
                var reason = post.VillagerIds.Count >= OutpostVillageSize ? SplitReason.OutpostGrew
                    : _space.Distance(post.Position, original.Position) > OutpostDriftDistance && _tick - post.Founded >= StoreClock.TicksPerStoreDay
                        ? SplitReason.OutpostDrifted : (SplitReason?)null;
                var settlers = post.VillagerIds.Where(id => !away.Contains(id)).ToArray();
                if (reason is not { } why || settlers.Length < FoundingGroup) continue;
                var village = _villages[original.Id];
                _villages[original.Id] = village with
                {
                    Outposts = [.. village.Outposts.Select(o => o.Id == post.Id ? o with { VillagerIds = [], Abandoned = true } : o)],
                };
                Split(clock, original.Id, settlers, post.Position, why);
            }
            var home = _villages[original.Id];
            if (home.LastSplit is { } last && _tick - last < SplitCooldown) continue;
            var members = home.VillagerIds.Where(people.ContainsKey).Select(id => people[id]).ToArray();
            if (members.Length < SplitMinimum) continue;
            var atPost = home.Outposts.Where(o => !o.Abandoned).SelectMany(o => o.VillagerIds).ToHashSet();
            var free = members.Where(v => IsAvailableWorker(v) && !atPost.Contains(v.Id) && !away.Contains(v.Id) && !home.Captives.Any(c => c.VillagerId == v.Id)).ToArray();
            var crowded = members.Length > home.CoveredSleepingPlaces + CrowdMargin;
            var discontent = members.Average(v => v.Needs.Morale) < SplitMorale;
            if (!crowded && !discontent) continue;
            var count = members.Length / 2;
            // The discontented leave in conflict; when crowded, the newest arrivals move on.
            var leavers = (discontent ? free.OrderBy(v => v.Needs.Morale).ThenBy(v => v.Id) : free.Reverse())
                .Take(count).Select(v => v.Id).ToArray();
            // A crowded Village with an Outpost settles its overflow there first; the Outpost may grow into a Village.
            if (!discontent && home.Outposts.FirstOrDefault(o => !o.Abandoned) is { } outpost)
            {
                var overflow = leavers.Take(members.Length - home.CoveredSleepingPlaces - CrowdMargin).ToArray();
                _villages[home.Id] = home with
                {
                    LastSplit = _tick,
                    Outposts = [.. home.Outposts.Select(o => o.Id == outpost.Id ? o with { VillagerIds = [.. o.VillagerIds, .. overflow] } : o)],
                };
                foreach (var id in overflow) _villagers.Send(id, outpost.Position);
                continue;
            }
            if (leavers.Length < FoundingGroup) continue;
            if (SplitSite(home) is not { } site)
            {
                _villages[home.Id] = home with { LastSplit = _tick }; // no room nearby: look again in a Store day
                continue;
            }
            Split(clock, home.Id, leavers, site, discontent ? SplitReason.LowMorale : SplitReason.Overcrowded);
        }
    }

    // The best known site far enough from every standing Village, or failing that a reachable spot at that distance.
    private Vector2? SplitSite(Village village)
    {
        var ring = Enumerable.Range(0, 8).Select(i => _navigation.NearestWalkable(_space.Wrap(village.Position +
            new Vector2(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4)) * (SplitDistance + 4))));
        return village.KnownMap.ExploredGround.Concat(ring)
            .Where(p => IsSafeFloor(p) && _villages.All(v => v.Fallen || _space.Distance(v.Position, p) >= SplitDistance))
            .Select(p => (Position: p, Score: SiteScore(p, village.KnownMap)))
            .OrderByDescending(s => s.Score).ThenBy(s => _space.Distance(village.Position, s.Position))
            .Where(s => _navigation.FindPath(village.Position, s.Position) is not null)
            .Select(s => (Vector2?)s.Position).FirstOrDefault();
    }

    private void Split(StoreClock clock, int fromId, IReadOnlyList<int> ids, Vector2 site, SplitReason reason)
    {
        var from = _villages[fromId];
        var share = (double)ids.Count / Math.Max(1, from.VillagerIds.Count);
        // The leavers carry their share of the Goods; Materials, Gear and buildings stay.
        var taken = from.Stores.ToDictionary(g => g.Key, g => (int)(g.Value * share));
        bool Kept(KeyValuePair<GoodsKind, int> g) => g.Value > 0;
        var village = new Village(_villages.Count, site, [.. ids])
        {
            KnownMap = from.KnownMap, Recipes = from.Recipes, Personality = from.Personality, HordeBelief = from.HordeBelief,
            Department = _departments.At(site).Type, LastSplit = _tick, Stores = taken.Where(Kept).ToDictionary().AsReadOnly(),
        };
        _villages[fromId] = _villages[fromId] with
        {
            LastSplit = _tick,
            VillagerIds = [.. from.VillagerIds.Except(ids)],
            Stores = from.Stores.Select(g => KeyValuePair.Create(g.Key, g.Value - taken[g.Key])).Where(Kept).ToDictionary().AsReadOnly(),
        };
        _villages.Add(village);
        _villagers.JoinVillage(village);
        foreach (var id in ids) _villagers.Send(id, site);
        Emit(new VillageSplit(clock, fromId, village, reason));
    }

    // The last standing Village has fallen: the Store enters a new era, and Wanderers keep arriving to found new Villages.
    private void CheckEra(StoreClock clock, int fallenId)
    {
        if (_villages.Any(v => !v.Fallen && v.VillagerIds.Count > 0)) return;
        _era++;
        Emit(new EraBegan(clock, _era, fallenId));
    }
}
