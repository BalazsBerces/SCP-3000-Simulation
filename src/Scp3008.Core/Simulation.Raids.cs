using System.Collections.ObjectModel;
using System.Numerics;

namespace Scp3008.Core;

/// <summary>What a Raid is after: stores, a held site (territory) or Captives.</summary>
public enum RaidGoal { Stores, Site, Captives }

/// <summary>The kinds of territory a Village holds.</summary>
public enum HoldingKind { Pillar, FountainCourt, Cafeteria, StockShelf }

/// <summary>
/// A place a Village holds as its territory: a Pillar, the fountain court, a Cafeteria or a Store shelf it guards for its food.
/// A Village takes the unheld ones near it; after that they change hands only in Raids.
/// </summary>
/// <param name="SiteId">The Pillar, Landmark, Point of interest or Stock Furniture id.</param>
public sealed record Holding(HoldingKind Kind, int SiteId, Vector2 Position);

/// <summary>A Villager seized in a Raid, a member of the captors' Village but held apart since <see cref="Since"/> (tick) until absorbed.</summary>
public sealed record Captive(int VillagerId, int FromVillageId, long Since);

/// <summary>Villagers of one Village walking to another to seize its stores, a site it holds or Captives, then home with what they took.</summary>
/// <param name="Site">The holding a <see cref="RaidGoal.Site"/> Raid goes for.</param>
/// <param name="Next">The Route waypoint the Raiders are walking to.</param>
/// <param name="Returning">Whether they have struck and are going home.</param>
public sealed record Raid(int Id, int FromVillageId, int ToVillageId, RaidGoal Goal, Holding? Site, IReadOnlyList<int> VillagerIds,
    IReadOnlyList<Vector2> Route, int Next, bool Returning)
{
    /// <summary>Villagers seized and walking home with the Raiders.</summary>
    public IReadOnlyList<int> CaptiveIds { get; init; } = [];
    /// <summary>The Goods and Materials carried home.</summary>
    public CaravanCargo Loot { get; init; } = new();
}

public sealed record RaidLaunched(StoreClock At, int RaidId, int FromVillageId, int ToVillageId, RaidGoal Goal) : DomainEvent(At);
/// <summary>
/// Raiders struck their target. Units taken from each place (an outside Furniture yard's pieces count as the Materials they yield);
/// <see cref="Hurt"/> is how many on both sides were hit (wounded, or Taken if already wounded).
/// </summary>
public sealed record RaidStruck(StoreClock At, int RaidId, int FromVillageId, int ToVillageId, RaidGoal Goal, bool Success,
    int FromYard, int FromPile, int FromStorehouse, IReadOnlyList<int> CaptiveIds, Holding? Site, int Hurt) : DomainEvent(At);
