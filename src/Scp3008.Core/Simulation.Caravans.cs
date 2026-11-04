using System.Collections.ObjectModel;
using System.Numerics;

namespace Scp3008.Core;

/// <summary>What a Caravan carries: stores, Materials, Gear, know-how (Recipes) and a part of the sender's Known map.</summary>
public sealed record CaravanCargo
{
    public IReadOnlyDictionary<GoodsKind, int> Goods { get; init; } = new ReadOnlyDictionary<GoodsKind, int>(new Dictionary<GoodsKind, int>());
    public IReadOnlyDictionary<MaterialKind, int> Materials { get; init; } = new ReadOnlyDictionary<MaterialKind, int>(new Dictionary<MaterialKind, int>());
    public IReadOnlyList<Gear> Gear { get; init; } = [];
    public IReadOnlyList<GearKind> Recipes { get; init; } = [];
    public KnownMap? Map { get; init; }
}

/// <summary>Villagers carrying goods between two Villages for barter. They walk the Route (a detour round the dead zone if need be), trade, and walk home with the payment.</summary>
/// <param name="Next">The Route waypoint the Caravan is walking to.</param>
/// <param name="Returning">Whether it has traded and is carrying the payment home.</param>
/// <param name="Position">Where its leader stands.</param>
public sealed record Caravan(int Id, int FromVillageId, int ToVillageId, IReadOnlyList<int> VillagerIds, CaravanCargo Cargo,
    IReadOnlyList<Vector2> Route, int Next, bool Returning, Vector2 Position)
{
    /// <summary>Platform carts linked in a train (1 is a single cart): it carries more, but walks slower and is heard from further.</summary>
    public int Carts { get; init; } = 1;
    [System.Text.Json.Serialization.JsonIgnore] public int LoadLimit => Simulation.CaravanLoadPerCart * Carts;
    [System.Text.Json.Serialization.JsonIgnore] public float Pace => 1 / (1 + 0.35f * (Carts - 1));
    [System.Text.Json.Serialization.JsonIgnore] public float NoiseRadius => Carts > 1 ? 12 * (Carts - 1) : 0;
}

public sealed record CaravanDeparted(StoreClock At, int CaravanId, int FromVillageId, int ToVillageId) : DomainEvent(At);
/// <summary>A Caravan bartered at its destination; <see cref="Value"/> is what the cargo was worth there, paid in <see cref="Paid"/>.</summary>
public sealed record CaravanTraded(StoreClock At, int CaravanId, int FromVillageId, int ToVillageId, double Value, GoodsKind? Paid, int PaidAmount) : DomainEvent(At);
public sealed record CaravanReturned(StoreClock At, int CaravanId, int VillageId) : DomainEvent(At);
/// <summary>The Staff hit a Caravan on the way: its cargo is lost and each Villager is hit.</summary>
public sealed record CaravanAmbushed(StoreClock At, int CaravanId, int FromVillageId, int ToVillageId, Vector2 Position) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Sends a Caravan of the Village's own Villagers with the cargo to another Village; the cargo leaves the stores at once.</summary>
    public Caravan SendCaravan(int fromVillageId, int toVillageId, IReadOnlyDictionary<GoodsKind, int>? goods = null,
        IReadOnlyDictionary<MaterialKind, int>? materials = null, IReadOnlyList<GearKind>? gear = null,
        IReadOnlyList<GearKind>? recipes = null, float? shareMapWithin = null, int carts = 1) =>
        _simulation.SendCaravan(fromVillageId, toVillageId, goods, materials, gear, recipes, shareMapWithin, carts);
}

public sealed partial class Simulation
{
    private const int CaravanEvery = 5;
    private const int CaravanDispatchEvery = 500;
    private const float WaypointReach = 3, DestinationReach = 6, AmbushRange = 5, DeadZoneMargin = 4;
    private const int CaravanMembers = 2, CaravanLoadLimit = 10;
    internal const int CaravanLoadPerCart = CaravanLoadLimit;
    private const double GearValue = 5, RecipeValue = 10, MapEntryValue = 0.5;
    private readonly List<Caravan> _caravans = [];
    private int _nextCaravanId;

