using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private void AdvanceOutposts(StoreClock clock)
    {
        if (_tick % 50 != 1) return;
        var people = _villagers.Snapshot();
        foreach (var original in _villages.ToArray())
        {
            var village = original;
            bool HasFood(Vector2 site, float radius, int minimum) => village.KnownMap.Goods
                .Where(g => GoodsCatalog.ClassOf(g.Goods.Kind) == GoodsClass.Food && _space.Distance(site, g.Position) <= radius)
                .Sum(g => g.Goods.Amount) >= minimum;
            foreach (var post in village.Outposts.Where(o => !o.Abandoned).ToArray())
            {
                var residents = post.VillagerIds.Where(id => people.Any(p => p.Id == id && p.VillageId == village.Id)).ToArray();
                var lost = residents.Length == 0 || !HasFood(post.Position, 24, 1);
                var updated = post with { VillagerIds = Array.AsReadOnly(residents), Abandoned = lost };
                village = village with { Outposts = Array.AsReadOnly(village.Outposts.Select(o => o.Id == post.Id ? updated : o).ToArray()) };
                _villages[village.Id] = village;
                if (lost)
                {
                    foreach (var id in residents) _villagers.Send(id, village.Position);
                    Emit(new OutpostAbandoned(clock, village.Id, post.Id));
                }
            }
            if (clock.Phase == DayPhase.ClosingTime || village.VillagerIds.Count < 6 ||
                village.Outposts.Any(o => !o.Abandoned) || HasFood(village.Position, 48, village.VillagerIds.Count * 2)) continue;
            var source = village.KnownMap.Goods.Where(g => GoodsCatalog.ClassOf(g.Goods.Kind) == GoodsClass.Food &&
                    g.Goods.Amount >= village.VillagerIds.Count * 2 && _space.Distance(village.Position, g.Position) > 48)
                .OrderBy(g => _space.Distance(village.Position, g.Position)).FirstOrDefault();
            if (source is null) continue;
            var position = _navigation.NearestWalkable(_space.Wrap(source.Position + new Vector2(0, 6)));
            var workers = people.Where(p => p.VillageId == village.Id && p.Role == Role.Scavenger &&
                IsAvailableWorker(p) && _navigation.FindPath(p.Position, position) is not null)
                .Take(2).Select(p => p.Id).ToArray();
            if (workers.Length < 2) continue;
            var barricade = CreateBarricade(new Village(village.Id, position, Array.AsReadOnly(workers)));
            var outpost = new Outpost(village.Outposts.Count, position, village.Walls.Count, Array.AsReadOnly(workers));
            _villages[village.Id] = village with
            {
                Outposts = Array.AsReadOnly(village.Outposts.Append(outpost).ToArray()),
                Walls = Array.AsReadOnly(village.Walls.Append(barricade).ToArray()),
            };
            Emit(new OutpostFounded(clock, village.Id, outpost));
        }
    }

    private void ValidateOutposts()
    {
        foreach (var village in _villages)
            if (village.Outposts.Select(o => o.Id).Distinct().Count() != village.Outposts.Count ||
                village.Outposts.Select(o => o.WallIndex).Distinct().Count() != village.Outposts.Count ||
                village.Outposts.Where(o => !o.Abandoned).SelectMany(o => o.VillagerIds).Distinct().Count() !=
                    village.Outposts.Where(o => !o.Abandoned).Sum(o => o.VillagerIds.Count) ||
                village.Outposts.Any(o => o.Id < 0 || o.Id >= village.Outposts.Count ||
                    !float.IsFinite(o.Position.X) || !float.IsFinite(o.Position.Y) ||
                    o.WallIndex < 0 || o.WallIndex >= village.Walls.Count ||
                    o.VillagerIds.Distinct().Count() != o.VillagerIds.Count ||
                    !o.Abandoned && (o.VillagerIds.Count == 0 || o.VillagerIds.Any(id => !village.VillagerIds.Contains(id)))))
                throw new InvalidDataException("Invalid world save: inconsistent Outposts.");
    }
}