public sealed record RaidReturned(StoreClock At, int RaidId, int VillageId) : DomainEvent(At);
public sealed record CaptiveAbsorbed(StoreClock At, int VillagerId, int VillageId, int FromVillageId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Sends Raiders (the Village's free, unwounded people, one staying home) against another Village; a Site Raid names a holding of the target's.</summary>
    public Raid SendRaid(int fromVillageId, int toVillageId, RaidGoal goal, Holding? site = null) =>
        _simulation.SendRaid(fromVillageId, toVillageId, goal, site);
}

/// <summary>Raids between Villages (stores, territory, Captives), the sites Villages hold and Captives absorbed into their captors' Village.</summary>
public sealed partial class Simulation
{
    private const int RaidEvery = 5, RaidDispatchEvery = 500, HoldingEvery = 500, CaptiveEvery = 50;
    private const int RaidMembers = 4, RaidLoadPerRaider = 5, HeldShelves = 3;
    /// <summary>Ticks a Captive is held before becoming one of the captors' own: half a Store day.</summary>
    public const int CaptiveAbsorbTicks = 6000;
    private const float HoldRange = 40, ShelfRange = 20, DefendRange = 40, NearSite = 20;
    // How much each defender counts where Raiders strike: an outside Furniture yard is far from them, the open pile unsheltered,
    // the Storehouse (and the people) behind them; walls double the inner guard.
    private const double YardGuard = 0.25, PileGuard = 0.5, FarSiteGuard = 0.5;
    private readonly List<Raid> _raids = [];
    private readonly SeededRandom _raidRandom;
    private int _nextRaidId;

    /// <summary>The chance a Village with a known neighbour sends a Raid at each dispatch, by its Personality.</summary>
    public static float RaidChance(Personality personality) => personality switch
    {
        Personality.Raiders => 0.5f,
        Personality.Bold => 0.05f,
        Personality.Isolationists or Personality.Builders or Personality.Cautious => 0.01f,
        _ => 0,
    };

    internal Raid SendRaid(int fromId, int toId, RaidGoal goal, Holding? site) => Publish(() =>
    {
        if (fromId == toId || fromId < 0 || toId < 0 || fromId >= _villages.Count || toId >= _villages.Count || !Enum.IsDefined(goal))
            throw new ArgumentException("A Raid goes from one Village against another.");
        if ((goal == RaidGoal.Site) != (site is not null) || site is not null && !_villages[toId].Holdings.Contains(site))
            throw new ArgumentException("A Site Raid goes for a site the target Village holds.");
        return LaunchRaid(new StoreClock(_tick), fromId, toId, goal, site) ?? throw new InvalidOperationException("No free Villagers to raid with.");
    });

    private Villager[] RaidParty(Village from)
    {
        var able = from.VillagerIds.Where(id => !_wounds.ContainsKey(id) && from.Captives.All(c => c.VillagerId != id))
            .Select(_villagers.ViewOf).OfType<Villager>().Where(p => IsAvailableWorker(p) && p.Action != VillagerAction.Trading).ToArray();
        return able.Take(Math.Min(RaidMembers, able.Length - 1)).ToArray(); // one always stays home
    }

    private Raid? LaunchRaid(StoreClock clock, int fromId, int toId, RaidGoal goal, Holding? site)
    {
        var party = RaidParty(_villages[fromId]);
        if (party.Length == 0) return null;
        var route = CaravanRoute(_villages[fromId].Position, site is null ? _villages[toId].Position : _navigation.NearestWalkable(site.Position));
        var raid = new Raid(_nextRaidId++, fromId, toId, goal, site, Array.AsReadOnly(party.Select(p => p.Id).ToArray()), route, 0, false);
        _raids.Add(raid);
        foreach (var p in party) _villagers.Engage(p.Id, VillagerAction.Raiding, route[0]);
        Emit(new RaidLaunched(clock, raid.Id, fromId, toId, goal));
        return raid;
    }

    private void AdvanceRaids(StoreClock clock)
    {
        if (_tick % HoldingEvery == 13) ClaimHoldings();
        if (_tick % RaidDispatchEvery == 11) DispatchRaids(clock);
        if (_tick % CaptiveEvery == 17) AbsorbCaptives(clock);
        if (_raids.Count == 0 || _tick % RaidEvery != 3) return;
        foreach (var raid in _raids.ToArray())
        {
            var here = raid with
            {
                VillagerIds = Array.AsReadOnly(raid.VillagerIds.Where(id => _villagers.ViewOf(id) is not null).ToArray()),
                CaptiveIds = Array.AsReadOnly(raid.CaptiveIds.Where(id => _villagers.ViewOf(id) is not null).ToArray()),
            };
            // All the Raiders Taken: what they carried is lost, their Captives are held where they stand.
            if (here.VillagerIds.Count == 0) { EndRaid(here with { Loot = new() }, clock); continue; }
            var leader = _villagers.ViewOf(here.VillagerIds[0])!;
            var last = here.Next == here.Route.Count - 1;
            var reached = _space.Distance(leader.Position, here.Route[here.Next]) <= (last ? DestinationReach : WaypointReach);
            if (!reached && leader.Destination is not null) { _raids[_raids.FindIndex(r => r.Id == raid.Id)] = here; continue; }
            if (!last)
            {
                here = here with { Next = here.Next + 1 };
                foreach (var id in here.VillagerIds.Concat(here.CaptiveIds)) _villagers.Engage(id, VillagerAction.Raiding, here.Route[here.Next]);
                _raids[_raids.FindIndex(r => r.Id == raid.Id)] = here;
            }
            else if (here.Returning) EndRaid(here, clock);
            else Strike(here, clock, reached);
        }
    }

    // Villages hold the unheld Pillars, fountain courts, Cafeterias and nearest food shelves close to them.
    private void ClaimHoldings()
    {
        var held = _villages.SelectMany(v => v.Holdings).Select(h => (h.Kind, h.SiteId)).ToHashSet();
        foreach (var village in _villages.ToArray())
        {
            if (village.Fallen || village.VillagerIds.Count == 0) continue;
            bool Near(Vector2 p, float range) => _space.Distance(village.Position, p) <= range;
            var shelves = _stock.Where(g => g.Amount > 0 && GoodsCatalog.ClassOf(g.Kind) == GoodsClass.Food).Select(g => g.FurnitureId).Distinct()
                .Select(id => _furniture.Pieces.FirstOrDefault(p => p.Id == id)).OfType<FurniturePiece>()
                .Where(p => Near(p.Position, ShelfRange)).OrderBy(p => _space.Distance(village.Position, p.Position)).Take(HeldShelves);
            var claims = _pillars.Where(p => Near(p.Position, HoldRange)).Select(p => new Holding(HoldingKind.Pillar, p.Id, p.Position))
                .Concat(_landmarks.Where(l => l.Type == LandmarkType.FountainCourt && Near(l.Centre, HoldRange))
                    .Select(l => new Holding(HoldingKind.FountainCourt, l.Id, l.Centre)))
                .Concat(_pointsOfInterest.Where(p => p.Type == PointOfInterestType.Cafeteria && Near(p.Centre, HoldRange))
                    .Select(p => new Holding(HoldingKind.Cafeteria, p.Id, p.Centre)))
                .Concat(shelves.Select(p => new Holding(HoldingKind.StockShelf, p.Id, p.Position)))
                .Where(h => held.Add((h.Kind, h.SiteId))).ToArray();
            if (claims.Length > 0) _villages[village.Id] = village with { Holdings = Array.AsReadOnly(village.Holdings.Concat(claims).ToArray()) };
        }
    }

    // Villages raid a known neighbour as their Personality inclines them, while the Store is open:
    // the hungry go for a food site, the outnumbered for people, the rest for stores.
    private void DispatchRaids(StoreClock clock)
    {
        if (clock.Phase == DayPhase.ClosingTime) return;
        foreach (var from in _villages.ToArray())
        {
            if (from.Fallen || RaidChance(from.Personality) == 0 || _raids.Any(r => r.FromVillageId == from.Id)) continue;
            var known = from.KnownMap.Villages.Where(v => !v.Abandoned).Select(v => v.VillageId).ToHashSet();
            var target = _villages.Where(v => v.Id != from.Id && !v.Fallen && v.VillagerIds.Count > 0 && known.Contains(v.Id))
                .OrderBy(v => _space.Distance(from.Position, v.Position)).FirstOrDefault();
            if (target is null || RaidParty(from).Length == 0 || !_raidRandom.Chance(RaidChance(from.Personality))) continue;
            var food = target.Holdings.FirstOrDefault(h => h.Kind is not HoldingKind.Pillar);
            if (from.FamineTicks > 0 && food is not null) LaunchRaid(clock, from.Id, target.Id, RaidGoal.Site, food);
            else LaunchRaid(clock, from.Id, target.Id, from.VillagerIds.Count < target.VillagerIds.Count ? RaidGoal.Captives : RaidGoal.Stores, null);
        }
    }

    private void Strike(Raid raid, StoreClock clock, bool reached)
    {
        var target = _villages[raid.ToVillageId];
        var raiders = raid.VillagerIds.Where(id => !_wounds.ContainsKey(id)).ToArray();
        // Everyone at home stands against them; the unwounded fight, the wounded only suffer.
        var present = (target.Fallen || !reached ? [] : target.VillagerIds.Where(id => target.Captives.All(c => c.VillagerId != id) &&
            _villagers.ViewOf(id) is { } p && p.Action is not (VillagerAction.Trading or VillagerAction.Raiding) &&
            _space.Distance(p.Position, target.Position) <= DefendRange)).OrderBy(_wounds.ContainsKey).ToArray();
        double defenders = present.Count(id => !_wounds.ContainsKey(id));
        var inner = target.HomeWalls().Any(w => w.Complete) ? 2 : 1;
        int fromYard = 0, fromPile = 0, fromStorehouse = 0;
        var goods = new Dictionary<GoodsKind, int>();
        var materials = new Dictionary<MaterialKind, int>();
        var captives = new List<int>();
        Holding? seized = null;
        var won = reached;
        var hardest = -1.0; // the strongest guard beaten; none yet
        // Raiders take each place in turn while they beat its guard; the first they cannot beat drives them off.
        bool Beat(double guard)
        {
            if (raiders.Length <= guard) return won = false;
            hardest = Math.Max(hardest, guard);
            return true;
        }
        if (reached)
            switch (raid.Goal)
            {
                case RaidGoal.Stores:
                    var room = raiders.Length * RaidLoadPerRaider;
                    if (target.FurnitureYard is { Inside: false } yard && Beat(defenders * YardGuard))
                    {
                        // Each Raider carries off one piece and breaks it up for its Materials.
                        var pieces = _furniture.Pieces.Where(p => _space.Distance(yard.Position, p.Position) <= FurnitureYard.Radius &&
                            !IsBuiltFurniture(p.Id) && !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).Take(room / RaidLoadPerRaider).Select(p => p.Id).ToHashSet();
                        foreach (var piece in _furniture.Pieces.Where(p => pieces.Contains(p.Id)).ToArray())
                            foreach (var (kind, amount) in FurnitureResources.Yield(piece))
                            {
                                materials[kind] = materials.GetValueOrDefault(kind) + amount;
                                fromYard += amount;
                            }
                        if (pieces.Count > 0) RemoveFurniture(p => pieces.Contains(p.Id));
                        room -= pieces.Count * RaidLoadPerRaider;
                    }
                    if (room > 0 && won && target.PileTotal > 0 && Beat(defenders * PileGuard))
                        foreach (var (kind, amount) in target.PileGoods())
                            fromPile += Take(kind, Math.Min(amount, room - fromPile));
                    room -= fromPile;
                    if (room > 0 && won && target.Stores.Values.Sum() > 0 && Beat(defenders * inner))
                        foreach (var (kind, amount) in _villages[target.Id].Stores.Where(g => g.Value > 0)
                            .OrderBy(g => GoodsCatalog.ClassOf(g.Key) != GoodsClass.Food).ThenBy(g => g.Key).ToArray())
                            fromStorehouse += Take(kind, Math.Min(amount, room - fromStorehouse));
                    break;
                case RaidGoal.Site when target.Holdings.Contains(raid.Site!):
                    if (Beat(defenders * (_space.Distance(raid.Site!.Position, target.Position) <= NearSite ? inner : FarSiteGuard)))
                    {
                        seized = raid.Site;
                        _villages[target.Id] = _villages[target.Id] with { Holdings = Array.AsReadOnly(target.Holdings.Where(h => h != seized).ToArray()) };
                        var captors = _villages[raid.FromVillageId];
                        _villages[captors.Id] = captors with { Holdings = Array.AsReadOnly(captors.Holdings.Append(seized).ToArray()) };
                    }
                    break;
                case RaidGoal.Site:
                    won = false; // lost to someone else meanwhile
                    break;
                case RaidGoal.Captives:
                    if (Beat(defenders * inner))
                        captives.AddRange(present.Reverse().Take(Math.Max(1, raiders.Length / 2)));
                    break;
            }
        // The losing side takes a blow for every two of the winners; a close win costs the Raiders one of theirs.
        var hurt = 0;
        if (reached && present.Length > 0 && (hardest >= 0 || !won))
        {
            var (losers, blows) = won ? (present, (raiders.Length + 1) / 2) : (raiders, ((int)defenders + 1) / 2);
            foreach (var id in losers.Where(id => _villagers.ViewOf(id) is not null).Take(blows)) { HitVillager(id); hurt++; }
            foreach (var id in raiders.Where(id => _villagers.ViewOf(id) is not null).Take(won && hardest * 2 >= raiders.Length ? 1 : 0).ToArray())
            {
                HitVillager(id);
                hurt++;
            }
        }
        captives.RemoveAll(id => _villagers.ViewOf(id) is null);
        foreach (var id in captives) Seize(id, target.Id, raid.FromVillageId);
        var success = won && fromYard + fromPile + fromStorehouse + captives.Count > 0 || seized is not null;
        if (reached)
            Emit(new RaidStruck(clock, raid.Id, raid.FromVillageId, target.Id, raid.Goal, success, fromYard, fromPile, fromStorehouse,
                Array.AsReadOnly(captives.ToArray()), seized, hurt));
        var route = CaravanRoute(_navigation.NearestWalkable(raid.Route[^1]), _villages[raid.FromVillageId].Position);
        var home = raid with
        {
            VillagerIds = Array.AsReadOnly(raid.VillagerIds.Where(id => _villagers.ViewOf(id) is not null).ToArray()),
            CaptiveIds = Array.AsReadOnly(captives.ToArray()), Route = route, Next = 0, Returning = true,
            Loot = new CaravanCargo { Goods = new ReadOnlyDictionary<GoodsKind, int>(goods), Materials = new ReadOnlyDictionary<MaterialKind, int>(materials) },
        };
        _raids[_raids.FindIndex(r => r.Id == raid.Id)] = home;
        foreach (var id in home.VillagerIds.Concat(home.CaptiveIds)) _villagers.Engage(id, VillagerAction.Raiding, route[0]);

        int Take(GoodsKind kind, int amount)
        {
            if (amount <= 0) return 0;
            ChangeStores(target.Id, kind, -amount);
            goods[kind] = goods.GetValueOrDefault(kind) + amount;
            return amount;
        }
    }

    // A Captive changes Village at once (so it is fed and counted there) but is held apart until absorbed.
    private void Seize(int villagerId, int fromId, int toId)
    {
        CancelFurnitureTasks(t => t.VillagerIds.Contains(villagerId));
        _craftingTasks.RemoveAll(t => t.VillagerId == villagerId);
        var home = _villages[fromId];
        _villages[fromId] = home with
        {
            VillagerIds = Array.AsReadOnly(home.VillagerIds.Where(id => id != villagerId).ToArray()),
            Outposts = Array.AsReadOnly(home.Outposts.Select(o => o with { VillagerIds = Array.AsReadOnly(o.VillagerIds.Where(id => id != villagerId).ToArray()) }).ToArray()),
        };
        _villages[toId] = _villages[toId] with { VillagerIds = Array.AsReadOnly(_villages[toId].VillagerIds.Append(villagerId).ToArray()) };
        _villagers.JoinVillage(_villages[toId]);
    }

    private void EndRaid(Raid raid, StoreClock clock)
    {
        _raids.RemoveAll(r => r.Id == raid.Id);
        foreach (var (kind, amount) in raid.Loot.Goods) ChangeStores(raid.FromVillageId, kind, amount);
        var home = _villages[raid.FromVillageId];
        _villages[home.Id] = home with
        {
            Materials = raid.Loot.Materials.Aggregate(home.Materials, (m, p) => Plus(m, p.Key, p.Value)),
            Captives = Array.AsReadOnly(home.Captives.Concat(raid.CaptiveIds.Select(id => new Captive(id, raid.ToVillageId, _tick))).ToArray()),
        };
        foreach (var id in raid.VillagerIds.Concat(raid.CaptiveIds)) _villagers.Release(id, VillagerAction.Raiding);
        Emit(new RaidReturned(clock, raid.Id, raid.FromVillageId));
    }

    private void AbsorbCaptives(StoreClock clock)
    {
        foreach (var village in _villages.ToArray())
        {
            if (village.Captives.Count == 0) continue;
            var kept = village.Captives.Where(c => village.VillagerIds.Contains(c.VillagerId) && _tick - c.Since < CaptiveAbsorbTicks).ToArray();
            if (kept.Length == village.Captives.Count) continue;
            _villages[village.Id] = village with { Captives = Array.AsReadOnly(kept) };
            foreach (var c in village.Captives.Except(kept).Where(c => village.VillagerIds.Contains(c.VillagerId)))
                Emit(new CaptiveAbsorbed(clock, c.VillagerId, village.Id, c.FromVillageId));
        }
    }

    private void RestoreRaids(WorldSnapshot s)
    {
        if (s.Raids.Select(r => r.Id).Distinct().Count() != s.Raids.Count ||
            s.Raids.Any(r => r.FromVillageId < 0 || r.FromVillageId >= s.Villages.Count || r.ToVillageId < 0 || r.ToVillageId >= s.Villages.Count ||
                !Enum.IsDefined(r.Goal) || (r.Goal == RaidGoal.Site) != (r.Site is not null) || r.Route.Count == 0 || r.Next < 0 || r.Next >= r.Route.Count ||
                r.CaptiveIds.Any(id => !s.Villages[r.FromVillageId].VillagerIds.Contains(id))) ||
            s.Villages.Any(v => v.Holdings.Any(h => !Enum.IsDefined(h.Kind)) ||
                v.Captives.Any(c => c.FromVillageId < 0 || c.FromVillageId >= s.Villages.Count || c.Since > s.Clock.Tick)) ||
            s.Villages.SelectMany(v => v.Holdings).Select(h => (h.Kind, h.SiteId)).Distinct().Count() != s.Villages.Sum(v => v.Holdings.Count))
            throw new InvalidDataException("Invalid world save: inconsistent Raids.");
        _raids.AddRange(s.Raids);
        _nextRaidId = s.Raids.Select(r => r.Id + 1).DefaultIfEmpty().Max();
    }
}