    /// <summary>What a Village asks for a unit of the Goods: dearer the less it holds against what it needs.</summary>
    public double BarterPrice(int villageId, GoodsKind kind) => Publish(() => Price(_villages[villageId], kind));

    private static int Need(Village village, GoodsKind kind) =>
        GoodsCatalog.ClassOf(kind) == GoodsClass.Food ? Math.Max(4, village.VillagerIds.Count * 2) : 4;

    private static double Price(Village village, GoodsKind kind) =>
        1 + 3 * Math.Clamp(1 - (double)village.Stores.GetValueOrDefault(kind) / Need(village, kind), 0, 1);

    private static IReadOnlyDictionary<K, int> Plus<K>(IReadOnlyDictionary<K, int> map, K key, int amount) where K : notnull
    {
        var copy = map.ToDictionary();
        copy[key] = copy.GetValueOrDefault(key) + amount;
        if (copy[key] <= 0) copy.Remove(key);
        return new ReadOnlyDictionary<K, int>(copy);
    }

    internal Caravan SendCaravan(int fromId, int toId, IReadOnlyDictionary<GoodsKind, int>? goods, IReadOnlyDictionary<MaterialKind, int>? materials,
        IReadOnlyList<GearKind>? gear, IReadOnlyList<GearKind>? recipes, float? shareMapWithin, int carts = 1) => Publish(() =>
    {
        if (fromId == toId || fromId < 0 || toId < 0 || fromId >= _villages.Count || toId >= _villages.Count)
            throw new ArgumentException("A Caravan goes between two different Villages.");
        var from = _villages[fromId];
        var people = _villagers.Snapshot();
        var members = from.VillagerIds.Select(id => people.FirstOrDefault(p => p.Id == id)).OfType<Villager>()
            .Where(p => IsAvailableWorker(p) && p.Action != VillagerAction.Trading && !_wounds.ContainsKey(p.Id))
            .OrderBy(p => p.Role != Role.Trader).Take(CaravanMembers).ToArray();
        if (members.Length == 0) throw new InvalidOperationException("No free Villager to lead a Caravan.");
        goods ??= new Dictionary<GoodsKind, int>();
        materials ??= new Dictionary<MaterialKind, int>();
        gear ??= [];
        recipes ??= [];
        if (carts is < 1 or > 4 || goods.Values.Sum() + materials.Values.Sum() > CaravanLoadPerCart * carts)
            throw new ArgumentException("A Caravan carries at most 10 units per linked cart, in a train of one to four.");
        var carried = gear.Select(kind => from.Gear.FirstOrDefault(g => g.Kind == kind)).ToArray();
        if (goods.Any(g => g.Value <= 0 || from.Stores.GetValueOrDefault(g.Key) < g.Value) ||
            materials.Any(m => m.Value <= 0 || from.Materials.GetValueOrDefault(m.Key) < m.Value) ||
            carried.Any(g => g is null) || carried.Distinct().Count() != carried.Length ||
            recipes.Any(r => !from.Recipes.Contains(r)))
            throw new ArgumentException("A Caravan can carry only what its Village has.");
        KnownMap? map = null;
        if (shareMapWithin is { } radius)
            map = new KnownMap
            {
                ExploredGround = from.KnownMap.ExploredGround.Where(p => _space.Distance(from.Position, p) <= radius).ToArray(),
                Goods = from.KnownMap.Goods.Where(g => _space.Distance(from.Position, g.Position) <= radius).ToArray(),
                Water = from.KnownMap.Water.Where(w => _space.Distance(from.Position, w.Position) <= radius).ToArray(),
                Dangers = from.KnownMap.Dangers.Where(d => _space.Distance(from.Position, d.Position) <= radius).ToArray(),
                Villages = from.KnownMap.Villages.Where(v => _space.Distance(from.Position, v.Position) <= radius).ToArray(),
            };
        var cargo = new CaravanCargo
        {
            Goods = new ReadOnlyDictionary<GoodsKind, int>(goods.ToDictionary()),
            Materials = new ReadOnlyDictionary<MaterialKind, int>(materials.ToDictionary()),
            Gear = carried.OfType<Gear>().ToArray(), Recipes = recipes.ToArray(), Map = map,
        };
        foreach (var (kind, amount) in goods) ChangeStores(fromId, kind, -amount);
        _villages[fromId] = _villages[fromId] with
        {
            Materials = materials.Aggregate(_villages[fromId].Materials, (m, p) => Plus(m, p.Key, -p.Value)),
            Gear = Array.AsReadOnly(_villages[fromId].Gear.Except(cargo.Gear).ToArray()),
        };
        var route = CaravanRoute(from.Position, _villages[toId].Position);
        var caravan = new Caravan(_nextCaravanId++, fromId, toId, Array.AsReadOnly(members.Select(m => m.Id).ToArray()), cargo,
            route, 0, false, members[0].Position) { Carts = carts };
        _caravans.Add(caravan);
        foreach (var member in members) _villagers.Engage(member.Id, VillagerAction.Trading, route[0]);
        Emit(new CaravanDeparted(new StoreClock(_tick), caravan.Id, fromId, toId));
        return caravan;
    });

