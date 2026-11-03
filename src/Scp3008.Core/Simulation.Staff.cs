using System.Numerics;

namespace Scp3008.Core;

/// <summary>A group of Staff moving together along a patrol route; a Sector's Hordes walk its Patrol route every Closing time.</summary>
/// <param name="SectorId">The Sector whose Patrol route the Horde walks; null for a Horde placed with its own route.</param>
/// <param name="Route">The waypoints the Horde walks round, in order.</param>
public sealed record Horde(int Id, int? SectorId, IReadOnlyList<int> StaffIds, IReadOnlyList<Vector2> Route);

public sealed partial class ScenarioBuilder
{
    /// <summary>
    /// A blow on a Staff. While the Store is open it does no harm: the Staff falls, gets up a few Store minutes later
    /// and stands again where it was, still dormant.
    /// </summary>
    public void HitStaff(int staffId) => _simulation.HitStaff(staffId);

    /// <summary>A noise of the given audible radius at a position (as dragging Furniture or fighting makes), heard by the Staff on the next step.</summary>
    public void MakeNoise(Vector2 position, float radius) => _simulation.MakeNoise(position, radius);
}

public sealed partial class Simulation
{
    private const int StaffRouteStream = 6;

    // The hidden Staff rules (tuning).
    private const int RouteWaypoints = 6;
    private const float RouteRadiusShare = 0.15f;
    private const int MaxHordeSize = 6;
    private const int MaxStaffPerSector = 12;
    private const int NightsPerExtraStaff = 2;
    private const int ResidentsPerExtraStaff = 4;
    // Footprint paths reach a few hundred metres at most, so a Staff crosses the Store in legs this long.
    private const float StaffLeg = 80f;
    private static readonly TimeSpan FallenFor = TimeSpan.FromMinutes(10);

    // Hearing and sight (tuning). Noise radii are what a Villager nearby would hear; the Staff hear it much farther.
    private const float NoiseCarry = 3f;
    private const float MattressMuffle = 0.4f;
    private const float FogMuffle = 0.7f;
    private const float StaffSight = 6f;
    private const float FogBlinding = 0.7f;
    private const float ChaseGivesUpAt = 25f;
    private const float SearchRadius = 5f;
    private static readonly TimeSpan SearchFor = TimeSpan.FromMinutes(20);

    /// <param name="Source">Where a heard noise drew the Horde; it searches there, then returns to its route.</param>
    /// <param name="ChaseId">The Villager the Horde chases, or -1.</param>
    private sealed record HordeState(int Id, int? SectorId, Vector2[] Route, int Start, int Next, List<int> StaffIds, long WaitUntil = 0,
        Vector2? Source = null, long SearchUntil = 0, int ChaseId = -1, int SiegeVillageId = -1, long NextBlow = 0, int Blows = 0);
    private sealed record StaffState(int HordeId, float Health, long FallenUntil);

    private readonly List<HordeState> _hordes = [];
    private readonly Dictionary<int, StaffState> _staffStates = [];
    // The last Closing night (numbered by the Store day it began on) whose Staff have come out.
    private int _staffNight;
    private readonly List<NoiseEmitted> _noises = [];

    /// <summary>How strongly the Staff of a Sector react to noise, 0.6 to 1.6; fixed by the Store seed, hidden from the Villagers.</summary>
    public float StaffNoiseSensitivity(int sectorId) => 0.6f + SeededRandom.For(_config.Seed, StaffRouteStream, sectorId, 1).Range(0f, 1f);

    /// <summary>A Staff placed by hand walks its own route as a Horde of one, from night to night where it left off.</summary>
    internal Staff PlaceStaff(Vector2 position, Vector2[] route) => Publish(() =>
    {
        var staff = _staff.Place(position);
        var horde = new HordeState(_hordes.Count == 0 ? 0 : _hordes.Max(h => h.Id) + 1, null, route.Select(_space.Wrap).ToArray(), 0, 0, [staff.Id]);
        _hordes.Add(horde);
        _staffStates[staff.Id] = new(horde.Id, 1f, 0);
        return StaffView(_staff.ViewOf(staff.Id)!, new StoreClock(_tick));
    });

    internal void MakeNoise(Vector2 position, float radius) => Publish(() =>
    {
        Emit(new NoiseEmitted(new StoreClock(_tick), _space.Wrap(position), radius, -1));
        return true;
    });

    internal void HitStaff(int staffId) => Publish(() =>
    {
        var state = _staffStates.GetValueOrDefault(staffId) ?? throw new ArgumentException($"No Staff {staffId}.", nameof(staffId));
        // shortcut: a blow at Closing time does nothing yet; harming and killing Staff is ticket #77.
        if (new StoreClock(_tick).Phase == DayPhase.ClosingTime) return false;
        _staffStates[staffId] = state with { FallenUntil = _tick + StoreClock.TicksFor(FallenFor) };
        _staff.Stop(staffId);
        return true;
    });

