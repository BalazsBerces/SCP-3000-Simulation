using System.Numerics;

namespace Scp3008.Core;

/// <summary>A Horde came upon a fortified Village at Closing time and began a Siege.</summary>
public sealed record SiegeBegan(StoreClock At, int VillageId, int HordeId) : DomainEvent(At);

/// <summary>A Siege broke a piece out of a complete home wall ring: the ring is breached.</summary>
public sealed record WallBreached(StoreClock At, int VillageId, int Ring, int Slot, int FurnitureId) : DomainEvent(At);

/// <summary>A Siege broke a piece out of the complete Keep: the Villagers' last refuge is breached.</summary>
public sealed record KeepBreached(StoreClock At, int VillageId, int FurnitureId) : DomainEvent(At);

/// <summary>A Village fell: its survivors left as a Band of Refugees (<paramref name="BandId"/> null when none survived).</summary>
public sealed record VillageFell(StoreClock At, int VillageId, IReadOnlyList<int> RefugeeIds, int? BandId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Plans a Village's Keep if it has none and fills it with wardrobes at once, as if the builders had finished it.</summary>
    public void BuildKeep(int villageId) => _simulation.BuildKeep(villageId);
}

public sealed partial class Simulation
{
    // The hidden Siege rules (tuning).
    private const float SiegeRange = 25f;
    private const float StrikeReach = 2.5f;
    private static readonly TimeSpan BlowEvery = TimeSpan.FromMinutes(3);
    // A Staff a Guard holds off strikes the Guard instead of the wall, one cycle in this many.
    private const int GuardHitEvery = 4;

    /// <summary>The piece a Siege strikes next; Ring is -1 for a Keep piece.</summary>
    private readonly record struct SiegeTarget(int FurnitureId, int Ring, int Slot, bool Strapped, bool Backfilled);

    internal void BuildKeep(int villageId) => Publish(() =>
    {
        var village = _villages[villageId];
        if (!village.Castle.Any(c => c.Part == CastlePart.Keep))
            _villages[villageId] = village = village with { Castle = Array.AsReadOnly(village.Castle.Append(KeepPlan(village, 0)).ToArray()) };
        var c = village.Castle.ToList().FindIndex(s => s.Part == CastlePart.Keep);
        for (var i = 0; i < village.Castle[c].Slots.Count; i++)
        {
            var slot = _villages[villageId].Castle[c].Slots[i];
            if (slot.FurnitureId is not null) continue;
            var supports = Castle.Support(CastlePart.Keep, i).Select(s => Piece(_villages[villageId].Castle[c].Slots[s].FurnitureId)).ToArray();
            var elevation = supports.Length == 0 ? 0 : supports.Max(s => s.Elevation + FurnitureCatalog.Spec(s).Height);
            var piece = PlaceFurniture(FurnitureType.Wardrobe, slot.Position, slot.Rotation, 0, 0, elevation);
            SetCastleSlot(villageId, c, i, slot with { Elevation = elevation, FurnitureId = piece.Id, ReservedFurnitureId = null });
        }
        return true;
    });

    /// <summary>A Village a Horde besieges: one with people and a complete home wall ring or a complete Keep.</summary>
    private static bool Fortified(Village village) => !village.Fallen && village.VillagerIds.Count > 0 &&
        (village.Walls.Where((w, i) => village.IsHomeWall(i) && w.Complete).Any() || village.Castle.Any(c => c.Part == CastlePart.Keep && c.Complete));

    /// <summary>The weakest piece still standing in an unbreached wall: lowest Tier, then most damaged; once every ring is breached, the Keep's.</summary>
    private SiegeTarget? NextSiegeTarget(Village village)
    {
        var wall = village.Walls.Select((w, r) => (Wall: w, Ring: r)).Where(x => village.IsHomeWall(x.Ring) && x.Wall.Complete)
            .SelectMany(x => x.Wall.Slots.Select((s, i) => (x.Ring, Index: i, Slot: s)))
            .OrderBy(x => x.Slot.Tier).ThenByDescending(x => Piece(x.Slot.FurnitureId).Damage).ThenBy(x => x.Ring).ThenBy(x => x.Index)
            .Select(x => (SiegeTarget?)new SiegeTarget(x.Slot.FurnitureId!.Value, x.Ring, x.Index, x.Slot.Strapped, x.Slot.Backfilled)).FirstOrDefault();
        if (wall is not null) return wall;
        if (village.Castle.FirstOrDefault(c => c.Part == CastlePart.Keep && c.Complete) is not { } keep) return null;
        return keep.Slots.Select(s => Piece(s.FurnitureId)).OrderBy(p => TierOf(p, false, false)).ThenByDescending(p => p.Damage).ThenBy(p => p.Id)
            .Select(p => (SiegeTarget?)new SiegeTarget(p.Id, -1, -1, false, false)).First();
    }

