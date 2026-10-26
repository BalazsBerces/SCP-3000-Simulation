using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Villagers
{
    public bool HasVillagers => _walkers.Count > 0;
    public List<Body> Bodies { get; } = [];

    private sealed record NeedTarget(Vector2 Position, StockedGoods? Goods = null, WaterPoint? Water = null, bool Rest = false, bool Haul = false);

    private sealed partial class Walker
    {
        public double StarvingHours { get; set; }
        public double ThirstyHours { get; set; }
        public double NextDecision { get; set; }
        public NeedTarget? Target { get; set; }
        public HashSet<Vector2> Unreachable { get; } = [];
    }

    public void AdvanceNeeds(double hours, WorldSnapshot world, NeedsConfig config,
        Func<int, StockedGoods, StoreClock, bool> take, Action<DomainEvent> emit,
        Func<StockedGoods, bool> collect, Action<int, int, GoodsKind> deliver)
    {
        IndexFurniture(world.Furniture);
        foreach (var walker in _walkers.ToArray())
        {
            var n = walker.Needs;
            var henge = world.PointsOfInterest.Any(p => p.Type == PointOfInterestType.ArmchairHenge &&
                MathF.Abs(space.Delta(p.Centre, walker.Position).X) <= p.Size.X / 2 &&
                MathF.Abs(space.Delta(p.Centre, walker.Position).Y) <= p.Size.Y / 2);
            var danger = world.Phase == DayPhase.ClosingTime
                ? world.Staff.OrderBy(s => space.Distance(walker.Position, s.Position)).FirstOrDefault() : null;
            var threatened = danger is not null && space.Distance(walker.Position, danger.Position) < 12;
            var safety = threatened ? 0 : world.Phase == DayPhase.ClosingTime ? 0.35 : 1;
            walker.Needs = n with
            {
                Hunger = Math.Min(1, n.Hunger + hours / 24),
                Thirst = Math.Min(1, n.Thirst + hours / 12),
                Rest = Math.Clamp(n.Rest + hours * (walker.Action == VillagerAction.Resting ? (henge ? 0.25 : 0.125) : -1.0 / 24), 0, 1),
                Safety = safety,
                Morale = Math.Clamp(n.Morale - hours * (safety < 0.5 ? 1.0 / 48 : 1.0 / 240), 0, 1),
            };
            walker.StarvingHours = n.Hunger >= 1 ? walker.StarvingHours + hours : 0;
            walker.ThirstyHours = n.Thirst >= 1 ? walker.ThirstyHours + hours : 0;
            if (walker.StarvingHours + 1e-9 >= config.StarvationHours || walker.ThirstyHours + 1e-9 >= config.DehydrationHours)
            {
                var body = new Body(walker.Id, walker.Position, walker.Heading, world.Clock,
                    walker.ThirstyHours + 1e-9 >= config.DehydrationHours ? DeathCause.Thirst : DeathCause.Starvation);
                Bodies.Add(body);
                _walkers.Remove(walker);
                emit(new VillagerDied(world.Clock, body));
                continue;
            }
            walker.NextDecision -= hours;
            if (walker.NextDecision > 0)
                continue;
            walker.NextDecision = 0.1;
            Observe(walker, world);
            n = walker.Needs;
            if (world.Phase == DayPhase.ClosingTime && world.Bands.Any(b => b.VillagerIds.Contains(walker.Id)))
            {
                walker.Target = null;
                walker.Waypoints.Clear();
                walker.Destination = null;
                walker.Action = VillagerAction.Resting;
                continue;
            }
            if (threatened && n.Hunger < 0.9 && n.Thirst < 0.9)
            {
                walker.Target = null;
                Plan(walker, navigation.NearestWalkable(walker.Position + Vector2.Normalize(space.Delta(danger!.Position, walker.Position) + new Vector2(0.001f)) * 16));
                walker.Action = VillagerAction.SeekingSafety;
                continue;
            }
            if (walker.Target is { } target)
            {
                if (walker.Destination is not null)
                    continue;
                walker.Target = null;
                if (space.Distance(walker.Position, navigation.NearestWalkable(target.Position)) < 0.1f)
                {
                    if (target.Haul && target.Goods is { } cargo)
                    {
                        if (world.Furniture.Any(p => p.Id == cargo.FurnitureId && p.Position == target.Position) && collect(cargo))
                            walker.CarriedGoods = cargo.Kind;
                    }
                    else if (target.Goods is { } meal &&
                        (meal.FurnitureId == -1 || world.Furniture.Any(p => p.Id == meal.FurnitureId && p.Position == target.Position)) &&
                        take(walker.Id, meal, world.Clock))
                    {
                        var kind = GoodsCatalog.ClassOf(meal.Kind);
                        walker.Needs = kind switch
                        {
                            GoodsClass.Food => n with { Hunger = Math.Max(0, n.Hunger - 0.65) },
                            GoodsClass.Drink => n with { Thirst = Math.Max(0, n.Thirst - 0.8) },
                            _ => n with { Morale = Math.Min(1, n.Morale + 0.35) },
                        };
                        if (kind == GoodsClass.Food) walker.StarvingHours = 0;
                        if (kind == GoodsClass.Drink) walker.ThirstyHours = 0;
                        walker.Action = kind == GoodsClass.Food ? VillagerAction.Eating : kind == GoodsClass.Drink ? VillagerAction.Drinking : VillagerAction.UsingComfort;
                        continue;
                    }
                    if (target.Water is { } point)
                    {
                        point = world.WaterPoints.FirstOrDefault(w => w.FurnitureId == point.FurnitureId && w.Position == point.Position);
                        var key = (target.Water.Value.FurnitureId, target.Position);
                        if (walker.KnownWater.TryGetValue(key, out var known))
                            walker.KnownWater[key] = known with { Running = point.Running, LastSeen = world.Clock, Stale = false };
                        if (point.Running)
                        {
                            walker.Needs = n with { Thirst = 0 };
                            walker.ThirstyHours = 0;
                            walker.Action = VillagerAction.Drinking;
                            emit(new WaterDrunk(world.Clock, walker.Id, point));
                            continue;
                        }
                    }
                    if (target.Rest)
                        walker.Action = VillagerAction.Resting;
                }
            }
            if (walker.Action == VillagerAction.Resting && n.Rest < 0.95 && n.Hunger < 0.75 && n.Thirst < 0.75)
                continue;
            var priorities = new[] { (GoodsClass.Drink, n.Thirst), (GoodsClass.Food, n.Hunger), (GoodsClass.Comfort, 1 - n.Morale) }
                .Where(p => p.Item2 >= 0.5).OrderByDescending(p => p.Item2);
            NeedTarget? chosen = null;
            foreach (var (kind, _) in priorities)
            {
                var candidates = walker.KnownGoods.Values
                    .Where(g => GoodsCatalog.ClassOf(g.Goods.Kind) == kind && (g.Goods.Amount > 0 || g.Stale))
                    .Select(g => new NeedTarget(g.Position, g.Goods)).ToList();
                var home = world.Villages.FirstOrDefault(v => v.Id == walker.VillageId);
                if (home is not null)
                    candidates.AddRange(home.Stores.Where(g => g.Value > 0 && GoodsCatalog.ClassOf(g.Key) == kind)
                        .Select(g => new NeedTarget(home.Position, new StockedGoods(-1, g.Key, g.Value))));
                if (kind == GoodsClass.Drink)
                    candidates.AddRange(walker.KnownWater.Values.Where(w => w.Running != false)
                        .Select(w => new NeedTarget(w.Position, Water: new WaterPoint(w.Kind, w.Position, w.Running == true, w.FurnitureId))));
                chosen = candidates.Where(t => !walker.Unreachable.Contains(t.Position))
                    .OrderBy(t => space.Distance(walker.Position, t.Position)).FirstOrDefault(t => TryPlan(walker, t));
                if (chosen is not null)
                {
                    walker.Action = kind == GoodsClass.Drink ? VillagerAction.SeekingDrink : kind == GoodsClass.Food ? VillagerAction.SeekingFood : VillagerAction.SeekingComfort;
                    break;
                }
            }
            if (chosen is not null)
                walker.Target = chosen;
            else if (n.Rest >= 0.4 && CommunityWork(walker, world, deliver))
                continue;
            else if (n.Rest < 0.4)
            {
                var places = world.PointsOfInterest.Where(p => p.Type == PointOfInterestType.ArmchairHenge)
                    .Select(p => new NeedTarget(p.Centre, Rest: true))
                    .Concat(world.Furniture.Where(p => p.Type is FurnitureType.Bed or FurnitureType.Sofa or FurnitureType.Armchair)
                        .Select(p => new NeedTarget(p.Position, Rest: true)));
                walker.Target = places.Where(t => space.Distance(walker.Position, t.Position) <= 30 && !walker.Unreachable.Contains(t.Position))
                    .OrderBy(t => space.Distance(walker.Position, t.Position)).FirstOrDefault(t => TryPlan(walker, t));
                walker.Action = walker.Target is null ? VillagerAction.Resting : VillagerAction.Idle;
            }
            else if (walker.Destination is null)
            {
                walker.Action = VillagerAction.Idle;
                if (n.Hunger >= 0.5 || n.Thirst >= 0.5 || world.Bands.Any(b => b.VillagerIds.Contains(walker.Id)))
                    Scout(walker);
            }
        }
    }

    private bool TryPlan(Walker walker, NeedTarget target)
    {
        Plan(walker, navigation.NearestWalkable(target.Position));
        if (walker.Destination is not null)
            return true;
        walker.Unreachable.Add(target.Position);
        return false;
    }
}
