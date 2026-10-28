namespace Scp3008.Core;

internal sealed partial class Villagers
{
    public void AssignRole(int id, Role role)
    {
        var walker = _walkers.Single(w => w.Id == id);
        walker.Role = role;
        walker.Skills.TryAdd(role, 0);
    }

    private bool CommunityWork(Walker walker, WorldSnapshot world, Action<int, int, GoodsKind> deliver)
    {
        var village = world.Villages.FirstOrDefault(v => v.Id == walker.VillageId);
        if (village is null)
            return false;
        if (walker.CarriedGoods is { } cargo)
        {
            walker.Action = VillagerAction.CarryingGoods;
            if (space.Distance(walker.Position, village.Position) < 0.5f)
            {
                for (var i = 0; i < walker.CarriedAmount; i++)
                    deliver(walker.Id, village.Id, cargo);
                walker.CarriedAmount = 0;
                if (walker.Bag is { } bag) walker.Bag = Wear(bag, 1);
                if (walker.Hand is { Kind: GearKind.ShoppingCart or GearKind.PlatformCart } cart) walker.Hand = Wear(cart, 1);
                walker.CarriedGoods = null;
                walker.Skills[Role.Scavenger] = walker.Skills.GetValueOrDefault(Role.Scavenger) + 1;
            }
            else if (walker.Destination is null)
                Plan(walker, village.Position);
            return true;
        }
        var outpost = village.Outposts.FirstOrDefault(o => !o.Abandoned && o.VillagerIds.Contains(walker.Id));
        if (world.Phase == DayPhase.ClosingTime)
        {
            if (outpost is not null && walker.Destination is null && space.Distance(walker.Position, outpost.Position) > Simulation.ArrivalRadius)
                Plan(walker, outpost.Position);
            walker.Action = VillagerAction.Resting;
            return true;
        }
        if (walker.Role != Role.Scavenger && outpost is null)
            return false;
        if (walker.Destination is not null)
            return true;
        var foodNeeded = village.Stores.Where(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).Sum(g => g.Value) < village.VillagerIds.Count * 2;
        var sources = walker.KnownGoods.Values.Where(g => (g.Goods.Amount > 0 || g.Stale) &&
            (outpost is null || space.Distance(outpost.Position, g.Position) <= 24) &&
            (!foodNeeded || GoodsCatalog.ClassOf(g.Goods.Kind) == GoodsClass.Food) && !walker.Unreachable.Contains(g.Position));
        var chosen = sources.OrderBy(g => space.Distance(walker.Position, g.Position))
            .Select(g => new NeedTarget(g.Position, g.Goods, Haul: true)).FirstOrDefault(t => TryPlan(walker, t));
        if (chosen is not null)
        {
            walker.Target = chosen;
            walker.Action = VillagerAction.Scavenging;
        }
        else
            Scout(walker);
        return true;
    }

    public void ShareKnowledge(IReadOnlyList<int> ids, KnownMap map)
    {
        foreach (var walker in _walkers.Where(w => ids.Contains(w.Id)))
        {
            walker.ExploredGround.UnionWith(map.ExploredGround);
            foreach (var goods in map.Goods)
                walker.KnownGoods[(goods.Goods.FurnitureId, goods.Goods.Kind, goods.Goods.LeftBehind)] = goods;
            foreach (var water in map.Water)
                walker.KnownWater[(water.FurnitureId, water.Position)] = water;
            foreach (var danger in map.Dangers)
                walker.KnownDangers[danger.StaffId] = danger;
            foreach (var village in map.Villages)
                walker.KnownVillages[village.VillageId] = village;
        }
    }

    public void JoinVillage(Village village)
    {
        var index = 0;
        foreach (var walker in _walkers.Where(w => village.VillagerIds.Contains(w.Id)))
        {
            if (walker.VillageId == village.Id)
                continue;
            walker.VillageId = village.Id;
            walker.Role = (Role)(index++ % 7);
            walker.Skills.TryAdd(walker.Role, 0);
        }
    }
}