    private void AdvanceStaff(StoreClock clock)
    {
        var closing = clock.Phase == DayPhase.ClosingTime;
        var night = clock.TimeOfDay >= StoreHours.ClosingTimeStarts ? clock.Day : clock.Day - 1;
        if (closing && night > _staffNight)
        {
            _staffNight = night;
            for (var i = 0; i < _hordes.Count; i++)
            {
                if (_hordes[i].SectorId is not null) _hordes[i] = _hordes[i] with { Next = _hordes[i].Start };
                _hordes[i] = _hordes[i] with { SiegeVillageId = -1 };
            }
            GrowStaff(night);
        }
        if (_hordes.Count == 0) { _noises.Clear(); return; }
        for (var i = 0; i < _hordes.Count; i++)
        {
            var horde = _hordes[i];
            var acting = new List<Villager>();
            foreach (var id in horde.StaffIds)
            {
                var staff = _staff.ViewOf(id)!;
                if (Acting(staff, clock)) acting.Add(staff);
                else if (staff.Destination is not null) _staff.Stop(id);
            }
            if (acting.Count == 0) continue;
            if (Besiege(i, acting, clock)) continue;
            horde = _hordes[i];
            horde = _hordes[i] = Hear(horde, acting);
            horde = _hordes[i] = Notice(horde, acting);
            if (horde.ChaseId >= 0)
            {
                // Retarget the Villager as they run.
                if (clock.Tick % 5 == 0 && _villagers.ViewOf(horde.ChaseId) is { } chased)
                    for (var n = 0; n < acting.Count; n++) _staff.Send(acting[n].Id, _space.Wrap(chased.Position + HordeOffset(n)));
                continue;
            }
            if (acting.Any(s => s.Destination is not null) || clock.Tick < horde.WaitUntil) continue;
            if (horde.Source is { } source)
            {
                if (_space.Distance(acting[0].Position, source) <= SearchRadius)
                {
                    var until = horde.SearchUntil == 0 ? clock.Tick + StoreClock.TicksFor(SearchFor) : horde.SearchUntil;
                    if (clock.Tick >= until) { _hordes[i] = horde with { Source = null, SearchUntil = 0 }; continue; }
                    _hordes[i] = horde with { SearchUntil = until };
                    // shortcut: it searches by wandering a few metres round the source; no line-of-sight search yet.
                    var wander = new Vector2(MathF.Cos(clock.Tick * 0.37f), MathF.Sin(clock.Tick * 0.37f)) * 6f;
                    for (var n = 0; n < acting.Count; n++) _staff.Send(acting[n].Id, _space.Wrap(source + wander + HordeOffset(n)));
                    continue;
                }
                for (var n = 0; n < acting.Count; n++) SendStaff(acting[n].Id, _space.Wrap(source + HordeOffset(n)), clock);
                // Shut in behind Furniture (a hiding Band): search round it from the near side instead.
                if (acting.All(s => _staff.ViewOf(s.Id)!.Destination is null))
                {
                    var side = source + Vector2.Normalize(_space.Delta(source, acting[0].Position) + new Vector2(0.001f)) * (SearchRadius - 1);
                    for (var n = 0; n < acting.Count; n++) SendStaff(acting[n].Id, _space.Wrap(side + HordeOffset(n)), clock);
                }
                // No way there: give up and return to the route.
                if (acting.All(s => _staff.ViewOf(s.Id)!.Destination is null)) _hordes[i] = horde with { Source = null };
                continue;
            }
            var target = horde.Route[horde.Next];
            var arriving = true;
            for (var n = 0; n < acting.Count; n++)
                arriving &= SendStaff(acting[n].Id, _space.Wrap(target + HordeOffset(n)), clock);
            if (acting.All(s => _staff.ViewOf(s.Id)!.Destination is null))
                // No way there: try the next waypoint a Store minute later rather than search again every tick.
                _hordes[i] = horde with { Next = (horde.Next + 1) % horde.Route.Length, WaitUntil = clock.Tick + StoreClock.TicksFor(TimeSpan.FromMinutes(1)) };
            else if (arriving)
                _hordes[i] = horde with { Next = (horde.Next + 1) % horde.Route.Length };
        }
        _noises.Clear();
        _staff.Advance(StoreClock.TickDuration.TotalHours);
    }