    // The waypoints from one place to another: the straight way, or a detour round the corners of a dead zone it would cross.
    // shortcut: one detour of at most two corners round one zone's rectangle; upgrade if dead zones ever crowd a route.
    private Vector2[] CaravanRoute(Vector2 from, Vector2 to)
    {
        bool Inside(Landmark z, Vector2 p)
        {
            var d = _space.Delta(z.Centre, p);
            return MathF.Abs(d.X) <= z.Size.X / 2 + DeadZoneMargin && MathF.Abs(d.Y) <= z.Size.Y / 2 + DeadZoneMargin;
        }
        bool Clear(Landmark z, Vector2 a, Vector2 b)
        {
            var delta = _space.Delta(a, b);
            var steps = (int)(delta.Length() / 2) + 1;
            for (var i = 0; i <= steps; i++)
                if (Inside(z, a + delta * (i / (float)steps))) return false;
            return true;
        }
        float Length(Vector2[] points) => points.Zip(points.Skip(1), (a, b) => _space.Distance(a, b)).Sum();
        var zone = _landmarks.FirstOrDefault(l => l.Type == LandmarkType.DeadZone && !Clear(l, from, to));
        if (zone is null) return [to];
        var half = zone.Size / 2 + new Vector2(DeadZoneMargin + 2);
        var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) }
            .Select(c => _navigation.NearestWalkable(_space.Wrap(zone.Centre + c * half))).ToArray();
        var best = corners.Select(c => new[] { c }).Concat(corners.SelectMany(a => corners.Where(b => b != a).Select(b => new[] { a, b })))
            .Select(path => (Vector2[])[from, .. path, to])
            .Where(path => path.Zip(path.Skip(1), (a, b) => Clear(zone, a, b)).All(ok => ok))
            .OrderBy(Length).FirstOrDefault();
        return best is null ? [to] : best[1..];
    }

    private void AdvanceCaravans(StoreClock clock)
    {
        if (_tick % CaravanDispatchEvery == 7) DispatchCaravans(clock);
        if (_caravans.Count == 0 || _tick % CaravanEvery != 2) return;
        var hostile = _staff.Snapshot().Where(s => Acting(s, clock)).ToArray();
        foreach (var caravan in _caravans.ToArray())
        {
            var members = caravan.VillagerIds.Where(id => _villagers.ViewOf(id) is not null).ToArray();
            if (members.Length == 0) { _caravans.Remove(caravan); continue; }
            foreach (var id in members) _villagers.SetPace(id, caravan.Pace);
            if (caravan.Carts > 1 && _tick % 20 == 2)
                Emit(new NoiseEmitted(clock, _villagers.ViewOf(members[0])!.Position, caravan.NoiseRadius, -1));
            var leader = _villagers.ViewOf(members[0])!;
            var here = caravan with { VillagerIds = Array.AsReadOnly(members), Position = leader.Position };
            if (members.Any(id => hostile.Any(s => _space.Distance(s.Position, _villagers.ViewOf(id)!.Position) <= AmbushRange)))
            {
                _caravans.Remove(caravan);
                Emit(new CaravanAmbushed(clock, caravan.Id, caravan.FromVillageId, caravan.ToVillageId, leader.Position));
                foreach (var id in members)
                {
                    _villagers.SetPace(id, 1);
                    HitVillager(id);
                    if (_villagers.ViewOf(id) is null) continue;
                    _villagers.Release(id, VillagerAction.Trading);
                    _villagers.Send(id, _villages[caravan.FromVillageId].Position);
                }
                continue;
            }
            var last = here.Next == here.Route.Count - 1;
            var reached = _space.Distance(leader.Position, here.Route[here.Next]) <= (last ? DestinationReach : WaypointReach);
            if (!reached && leader.Destination is not null)
            {
                _caravans[_caravans.IndexOf(caravan)] = here;
                continue;
            }
            if (!last)
            {
                here = here with { Next = here.Next + 1 };
                foreach (var id in members) _villagers.Engage(id, VillagerAction.Trading, here.Route[here.Next]);
                _caravans[_caravans.IndexOf(caravan)] = here;
            }
            // Out of ways to walk short of the goal: a Caravan going out turns back with its cargo, one coming home just ends.
            else if (here.Returning) Arrive(here, clock);
            else Trade(here, clock, reached);
        }
    }

    private void Trade(Caravan caravan, StoreClock clock, bool reached)
    {
        var to = _villages[caravan.ToVillageId];
        var cargo = caravan.Cargo;
        var back = cargo;
        if (reached && !to.Fallen)
        {
            // Barter takes Goods only up to the Village's need; its currency it takes whatever the amount. The rest goes home.
            var accepted = cargo.Goods.ToDictionary(g => g.Key, g => to.Currency == g.Key ? g.Value
                : Math.Clamp(Need(to, g.Key) - to.Stores.GetValueOrDefault(g.Key), 0, g.Value));
            var value = accepted.Sum(g => g.Value * TradeValue(to, g.Key)) + cargo.Materials.Values.Sum() + cargo.Gear.Count * GearValue +
                cargo.Recipes.Count(r => !to.Recipes.Contains(r)) * RecipeValue +
                (cargo.Map is { } m ? m.Goods.Count + m.Water.Count + m.Dangers.Count + m.Villages.Count : 0) * MapEntryValue;
            foreach (var (kind, amount) in accepted.Where(g => g.Value > 0)) ChangeStores(to.Id, kind, amount);
            var refused = cargo.Goods.Where(g => g.Value > accepted[g.Key]).ToDictionary(g => g.Key, g => g.Value - accepted[g.Key]);
            to = _villages[to.Id] with
            {
                Materials = cargo.Materials.Aggregate(_villages[to.Id].Materials, (mats, p) => Plus(mats, p.Key, p.Value)),
                Gear = Array.AsReadOnly(_villages[to.Id].Gear.Concat(cargo.Gear).ToArray()),
                Recipes = Array.AsReadOnly(to.Recipes.Union(cargo.Recipes).ToArray()),
                KnownMap = cargo.Map is { } shared ? MergeMaps([_villages[to.Id].KnownMap, shared]) : _villages[to.Id].KnownMap,
            };
            _villages[to.Id] = to;
            if (cargo.Map is { } known) _villagers.ShareKnowledge(to.VillagerIds, known);
            // The Village pays in the Goods it has most to spare, never more than the spare.
            GoodsKind? paid = to.Stores.Where(g => !cargo.Goods.ContainsKey(g.Key) && g.Value > Need(to, g.Key))
                .OrderByDescending(g => g.Value - Need(to, g.Key)).Select(g => (GoodsKind?)g.Key).FirstOrDefault();
            var paidAmount = paid is { } kindPaid
                ? Math.Min(to.Stores[kindPaid] - Need(to, kindPaid), (int)Math.Floor(value / Price(to, kindPaid))) : 0;
            if (paidAmount > 0) ChangeStores(to.Id, paid!.Value, -paidAmount);
            else paid = null;
            if (paid is { } p) refused[p] = paidAmount;
            back = new CaravanCargo { Goods = new ReadOnlyDictionary<GoodsKind, int>(refused) };
            Emit(new CaravanTraded(clock, caravan.Id, caravan.FromVillageId, to.Id, value, paid, paidAmount));
        }
        var route = CaravanRoute(to.Position, _villages[caravan.FromVillageId].Position);
        var home = caravan with { Cargo = back, Route = route, Next = 0, Returning = true };
        _caravans[_caravans.FindIndex(c => c.Id == caravan.Id)] = home;
        foreach (var id in home.VillagerIds) _villagers.Engage(id, VillagerAction.Trading, route[0]);
    }

    private void Arrive(Caravan caravan, StoreClock clock)
    {
        _caravans.RemoveAll(c => c.Id == caravan.Id);
        var cargo = caravan.Cargo;
        foreach (var (kind, amount) in cargo.Goods) ChangeStores(caravan.FromVillageId, kind, amount);
        var home = _villages[caravan.FromVillageId];
        _villages[home.Id] = home with
        {
            Materials = cargo.Materials.Aggregate(home.Materials, (m, p) => Plus(m, p.Key, p.Value)),
            Gear = Array.AsReadOnly(home.Gear.Concat(cargo.Gear).ToArray()),
        };
        foreach (var id in caravan.VillagerIds) { _villagers.SetPace(id, 1); _villagers.Release(id, VillagerAction.Trading); }
        Emit(new CaravanReturned(clock, caravan.Id, home.Id));
    }

    // Villages of Traders send surplus Goods to a Village that wants them more, while the Store is open.
    private void DispatchCaravans(StoreClock clock)
    {
        if (clock.Phase == DayPhase.ClosingTime) return;
        foreach (var from in _villages.ToArray())
        {
            if (from.Personality != Personality.Traders || from.Fallen || from.VillagerIds.Count < 2 || _caravans.Any(c => c.FromVillageId == from.Id)) continue;
            var known = from.KnownMap.Villages.Where(v => !v.Abandoned).Select(v => v.VillageId).ToHashSet();
            var best = (Gain: 1.0, To: -1, Kind: default(GoodsKind), Amount: 0);
            foreach (var to in _villages.Where(v => v.Id != from.Id && !v.Fallen && v.VillagerIds.Count > 0 && known.Contains(v.Id)))
                foreach (var (kind, held) in from.Stores.Where(g => g.Value > Need(from, g.Key) * 1.5))
                {
                    var gain = Price(to, kind) - Price(from, kind);
                    if (gain > best.Gain) best = (gain, to.Id, kind, Math.Min(held - Need(from, kind), CaravanLoadLimit));
                }
            if (best.To < 0) continue;
            try { SendCaravan(from.Id, best.To, new Dictionary<GoodsKind, int> { [best.Kind] = best.Amount }, null, null, null, null); }
            catch (InvalidOperationException) { /* nobody free to go */ }
        }
    }

    private void RestoreCaravans(IReadOnlyList<Caravan> caravans, IReadOnlyList<Village> villages)
    {
        if (caravans.Select(c => c.Id).Distinct().Count() != caravans.Count ||
            caravans.Any(c => c.FromVillageId < 0 || c.FromVillageId >= villages.Count || c.ToVillageId < 0 || c.ToVillageId >= villages.Count ||
                c.Route.Count == 0 || c.Next < 0 || c.Next >= c.Route.Count || c.VillagerIds.Count == 0 ||
                c.Cargo.Gear.Any(g => { g.Validate(); return false; })))
            throw new InvalidDataException("Invalid world save: inconsistent Caravans.");
        _caravans.AddRange(caravans);
        _nextCaravanId = caravans.Select(c => c.Id + 1).DefaultIfEmpty().Max();
    }
}