    /// <summary>
    /// A Horde near a fortified Village besieges it: it strikes the weakest wall piece, then the Keep, while each Guard holds off one
    /// Staff; with walls and Keep breached it strikes the Guards, and with no defender left the Village falls. Whether it besieges.
    /// </summary>
    private bool Besiege(int i, List<Villager> acting, StoreClock clock)
    {
        var horde = _hordes[i];
        if (Calm) return false;
        if (horde.SiegeVillageId < 0)
        {
            var lead = acting[0].Position;
            if (_villages.FirstOrDefault(v => Fortified(v) && _space.Distance(lead, v.Position) <= SiegeRange) is not { } found) return false;
            foreach (var s in acting) _staff.Stop(s.Id);
            horde = _hordes[i] = horde with { SiegeVillageId = found.Id, ChaseId = -1, Source = null, SearchUntil = 0,
                NextBlow = clock.Tick + StoreClock.TicksFor(BlowEvery) };
            Emit(new SiegeBegan(clock, found.Id, horde.Id));
        }
        var village = _villages[horde.SiegeVillageId];
        if (village.Fallen || village.VillagerIds.Count == 0)
        {
            _hordes[i] = horde with { SiegeVillageId = -1 };
            return false;
        }
        // shortcut: Guards defend from anywhere in the Village; walk them to the breach when fights get positions.
        var guards = village.VillagerIds.Where(id => !_wounds.ContainsKey(id) && _villagers.ViewOf(id) is { Role: Role.Guard }).ToList();
        var target = NextSiegeTarget(village);
        if (target is null && guards.Count == 0)
        {
            Fall(village, clock);
            return true;
        }
        var aim = target is { } t ? Piece(t.FurnitureId).Position : _villagers.ViewOf(guards[0])!.Position;
        var outward = _space.Delta(village.Position, aim);
        var stand = _navigation.NearestWalkable(_space.Wrap(aim + (outward.LengthSquared() > 0.01f ? Vector2.Normalize(outward) : Vector2.UnitX)));
        var inReach = acting.Where(s => _space.Distance(s.Position, aim) <= StrikeReach).ToList();
        if (clock.Tick >= horde.WaitUntil && inReach.Count < acting.Count)
        {
            for (var n = 0; n < acting.Count; n++)
                if (!inReach.Contains(acting[n]) && _staff.ViewOf(acting[n].Id)!.Destination is null)
                    SendStaff(acting[n].Id, _space.Wrap(stand + HordeOffset(n) * 0.3f), clock);
            if (inReach.Count == 0 && acting.All(s => _staff.ViewOf(s.Id)!.Destination is null))
                horde = _hordes[i] = horde with { WaitUntil = clock.Tick + StoreClock.TicksFor(TimeSpan.FromMinutes(1)) };
        }
        // Enough Guards with Halberds take on a Staff in reach together (ADR 0007).
        var armed = guards.Where(id => _villagers.ViewOf(id)!.Hand?.Kind == GearKind.Halberd).ToList();
        if (armed.Count >= FightingGroup && inReach.Count > 0 && !_fights.Any(f => f.VillagerIds.Any(armed.Contains)))
            StartFight(inReach[0].Id, armed);
        if (clock.Tick < horde.NextBlow || inReach.Count == 0) return true;
        var blows = horde.Blows + 1;
        _hordes[i] = horde with { NextBlow = clock.Tick + StoreClock.TicksFor(BlowEvery), Blows = blows };
        if (target is not { } piece)
        {
            foreach (var guard in guards.Take(inReach.Count)) HitVillager(guard);
            return true;
        }
        var held = Math.Min(guards.Count, inReach.Count);
        if (blows % GuardHitEvery == 0)
            foreach (var guard in guards.Take(held)) HitVillager(guard);
        for (var n = held; n < inReach.Count; n++)
        {
            if (!StrikePiece(piece.FurnitureId, piece.Strapped, piece.Backfilled).Broken) continue;
            if (piece.Ring >= 0) Emit(new WallBreached(clock, village.Id, piece.Ring, piece.Slot, piece.FurnitureId));
            else Emit(new KeepBreached(clock, village.Id, piece.FurnitureId));
            break;
        }
        // Behind breached walls the people withdraw into the Keep.
        if (NextSiegeTarget(_villages[village.Id]) is not { Ring: >= 0 } && _villages[village.Id].Castle.FirstOrDefault(c => c.Part == CastlePart.Keep) is { } keepPlan)
        {
            var refuge = keepPlan.Slots[0].Position + _space.Delta(keepPlan.Slots[0].Position, keepPlan.Slots[1].Position) / 2;
            foreach (var id in _villages[village.Id].VillagerIds.Except(guards))
                if (_villagers.ViewOf(id) is { } v && _space.Distance(v.Position, refuge) > 3f) _villagers.Send(id, _navigation.NearestWalkable(refuge));
        }
        return true;
    }

    /// <summary>The Village falls: its survivors leave as a Band of Refugees carrying its knowledge and Recipes.</summary>
    private void Fall(Village village, StoreClock clock)
    {
        var survivors = village.VillagerIds.ToArray();
        _villages[village.Id] = village with
        {
            Fallen = true, VillagerIds = [],
            Outposts = Array.AsReadOnly(village.Outposts.Select(o => o with { VillagerIds = [], Abandoned = true }).ToArray()),
        };
        _villagers.LeaveVillage(survivors);
        int? bandId = null;
        if (survivors.Length > 0)
        {
            var band = new Band(_nextBandId++, Array.AsReadOnly(survivors))
            {
                Position = village.Position, FoundingPopulation = survivors.Length, Personality = village.Personality,
                KnownMap = village.KnownMap, Recipes = village.Recipes, FallenVillageId = village.Id,
            };
            _bands.Add(band with { Strategy = StrategyFor(band.Personality) });
            bandId = band.Id;
        }
        for (var h = 0; h < _hordes.Count; h++)
            if (_hordes[h].SiegeVillageId == village.Id) _hordes[h] = _hordes[h] with { SiegeVillageId = -1 };
        Emit(new VillageFell(clock, village.Id, Array.AsReadOnly(survivors), bandId));
    }
}

internal sealed partial class Villagers
{
    /// <summary>The Villagers no longer belong to a Village (it fell).</summary>
    public void LeaveVillage(IReadOnlyCollection<int> ids)
    {
        foreach (var walker in _walkers.Where(w => ids.Contains(w.Id))) walker.VillageId = null;
    }
}