    /// <summary>A noise draws the Horde if it carries as far as the Horde: farther in some regions, muffled by Fog banks and the mattress mountain.</summary>
    private HordeState Hear(HordeState horde, List<Villager> acting)
    {
        if (_noises.Count == 0 || horde.ChaseId >= 0) return horde;
        var lead = acting[0].Position;
        var sensitivity = StaffNoiseSensitivity(_snapshot.SectorAt(lead).Id);
        foreach (var noise in _noises)
        {
            var muffle = Math.Max(_snapshot.FogDensityAt(noise.Position), _snapshot.FogDensityAt(lead)) * FogMuffle;
            var reach = noise.Radius * NoiseCarry * sensitivity * (1 - muffle) *
                (_landmarks.Any(l => l.Type == LandmarkType.MattressMountain && l.Covers(_space, noise.Position)) ? MattressMuffle : 1f);
            if (_space.Distance(lead, noise.Position) > reach) continue;
            horde = horde with { Source = noise.Position, SearchUntil = 0 };
            foreach (var s in acting) _staff.Stop(s.Id);
        }
        return horde;
    }

    /// <summary>A Staff notices a Villager up close, in the dark too, unless they stand still behind Furniture; fog shortens the sight.</summary>
    private HordeState Notice(HordeState horde, List<Villager> acting)
    {
        if (horde.ChaseId >= 0)
        {
            if (_villagers.ViewOf(horde.ChaseId) is { } v && acting.Any(s => _space.Distance(s.Position, v.Position) <= ChaseGivesUpAt)) return horde;
            return horde with { ChaseId = -1, Source = null, SearchUntil = 0 };
        }
        foreach (var staff in acting)
        {
            var reach = StaffSight * (1 - FogBlinding * _snapshot.FogDensityAt(staff.Position));
            foreach (var v in _villagers.Snapshot())
            {
                if (_space.Distance(staff.Position, v.Position) > reach) continue;
                if (v.Destination is null && Hidden(staff, v)) continue;
                foreach (var s in acting) _staff.Stop(s.Id);
                return horde with { ChaseId = v.Id, Source = null, SearchUntil = 0 };
            }
        }
        return horde;
    }

    private bool Hidden(Villager staff, Villager villager)
    {
        var eye = new Vector3(staff.Position, 1.6f);
        var target = new Vector3(staff.Position + _space.Delta(staff.Position, villager.Position), 1f);
        return _piecesIndex.Near(villager.Position, 8f).Any(p => RestockSight.Blocks(_space, p, eye, target));
    }

    /// <summary>Whether a Staff acts now: at Closing time, or in the dead zone at any hour, unless it lies fallen.</summary>
    private bool Acting(Villager staff, StoreClock clock) =>
        _staffStates[staff.Id].FallenUntil <= clock.Tick && (clock.Phase == DayPhase.ClosingTime || InDeadZone(staff.Position));

    private bool InDeadZone(Vector2 position) => _landmarks.Any(l => l.Type == LandmarkType.DeadZone && l.Covers(_space, position));

    // The members of a Horde keep a little apart around its waypoint.
    private static Vector2 HordeOffset(int member) =>
        member == 0 ? Vector2.Zero : new Vector2(MathF.Cos(member * 2.4f), MathF.Sin(member * 2.4f)) * (1.5f + member * 0.3f);

    /// <summary>
    /// Sends a Staff on towards <paramref name="goal"/>: through the Ball pits first if it stands at one at Closing time and
    /// another comes out nearer the goal, then all the way, or one leg if the goal is far. Whether it is sent all the way.
    /// </summary>
    private bool SendStaff(int id, Vector2 goal, StoreClock clock)
    {
        var position = _staff.ViewOf(id)!.Position;
        if (clock.Phase == DayPhase.ClosingTime && BallPitAt(position) is { } pit
            && _ballPitNetwork.StaffExitsFrom(pit.Id, clock.Phase).Select(p => _pointsOfInterest[p])
                .MinBy(p => _space.Distance(p.Centre, goal)) is { } exit
            && _space.Distance(exit.Centre, goal) < _space.Distance(position, goal))
        {
            _staff.Teleport(id, BallPitMouth(exit));
            position = _staff.ViewOf(id)!.Position;
        }
        var delta = _space.Delta(position, goal);
        if (delta.Length() <= StaffLeg)
        {
            _staff.Send(id, goal);
            return true;
        }
        _staff.Send(id, _space.Wrap(position + Vector2.Normalize(delta) * StaffLeg));
        return false;
    }

    /// <summary>Where Staff come out of a Ball pit: the free floor nearest its centre that is not shut in among its Furniture.</summary>
    private Vector2 BallPitMouth(PointOfInterest pit)
    {
        for (var radius = 0f; radius <= pit.Size.X / 2; radius += 0.5f)
            for (var k = 0; k < 8; k++)
            {
                var at = _navigation.NearestWalkable(_space.Wrap(pit.Centre + new Vector2(MathF.Cos(k * MathF.Tau / 8), MathF.Sin(k * MathF.Tau / 8)) * radius));
                if (_navigation.Pocket(at) is null) return at;
            }
        return _navigation.NearestWalkable(pit.Centre);
    }

