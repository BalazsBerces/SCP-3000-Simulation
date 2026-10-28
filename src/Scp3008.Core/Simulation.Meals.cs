using System.Numerics;

namespace Scp3008.Core;

public sealed record SharedMealServed(StoreClock At, int VillageId, IReadOnlyList<int> VillagerIds,
    int GoodsUsed, bool Cooked) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public void AssignRole(int villagerId, Role role) => _simulation.SetRole(villagerId, role);
    public void SetNeeds(int villagerId, Needs needs) => _simulation.SetNeeds(villagerId, needs);
}

public sealed partial class Simulation
{
    internal void SetNeeds(int id, Needs needs) => Publish(() =>
    {
        ArgumentNullException.ThrowIfNull(needs);
        needs.Validate();
        _villagers.SetNeeds(id, needs);
        return true;
    });

    internal void SetRole(int id, Role role) => Publish(() =>
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        _villagers.AssignRole(id, role);
        return true;
    });

    private void AdvanceMeals(StoreClock clock)
    {
        var start = MealStart(clock);
        if (start < 0 || _tick - start < StoreClock.TicksFor(TimeSpan.FromMinutes(30))) return;
        foreach (var village in _villages.ToArray())
        {
            if (village.MessHall is not { Complete: true } hall || hall.LastMealTick >= start) continue;
            var diners = _villagers.Snapshot().Where(v => v.VillageId == village.Id &&
                v.Action == VillagerAction.GoingToMeal && v.Destination is null &&
                _space.Distance(v.Position, hall.Position) <= 5).ToArray();
            var cooked = diners.Any(v => v.Role == Role.Cook);
            var food = village.Stores.Where(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food && g.Value > 0).ToArray();
            var available = food.Sum(g => g.Value);
            var fed = diners.Take(Math.Min(diners.Length, available * (cooked ? 2 : 1))).ToArray();
            var used = (fed.Length + (cooked ? 1 : 0)) / (cooked ? 2 : 1);
            var remaining = used;
            foreach (var goods in food)
            {
                var amount = Math.Min(remaining, goods.Value);
                if (amount > 0) ChangeStores(village.Id, goods.Key, -amount);
                remaining -= amount;
            }
            var current = _villages[village.Id];
            _villages[village.Id] = current with { MessHall = hall with { LastMealTick = start } };
            if (fed.Length == 0) continue;
            var ids = Array.AsReadOnly(fed.Select(v => v.Id).ToArray());
            _villagers.EatSharedMeal(ids, hall.Position);
            _villagers.ShareKnowledge(ids, MergeMaps(fed.Select(v => v.KnownMap)));
            Emit(new SharedMealServed(clock, village.Id, ids, used, cooked));
        }
    }

    // Half an hour to gather, then a quarter hour at the table; the last meal precedes Closing time.
    internal static long MealStart(StoreClock clock)
    {
        var hour = clock.TimeOfDay.TotalHours;
        var start = MessHall.MealHours.FirstOrDefault(h => hour >= h && hour < h + 0.75, -1);
        return start < 0 ? -1 : clock.Tick - StoreClock.TicksFor(clock.TimeOfDay) + StoreClock.TicksFor(TimeSpan.FromHours(start));
    }
}

internal sealed partial class Villagers
{
    public void SetNeeds(int id, Needs needs)
    {
        var walker = _walkers.Single(w => w.Id == id);
        walker.Needs = needs;
        walker.StarvingHours = walker.ThirstyHours = 0;
    }

    private bool AttendMeal(Walker walker, WorldSnapshot world)
    {
        var start = Simulation.MealStart(world.Clock);
        var village = world.Villages.FirstOrDefault(v => v.Id == walker.VillageId);
        if (start < 0 || village?.MessHall is not { Complete: true } hall || walker.Needs.Thirst >= 0.9 ||
            walker.CarriedAmount + walker.CarriedMaterials > 0 || space.Distance(walker.Position, hall.Position) > 24)
            return false;
        if (hall.LastMealTick >= start) return walker.Action == VillagerAction.SharedMeal;
        if (!village.Stores.Any(g => GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food && g.Value > 0)) return false;
        // A Villager on a trip (an errand, an order, scouting) keeps going and can miss the meal.
        if (walker.Action != VillagerAction.GoingToMeal && (walker.Target is not null || walker.Destination is not null)) return false;
        if (walker.Destination is null && space.Distance(walker.Position, hall.Position) > 5)
        {
            // Test the way first, so an unreachable seat leaves the current route alone.
            var index = village.VillagerIds.ToList().IndexOf(walker.Id);
            var angle = index * MathF.Tau / Math.Max(2, village.VillagerIds.Count);
            var place = navigation.NearestWalkable(space.Wrap(hall.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 2.2f));
            if (navigation.FindPath(walker.Position, place) is not { } path) return false;
            walker.Waypoints = path;
            walker.Destination = place;
        }
        walker.Action = VillagerAction.GoingToMeal;
        return true;
    }

    public void EatSharedMeal(IReadOnlyList<int> ids, Vector2 table)
    {
        foreach (var walker in _walkers.Where(w => ids.Contains(w.Id)))
        {
            walker.Needs = walker.Needs with { Hunger = Math.Max(0, walker.Needs.Hunger - 0.65),
                Morale = Math.Min(1, walker.Needs.Morale + (ids.Count > 1 ? 0.04 * (ids.Count - 1) : 0)) };
            walker.StarvingHours = 0;
            walker.Action = VillagerAction.SharedMeal;
            var delta = space.Delta(walker.Position, table);
            walker.Heading = MathF.Atan2(delta.Y, delta.X);
            if (walker.Role == Role.Cook) walker.Skills[Role.Cook] = walker.Skills.GetValueOrDefault(Role.Cook) + 1;
        }
    }
}
