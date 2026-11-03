using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// The body a killed Staff leaves (ADR 0007). After a few hours it draws other Staff, ever more strongly, until Staff carry it
/// off at Closing time; a Village hauls it away from itself, far enough that the Horde it draws stays away.
/// </summary>
/// <param name="KilledAt">When the Staff was killed; the remains draw Staff from <see cref="Pull"/> metres away.</param>
public sealed record StaffRemains(int Id, Vector2 Position, StoreClock KilledAt)
{
    /// <summary>How far, in metres, the remains draw Staff now: 0 for the first few hours, then growing.</summary>
    public float Pull { get; init; }
    /// <summary>The Villager hauling them away (walking to them, then carrying them), if any.</summary>
    public int? CarrierId { get; init; }
    /// <summary>Where the Village is hauling them, or where it already left them.</summary>
    public Vector2? HaulTo { get; init; }
    /// <summary>Whether the carrier has picked them up.</summary>
    public bool Carried { get; init; }
}

/// <summary>A group of Villagers fighting a Staff: a blow exchange every Store minute while they are in reach.</summary>
public sealed record StaffFight(int StaffId, IReadOnlyList<int> VillagerIds, long NextBlowTick, long UntilTick);

/// <summary>Villagers killed a Staff at Closing time; it left <paramref name="Remains"/>.</summary>
public sealed record StaffKilled(StoreClock At, int StaffId, IReadOnlyList<int> VillagerIds, StaffRemains Remains) : DomainEvent(At);

/// <summary>A blow without a Staff-killing weapon drove a Staff back for a moment.</summary>
public sealed record StaffDrivenBack(StoreClock At, int StaffId, int VillagerId) : DomainEvent(At);

/// <summary>A Staff carried Staff remains off at Closing time.</summary>
public sealed record StaffRemainsCarriedOff(StoreClock At, int RemainsId, int StaffId, Vector2 Position) : DomainEvent(At);

/// <summary>A Village hauled Staff remains away from itself and left them at <paramref name="Position"/>.</summary>
public sealed record StaffRemainsHauled(StoreClock At, int RemainsId, int VillageId, int VillagerId, Vector2 Position) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>
    /// The Villagers go for a Staff and fight it. At Closing time a Halberd blow harms it, any other weapon only drives it back,
    /// and the Staff strikes the fighter in front; while the Store is open a blow only fells it for a while (unharmed).
    /// </summary>
    public void FightStaff(int staffId, IReadOnlyList<int> villagerIds) => _simulation.FightStaff(staffId, villagerIds);

    /// <summary>Staff remains at a position, as if a Staff had been killed there <paramref name="age"/> ago.</summary>
    public StaffRemains PlaceStaffRemains(Vector2 position, TimeSpan age = default) => _simulation.PlaceStaffRemains(position, age);
}

public sealed partial class Simulation
{
    // The hidden fighting rules (tuning; ADR 0007: if Villages farm kills, raise the cost, not the killability).
    private const float FightReach = 2f;
    private static readonly TimeSpan FightBlowEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FightGivesUpAfter = TimeSpan.FromHours(1);
    private const float HalberdBlow = 0.09f;
    private const double ArmourBlow = 50;
    private const float FightNoise = 12f;
    private const float DrivenBackBy = 2f;
    private static readonly TimeSpan DrivenBackFor = TimeSpan.FromSeconds(20);
    // So few Guards with Halberds do not take on a Staff in a Siege.
    private const int FightingGroup = 3;

    // The hidden remains rules (tuning).
    private const double RemainsQuietHours = 3;
    private const float RemainsPullPerHour = 15f;
    private const float RemainsMaxPull = 200f;
    private const float CarryOffReach = 2f;
    private const float HaulRange = 40f;
    private const float HaulDistance = 60f;

    private readonly List<StaffFight> _fights = [];
    private readonly List<StaffRemains> _remains = [];

    internal void FightStaff(int staffId, IReadOnlyList<int> villagerIds) => Publish(() =>
    {
        if (!_staffStates.ContainsKey(staffId)) throw new ArgumentException($"No Staff {staffId}.", nameof(staffId));
        if (villagerIds.Count == 0 || villagerIds.Any(id => _villagers.ViewOf(id) is null))
            throw new ArgumentException("A fight needs Villagers.", nameof(villagerIds));
        StartFight(staffId, villagerIds);
        return true;
    });