    private PointOfInterest? BallPitAt(Vector2 position) => _ballPitNetwork.BallPitIds.Select(id => _pointsOfInterest[id])
        .FirstOrDefault(p => _space.Distance(p.Centre, position) <= p.Size.Length() / 2 + 2);

    /// <summary>
    /// New Staff come out of the Ball pits at the start of Closing time: each Sector's Staff grow towards a number that
    /// rises with the nights and with the people in the Sector, and join its Hordes.
    /// </summary>
    private void GrowStaff(int night)
    {
        var pits = _ballPitNetwork.BallPitIds;
        if (pits.Count == 0) return;
        var residents = _villagers.Snapshot().GroupBy(v => _snapshot.SectorAt(v.Position).Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var sector in _sectors)
        {
            var target = Math.Min(MaxStaffPerSector,
                1 + (night - 1) / NightsPerExtraStaff + residents.GetValueOrDefault(sector.Id) / ResidentsPerExtraStaff);
            var hordes = _hordes.Where(h => h.SectorId == sector.Id).ToList();
            for (var count = hordes.Sum(h => h.StaffIds.Count); count < target; count++)
            {
                var horde = hordes.LastOrDefault(h => h.StaffIds.Count < MaxHordeSize);
                if (horde is null)
                {
                    var route = PatrolRoute(sector);
                    horde = new(_hordes.Count == 0 ? 0 : _hordes.Max(h => h.Id) + 1, sector.Id, route, hordes.Count * 2 % route.Length,
                        hordes.Count * 2 % route.Length, []);
                    _hordes.Add(horde);
                    hordes.Add(horde);
                }
                // They come out of the Ball pit nearest the Sector's centre.
                var pit = pits.Select(id => _pointsOfInterest[id]).MinBy(p => _space.Distance(p.Centre, sector.Centre))!;
                var staff = _staff.Place(BallPitMouth(pit));
                horde.StaffIds.Add(staff.Id);
                _staffStates[staff.Id] = new(horde.Id, 1f, 0);
            }
        }
    }

    /// <summary>A Sector's Patrol route: a loop of waypoints around its centre, fixed by the Store seed.</summary>
    private Vector2[] PatrolRoute(Sector sector)
    {
        var random = SeededRandom.For(_config.Seed, StaffRouteStream, sector.Id);
        var turn = random.Range(0, MathF.Tau);
        return Enumerable.Range(0, RouteWaypoints).Select(i =>
        {
            var angle = turn + MathF.Tau * i / RouteWaypoints + random.Range(-0.3f, 0.3f);
            var radius = sector.Size * RouteRadiusShare * random.Range(0.7f, 1.3f);
            return _navigation.NearestWalkable(sector.Centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }).ToArray();
    }

    private IReadOnlyList<Staff> StaffSnapshot()
    {
        var clock = new StoreClock(_tick);
        return Array.AsReadOnly(_staff.Snapshot().Select(s => StaffView(s, clock)).ToArray());
    }

    private Staff StaffView(Villager staff, StoreClock clock)
    {
        var state = _staffStates[staff.Id];
        return new(staff.Id, staff.Position, staff.Destination, staff.Heading)
        {
            HordeId = state.HordeId, ChasingVillagerId = _hordes.FirstOrDefault(h => h.Id == state.HordeId) is { ChaseId: >= 0 } h ? h.ChaseId : null, Health = state.Health, Fallen = state.FallenUntil > clock.Tick,
            Hostile = state.FallenUntil <= clock.Tick && (clock.Phase == DayPhase.ClosingTime || InDeadZone(staff.Position)),
        };
    }

    private IReadOnlyList<Horde> HordeSnapshot() =>
        _hordes.Select(h => new Horde(h.Id, h.SectorId, h.StaffIds.ToList().AsReadOnly(), Array.AsReadOnly(h.Route.ToArray()))).ToList().AsReadOnly();
}

internal sealed partial class Villagers
{
    /// <summary>Stops a walker where it stands.</summary>
    public void Stop(int id)
    {
        var walker = _walkers.Single(w => w.Id == id);
        walker.Waypoints.Clear();
        walker.ClimbWaypoints.Clear();
        walker.ClimbDestination = null;
        walker.Destination = null;
    }

    /// <summary>Puts a walker down at the position (or the nearest free floor), stopped.</summary>
    public void Teleport(int id, Vector2 position)
    {
        Stop(id);
        var walker = _walkers.Single(w => w.Id == id);
        walker.Position = navigation.NearestWalkable(position);
        walker.Elevation = 0;
    }
}
