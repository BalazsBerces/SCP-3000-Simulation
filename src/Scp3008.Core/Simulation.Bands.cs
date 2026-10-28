using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private readonly List<Village> _villages = [];
    private readonly List<string> _chronicle = [];
    private int _nextBandId;

    // Members within this distance of a Village or site have arrived there.
    internal const float ArrivalRadius = 2;
    // A site with both food and water nearby; a Band founds unforced only on one of these.
    private const int GoodSiteScore = 100;
    // A Band this large may found its own Village rather than join one it knows.
    private const int FoundingGroup = 3;

    private void AdvanceVillages(StoreClock clock)
    {
        if (_villages.Count == 0 || _tick % 50 != 1)
            return;
        var people = _villagers.Snapshot();
        foreach (var village in _villages.ToArray())
        {
            var members = people.Where(v => v.VillageId == village.Id).ToArray();
            var known = MergeMaps(members.Select(v => v.KnownMap).Append(village.KnownMap));
            var updated = village with { KnownMap = known, VillagerIds = Array.AsReadOnly(members.Select(v => v.Id).ToArray()) };
            _villages[village.Id] = updated;

            var shortage = village.Stores.Where(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).Sum(g => g.Value) < members.Length;
            // A cook at a built Mess hall stretches the stores, so a shortage keeps them cooking.
            var kitchen = village.MessHall is { Complete: true };
            if (shortage && members.FirstOrDefault(v => v.Role != Role.Scavenger && !(kitchen && v.Role == Role.Cook) &&
                v.FurnitureTaskId is null && !v.Crafting) is { } worker)
            {
                _villagers.AssignRole(worker.Id, Role.Scavenger);
                Emit(new RoleChanged(clock, worker.Id, Role.Scavenger));
            }
            else if (!shortage && members.Length >= 2 && !members.Any(v => v.Role == Role.Builder) &&
                members.LastOrDefault(v => v.Role == Role.Scavenger &&
                    !village.Outposts.Any(o => !o.Abandoned && o.VillagerIds.Contains(v.Id)) && v.FurnitureTaskId is null && !v.Crafting &&
                    v.CarriedAmount + v.CarriedMaterials == 0 && v.Hand?.Kind is null or GearKind.ShoppingCart) is { } builder)
            {
                _villagers.AssignRole(builder.Id, Role.Builder);
                Emit(new RoleChanged(clock, builder.Id, Role.Builder));
            }
            else if (kitchen && !members.Any(v => v.Role == Role.Cook) && members.Count(v => v.Role == Role.Scavenger) >= 2 &&
                members.LastOrDefault(v => v.Role == Role.Scavenger &&
                    !village.Outposts.Any(o => !o.Abandoned && o.VillagerIds.Contains(v.Id)) && v.FurnitureTaskId is null && !v.Crafting &&
                    v.CarriedAmount + v.CarriedMaterials == 0) is { } cook)
            {
                _villagers.AssignRole(cook.Id, Role.Cook);
                Emit(new RoleChanged(clock, cook.Id, Role.Cook));
            }
        }
    }

    private void CreateStartingBands(SeededRandom random)
    {
        var departments = _departments.All.OrderBy(_ => random.NextULong()).ToArray();
        var count = 4 + random.Next(2);
        foreach (var department in departments)
        {
            var position = _navigation.NearestWalkable(department.Anchor);
            if (_departments.At(position).Id != department.Id || _snapshot.GroundAt(position).HasFlag(GroundProperty.Darkness))
                continue;
            var members = Enumerable.Range(0, 2 + random.Next(5))
                .Select(i => _navigation.NearestWalkable(position + new Vector2(i % 3, i / 3) * 2))
                .Where(p => !_snapshot.GroundAt(p).HasFlag(GroundProperty.Darkness)).ToArray();
            if (members.Length < 2)
                continue;
            PlaceBand(members);
            if (_bands.Count == count)
                break;
        }
    }

    private void AdvanceBands(StoreClock clock)
    {
        if (_bands.Count == 0 || _tick % 50 != 1)
            return;
        var people = _villagers.Snapshot();
        foreach (var band in _bands.ToArray())
        {
            var members = people.Where(v => band.VillagerIds.Contains(v.Id)).ToArray();
            if (members.Length == 0)
            {
                _bands.Remove(band);
                continue;
            }
            var known = MergeMaps(members.Select(v => v.KnownMap).Append(band.KnownMap));
            var updated = band with { KnownMap = known, Position = members[0].Position,
                VillagerIds = Array.AsReadOnly(members.Select(v => v.Id).ToArray()) };
            _bands[_bands.IndexOf(band)] = updated;
            _villagers.ShareKnowledge(updated.VillagerIds, known);
            if (clock.Phase == DayPhase.ClosingTime)
                continue;
            var forced = members.Any(v => v.Needs.Rest < 0.2 || v.Needs.Hunger >= 0.85) ||
                members.Length < band.FoundingPopulation;
            // Only the Band's own Known map says where Villages are; whether one still lives is learned on arrival.
            var destination = known.Villages.Where(v => !v.Abandoned)
                .OrderBy(v => _space.Distance(updated.Position, v.Position))
                .FirstOrDefault(v => members.All(m => _navigation.FindPath(m.Position, v.Position) is not null));
            // A group of founders with a good site closer than any known Village builds its own.
            var mayFound = destination is null || !forced && members.Length >= FoundingGroup;
            var site = mayFound ? BestFoundingSite(updated.Position, known, forced) : null;
            var founds = site is { } own && (destination is null ||
                _space.Distance(updated.Position, own) < _space.Distance(updated.Position, destination.Position));
            if (destination is not null && !founds)
            {
                if (members.Any(m => _space.Distance(m.Position, destination.Position) > ArrivalRadius))
                {
                    _bands[_bands.IndexOf(updated)] = updated with { Site = destination.Position };
                    continue;
                }
                if (_villages[destination.VillageId].VillagerIds.Count == 0)
                    continue;
                JoinVillage(clock, updated, _villages[destination.VillageId]);
                continue;
            }
            if (site is not { } position)
                continue;
            if (_space.Distance(updated.Position, position) > ArrivalRadius)
            {
                _bands[_bands.IndexOf(updated)] = updated with { Site = position };
                continue;
            }
            var village = new Village(_villages.Count, position, updated.VillagerIds)
                { KnownMap = known, Recipes = updated.Recipes, Personality = updated.Personality };
            _villages.Add(village);
            _bands.Remove(updated);
            _villagers.JoinVillage(village);
            Emit(new VillageFounded(clock, band.Id, village, forced));
        }
    }

    private void JoinVillage(StoreClock clock, Band band, Village village)
    {
        var joined = village with
        {
            VillagerIds = Array.AsReadOnly(village.VillagerIds.Concat(band.VillagerIds).ToArray()),
            KnownMap = MergeMaps([village.KnownMap, band.KnownMap]),
            Recipes = Array.AsReadOnly(village.Recipes.Union(band.Recipes).ToArray()),
        };
        _villages[joined.Id] = joined;
        _bands.Remove(band);
        _villagers.JoinVillage(joined);
        _villagers.ShareKnowledge(band.VillagerIds, joined.KnownMap);
        Emit(new WanderersJoined(clock, band.Id, joined.Id, band.VillagerIds));
    }

    private Vector2? BestFoundingSite(Vector2 from, KnownMap known, bool forced) =>
        known.ExploredGround.Append(from).Where(IsSafeFloor)
            .Select(p => (Position: p, Score: SiteScore(p, known)))
            .OrderByDescending(s => s.Score).ThenBy(s => _space.Distance(from, s.Position))
            .Where(s => (forced || s.Score >= GoodSiteScore) && _navigation.FindPath(from, s.Position) is not null)
            .Select(s => (Vector2?)s.Position).FirstOrDefault();

    private int SiteScore(Vector2 position, KnownMap known)
    {
        var food = known.Goods.Any(g => g.Goods.Amount > 0 && GoodsCatalog.ClassOf(g.Goods.Kind) == GoodsClass.Food &&
            _space.Distance(position, g.Position) <= 48);
        var water = known.Water.Any(w => w.Running == true && _space.Distance(position, w.Position) <= 48);
        return (food && water ? GoodSiteScore : food || water ? 30 : 0) +
            _villagers.FurnitureNear(position, 24).Where(p => _space.Distance(position, p.Position) <= 24).Take(10).Count() +
            (_pillars.Any(p => _space.Distance(position, p.Position) <= 48) ? 20 : 0);
    }

    private static KnownMap MergeMaps(IEnumerable<KnownMap> maps)
    {
        var all = maps.ToArray();
        return new KnownMap
        {
            ExploredGround = Array.AsReadOnly(all.SelectMany(m => m.ExploredGround).Distinct().ToArray()),
            Goods = Array.AsReadOnly(all.SelectMany(m => m.Goods).GroupBy(g => (g.Goods.FurnitureId, g.Goods.Kind, g.Goods.LeftBehind))
                .Select(g => g.MaxBy(x => x.LastSeen.Tick)!).ToArray()),
            Water = Array.AsReadOnly(all.SelectMany(m => m.Water).GroupBy(w => (w.FurnitureId, w.Position))
                .Select(g => g.OrderByDescending(w => w.Running.HasValue).ThenByDescending(w => w.LastSeen.Tick).First()).ToArray()),
            Dangers = Array.AsReadOnly(all.SelectMany(m => m.Dangers).GroupBy(d => d.StaffId)
                .Select(g => g.MaxBy(x => x.LastSeen.Tick)!).ToArray()),
            Villages = Array.AsReadOnly(all.SelectMany(m => m.Villages).GroupBy(v => v.VillageId)
                .Select(g => g.MaxBy(x => x.LastSeen.Tick)!).ToArray()),
        };
    }
}

public sealed partial class ScenarioBuilder
{
    /// <summary>Places Wanderers, optionally carrying observations and a fallen Village's Recipes.</summary>
    public Band PlaceBand(IReadOnlyList<Vector2> positions, Needs? needs = null,
        KnownMap? knownMap = null, IReadOnlyList<GearKind>? recipes = null, Personality? personality = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (personality is { } temperament && !Enum.IsDefined(temperament) || positions.Count == 0 || positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new ArgumentException("A Band needs members at finite positions.", nameof(positions));
        if (recipes is not null && recipes.Any(k => !Recipes.All.Any(r => r.Gear == k)))
            throw new ArgumentException("Unknown Recipe.", nameof(recipes));
        return _simulation.PlaceBand(positions, needs, knownMap, recipes, personality);
    }

    /// <summary>Places a known kind of running water source for a scenario.</summary>
    public WaterPoint PlaceWaterPoint(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentException("A Water point needs a finite position.", nameof(position));
        return _simulation.AddWaterPoint(position);
    }
}