    private void StartFight(int staffId, IReadOnlyList<int> villagerIds)
    {
        _fights.RemoveAll(f => f.StaffId == staffId);
        _fights.Add(new(staffId, villagerIds.ToArray(), _tick, _tick + StoreClock.TicksFor(FightGivesUpAfter)));
        _staff.Stop(staffId);
        foreach (var id in villagerIds) _villagers.Engage(id, VillagerAction.Fighting, _staff.ViewOf(staffId)!.Position);
    }

    internal StaffRemains PlaceStaffRemains(Vector2 position, TimeSpan age) => Publish(() =>
    {
        if (age < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(age));
        var remains = new StaffRemains(NextRemainsId(), _navigation.NearestWalkable(_space.Wrap(position)),
            new StoreClock(Math.Max(0, _tick - StoreClock.TicksFor(age))));
        _remains.Add(remains);
        return RemainsView(remains);
    });

    private IReadOnlyList<StaffFight> FightSnapshot() => _fights.ToList().AsReadOnly();

    private int NextRemainsId() => _remains.Count == 0 ? 0 : _remains.Max(r => r.Id) + 1;

    private bool Fighting(int staffId) => _fights.Any(f => f.StaffId == staffId);

    private void AdvanceFights(StoreClock clock)
    {
        for (var i = _fights.Count - 1; i >= 0; i--)
        {
            var fight = _fights[i];
            var fighters = fight.VillagerIds.Select(_villagers.ViewOf).OfType<Villager>().ToList();
            if (!_staffStates.TryGetValue(fight.StaffId, out var state) || fighters.Count == 0 || clock.Tick >= fight.UntilTick)
            {
                EndFight(i);
                continue;
            }
            var at = _staff.ViewOf(fight.StaffId)!.Position;
            var inReach = fighters.Where(v => _space.Distance(v.Position, at) <= FightReach).ToList();
            if (clock.Tick % 5 == 0)
                foreach (var v in fighters.Except(inReach).Where(v => v.Destination is null))
                    _villagers.Engage(v.Id, VillagerAction.Fighting, at);
            if (clock.Tick < fight.NextBlowTick || inReach.Count == 0) continue;
            _fights[i] = fight with { NextBlowTick = clock.Tick + StoreClock.TicksFor(FightBlowEvery) };
            Emit(new NoiseEmitted(clock, at, FightNoise, -1));
            // While the Store is open (outside the dead zone) a Staff cannot be harmed: it falls and gets up again.
            if (clock.Phase != DayPhase.ClosingTime && !InDeadZone(at))
            {
                _staffStates[fight.StaffId] = state with { FallenUntil = clock.Tick + StoreClock.TicksFor(FallenFor) };
                continue;
            }
            // The Staff strikes first, at the fighter in front, unless it is down.
            if (state.FallenUntil <= clock.Tick && !_villagers.AbsorbBlow(inReach[0].Id, ArmourBlow)) HitVillager(inReach[0].Id);
            var health = state.Health;
            Villager? driver = null;
            foreach (var v in inReach.Where(v => _villagers.ViewOf(v.Id) is not null))
            {
                if (v.Hand?.Kind == GearKind.Halberd) health -= HalberdBlow;
                else if (v.Hand is not null) driver ??= v;
            }
            if (health <= 0)
            {
                KillStaff(fight.StaffId, fight.VillagerIds, clock);
                continue;
            }
            state = state with { Health = health };
            if (driver is not null)
            {
                var away = _space.Delta(driver.Position, at);
                _staff.Teleport(fight.StaffId, _space.Wrap(at + (away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitX) * DrivenBackBy));
                state = state with { FallenUntil = clock.Tick + StoreClock.TicksFor(DrivenBackFor) };
                Emit(new StaffDrivenBack(clock, fight.StaffId, driver.Id));
            }
            _staffStates[fight.StaffId] = state;
        }
    }

    private void EndFight(int index)
    {
        foreach (var id in _fights[index].VillagerIds) _villagers.Release(id, VillagerAction.Fighting);
        _fights.RemoveAt(index);
    }

