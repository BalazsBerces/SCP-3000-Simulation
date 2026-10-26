using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private readonly List<Village> _villages = [];
    private readonly List<string> _chronicle = [];
    private int _nextBandId;

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
            _villagers.ShareKnowledge(updated.VillagerIds, known);
            var shortage = village.Stores.Where(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).Sum(g => g.Value) < members.Length;
            if (shortage && members.FirstOrDefault(v => v.Role != Role.Scavenger) is { } worker)
            {
                _villagers.AssignRole(worker.Id, Role.Scavenger);
                Emit(new RoleChanged(clock, worker.Id, Role.Scavenger));
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
            var candidates = known.ExploredGround.Append(updated.Position)
                .Where(p => !_snapshot.GroundAt(p).HasFlag(GroundProperty.Darkness) && _navigation.IsWalkable(p))
                .Select(p => (Position: p, Score: SiteScore(p, known)))
                .OrderByDescending(s => s.Score).ThenBy(s => _space.Distance(updated.Position, s.Position));
            var sites = candidates.Where(s => (forced || s.Score >= 100) &&
                _navigation.FindPath(updated.Position, s.Position) is not null).Take(1).ToArray();
            if (sites.Length == 0)
                continue;
            var site = sites[0];
            if (_space.Distance(updated.Position, site.Position) > 2)
            {
                _bands[_bands.IndexOf(updated)] = updated with { Site = site.Position };
                foreach (var member in members)
                    _villagers.Send(member.Id, site.Position);
                continue;
            }
            var village = new Village(_villages.Count, site.Position, updated.VillagerIds) { KnownMap = known };
            _villages.Add(village);
            _bands.Remove(updated);
            _villagers.JoinVillage(village);
            Emit(new VillageFounded(clock, band.Id, village, forced));
        }
    }

    private int SiteScore(Vector2 position, KnownMap known)
    {
        var food = known.Goods.Any(g => g.Goods.Amount > 0 && GoodsCatalog.ClassOf(g.Goods.Kind) == GoodsClass.Food &&
            _space.Distance(position, g.Position) <= 48);
        var water = known.Water.Any(w => w.Running == true && _space.Distance(position, w.Position) <= 48);
        return (food && water ? 100 : food || water ? 30 : 0) +
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
        };
    }
}

public sealed partial class ScenarioBuilder
{
    public Band PlaceBand(IReadOnlyList<Vector2> positions, Needs? needs = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Count == 0 || positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new ArgumentException("A Band needs members at finite positions.", nameof(positions));
        return _simulation.PlaceBand(positions, needs);
    }

    /// <summary>Places a known kind of running water source for a scenario.</summary>
    public WaterPoint PlaceWaterPoint(Vector2 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentException("A Water point needs a finite position.", nameof(position));
        return _simulation.AddWaterPoint(position);
    }
}