    private void KillStaff(int staffId, IReadOnlyList<int> villagerIds, StoreClock clock)
    {
        var position = _staff.ViewOf(staffId)!.Position;
        _staff.Remove(staffId);
        var hordeId = _staffStates[staffId].HordeId;
        _staffStates.Remove(staffId);
        var horde = _hordes.Single(h => h.Id == hordeId);
        horde.StaffIds.Remove(staffId);
        // A Sector's Horde stays to be refilled by the Staff's growth; a placed Horde of one is gone.
        if (horde.StaffIds.Count == 0 && horde.SectorId is null) _hordes.Remove(horde);
        var index = _fights.FindIndex(f => f.StaffId == staffId);
        if (index >= 0) EndFight(index);
        var remains = new StaffRemains(NextRemainsId(), position, clock);
        _remains.Add(remains);
        Emit(new StaffKilled(clock, staffId, villagerIds.ToArray(), remains));
    }

    /// <summary>How far the remains draw Staff: nothing for the first hours, then ever farther.</summary>
    private float RemainsPull(StaffRemains remains)
    {
        var hours = (_tick - remains.KilledAt.Tick) * StoreClock.TickDuration.TotalHours - RemainsQuietHours;
        return hours <= 0 ? 0 : (float)Math.Min(RemainsMaxPull, hours * RemainsPullPerHour);
    }

    private StaffRemains RemainsView(StaffRemains remains) => remains with { Pull = RemainsPull(remains) };

    private IReadOnlyList<StaffRemains> RemainsSnapshot() => _remains.Select(RemainsView).ToList().AsReadOnly();

    /// <summary>Remains draw a Horde that is not already going somewhere, if their pull reaches it.</summary>
    private HordeState DrawToRemains(HordeState horde, List<Villager> acting)
    {
        if (_remains.Count == 0 || horde.ChaseId >= 0 || horde.Source is not null) return horde;
        var lead = acting[0].Position;
        var drawing = _remains.Where(r => _space.Distance(lead, r.Position) <= RemainsPull(r))
            .OrderBy(r => _space.Distance(lead, r.Position)).FirstOrDefault();
        if (drawing is null) return horde;
        foreach (var s in acting) _staff.Stop(s.Id);
        return horde with { Source = drawing.Position, SearchUntil = 0 };
    }

    /// <summary>At Closing time a Staff that reaches remains carries them off. Whether it did.</summary>
    private bool CarryOffRemains(int hordeIndex, List<Villager> acting, StoreClock clock)
    {
        foreach (var staff in acting)
        {
            var remains = _remains.FirstOrDefault(r => _space.Distance(staff.Position, r.Position) <= CarryOffReach);
            if (remains is null) continue;
            // shortcut: carried-off remains vanish at once; walking them to a Ball pit can come with a carrying animation.
            _remains.Remove(remains);
            if (remains.CarrierId is { } carrier) _villagers.Release(carrier, VillagerAction.HaulingRemains);
            _hordes[hordeIndex] = _hordes[hordeIndex] with { Source = null, SearchUntil = 0 };
            Emit(new StaffRemainsCarriedOff(clock, remains.Id, staff.Id, remains.Position));
            return true;
        }
        return false;
    }

    /// <summary>
    /// A Village hauls remains lying near it away from itself, while the Store is open: a free member walks to them and
    /// carries them off, far enough that the Horde they draw stays away from the Village (bait).
    /// </summary>
    private void AdvanceRemains(StoreClock clock)
    {
        for (var i = _remains.Count - 1; i >= 0; i--)
        {
            var remains = _remains[i];
            if (remains.CarrierId is not { } carrierId)
            {
                if (clock.Tick % 50 == 3 && clock.Phase != DayPhase.ClosingTime) _remains[i] = StartHaul(remains);
                continue;
            }
            if (_villagers.ViewOf(carrierId) is not { } carrier)
            {
                _remains[i] = remains with { CarrierId = null, Carried = false, HaulTo = null };
                continue;
            }
            var haulTo = remains.HaulTo!.Value;
            if (!remains.Carried)
            {
                if (_space.Distance(carrier.Position, remains.Position) <= 1.5f)
                {
                    _villagers.Engage(carrierId, VillagerAction.HaulingRemains, haulTo);
                    _remains[i] = remains with { Carried = true };
                }
                else if (carrier.Destination is null) Drop(i, null);
                continue;
            }
            _remains[i] = remains = remains with { Position = carrier.Position };
            if (_space.Distance(carrier.Position, haulTo) <= 2f)
                Drop(i, new StaffRemainsHauled(clock, remains.Id, _villages.Where(v => !v.Fallen).MinBy(v => _space.Distance(v.Position, haulTo))?.Id ?? -1, carrierId, carrier.Position));
            // No way on: leave them where the path ends.
            else if (carrier.Destination is null) Drop(i, null);
        }
    }

    private StaffRemains StartHaul(StaffRemains remains)
    {
        if (remains.HaulTo is not null) return remains;
        var village = _villages.Where(v => !v.Fallen && v.VillagerIds.Count > 0 && _space.Distance(v.Position, remains.Position) <= HaulRange)
            .MinBy(v => _space.Distance(v.Position, remains.Position));
        if (village is null) return remains;
        var carrier = village.VillagerIds.Select(_villagers.ViewOf).OfType<Villager>()
            .Where(v => IsAvailableWorker(v) && v.CarriedAmount + v.CarriedMaterials == 0 && !_wounds.ContainsKey(v.Id) &&
                v.Action is not (VillagerAction.Fighting or VillagerAction.HaulingRemains) && !_remains.Any(r => r.CarrierId == v.Id))
            .MinBy(v => _space.Distance(v.Position, remains.Position));
        if (carrier is null) return remains;
        var away = _space.Delta(village.Position, remains.Position);
        var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitX;
        // Never farther than half the Store across, or the wrap brings them back round to the Village.
        var distance = Math.Min(HaulDistance, _space.Size / 2 - 4);
        var haulTo = _navigation.NearestWalkable(_space.Wrap(village.Position + direction * distance));
        _villagers.Engage(carrier.Id, VillagerAction.HaulingRemains, remains.Position);
        return remains with { CarrierId = carrier.Id, HaulTo = haulTo };
    }

    private void Drop(int index, StaffRemainsHauled? hauled)
    {
        var remains = _remains[index];
        _villagers.Release(remains.CarrierId!.Value, VillagerAction.HaulingRemains);
        // Left short of the way and still near a Village, they may be taken up again.
        _remains[index] = remains with { CarrierId = null, Carried = false, HaulTo = hauled is null ? null : remains.HaulTo };
        if (hauled is not null) Emit(hauled);
    }

    private void RestoreStaffKills(WorldSnapshot world)
    {
        var fights = world.StaffFights;
        var people = world.Villagers.Select(v => v.Id).ToHashSet();
        if (world.StaffRemains.Select(r => r.Id).Distinct().Count() != world.StaffRemains.Count ||
            world.StaffRemains.Any(r => !float.IsFinite(r.Position.X) || !float.IsFinite(r.Position.Y) || r.KilledAt.Tick > world.Clock.Tick ||
                r.CarrierId is { } c && (!people.Contains(c) || r.HaulTo is null) || r.Carried && r.CarrierId is null) ||
            fights.Any(f => !_staffStates.ContainsKey(f.StaffId) || f.VillagerIds.Count == 0) ||
            fights.Select(f => f.StaffId).Distinct().Count() != fights.Count)
            throw new InvalidDataException("Invalid world save: inconsistent Staff fights or remains.");
        _remains.AddRange(world.StaffRemains.Select(r => r with { Pull = 0 }));
        _fights.AddRange(fights);
    }
}

internal sealed partial class Villagers
{
    /// <summary>Puts a Villager on a Staff task (fighting, hauling remains), going to <paramref name="destination"/>; their own needs do not move them until released.</summary>
    public void Engage(int id, VillagerAction action, Vector2 destination)
    {
        Send(id, destination);
        _walkers.Single(w => w.Id == id).Action = action;
    }

    /// <summary>Ends a Staff task: the Villager's own needs take over again.</summary>
    public void Release(int id, VillagerAction action)
    {
        if (_walkers.Find(w => w.Id == id) is { } walker && walker.Action == action) walker.Action = VillagerAction.Idle;
    }

    /// <summary>Metal armour takes a Staff's blow, wearing down. Whether it took it.</summary>
    public bool AbsorbBlow(int id, double wear)
    {
        var w = _walkers.Single(w => w.Id == id);
        if (w.Armour?.Kind != GearKind.MetalArmour) return false;
        w.Armour = Wear(w.Armour, wear);
        return true;
    }
}
