using System.Numerics;

namespace Scp3008.Core;

public enum DeviceKind { NoiseLure, Alarm, FurnitureTrap, Pit }

/// <summary>
/// A noise lure, alarm or trap a Village sets against the Hordes. A noise lure (a wind-up music box, variant 1, or a radio) set
/// off far from the Village draws a Horde away; an alarm (a tin-can trip wire) wakes the Guards when a Staff walks into it;
/// a Furniture trap (a stack built to fall under the stability rules) or a pit catches the Staff that walk onto it.
/// </summary>
/// <param name="FurnitureIds">A Furniture trap's stack, the prop it stands on first; empty for the other devices.</param>
public sealed record Device(int Id, DeviceKind Kind, int VillageId, Vector2 Position, IReadOnlyList<int> FurnitureIds)
{
    public int Variant { get; init; }
    /// <summary>A noise lure set off plays until this tick.</summary>
    public long PlayingUntil { get; init; }
    /// <summary>The alarm or trap went off; alarms and pits are set again when the Store opens, a Furniture trap once its stack stands again.</summary>
    public bool Sprung { get; init; }
}

/// <summary>A claim a Village holds about the Store's hidden rules, with a confidence; it may be false.</summary>
/// <param name="Where">The place the claim is about (for the Village's Horde Belief: where the Hordes come from).</param>
public sealed record Belief(string Claim, Vector2 Where, double Confidence);

public sealed record DeviceSet(StoreClock At, Device Device) : DomainEvent(At);
public sealed record LureSetOff(StoreClock At, int DeviceId, int VillageId) : DomainEvent(At);
/// <summary>A playing noise lure drew a Horde off its course.</summary>
public sealed record HordeLured(StoreClock At, int DeviceId, int VillageId, int HordeId, Vector2 Position) : DomainEvent(At);
public sealed record AlarmRaised(StoreClock At, int DeviceId, int VillageId, int StaffId) : DomainEvent(At);
/// <summary>A Village's Guards got up and stood guard (an alarm, or a Siege beginning).</summary>
public sealed record GuardsWoken(StoreClock At, int VillageId, IReadOnlyList<int> GuardIds) : DomainEvent(At);
/// <summary>A trap caught Staff; <paramref name="Baited"/> when Staff remains lay on it.</summary>
public sealed record TrapSprung(StoreClock At, int DeviceId, DeviceKind Kind, int VillageId, IReadOnlyList<int> StaffIds, bool Baited, Vector2 Position) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>A Village's device at a position, set at once (a Furniture trap's stack is built there).</summary>
    public Device SetDevice(int villageId, DeviceKind kind, Vector2 position, int variant = 0) => _simulation.SetDevice(villageId, kind, position, variant);

    /// <summary>The Village learns a Recipe, as if it had found its assembly instructions.</summary>
    public void TeachRecipe(int villageId, GearKind recipe) => _simulation.TeachRecipe(villageId, recipe);

    /// <summary>Sets a noise lure off: it plays for an hour.</summary>
    public void SetOffLure(int deviceId) => _simulation.SetOffLure(deviceId);
}

public sealed partial class Simulation
{
    // The hidden device rules (tuning).
    private const float LureReach = 90f;
    private static readonly TimeSpan LurePlaysFor = TimeSpan.FromHours(1);
    private const float AlarmSpan = 5f;
    private const float TrapReach = 2.5f;
    private const float TrapBlow = 0.25f;
    // A trap hurts but never kills: killing takes a fight (ADR 0007).
    private const float TrapLeaves = 0.1f;
    private static readonly TimeSpan TrapPins = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PitHolds = TimeSpan.FromMinutes(30);
    private const float BaitReach = 3f;
    // Where a Village sets them, along the side its Belief says the Hordes come from; lures off to the side, far away.
    private const float AlarmRange = 40f;
    private const float TrapRange = 33f;
    private const float LureRange = 70f;
    private const int DevicesPerHordeNight = 2;
    private const int MaxDevices = 8;

    private static readonly DeviceKind[] DeviceOrder = [DeviceKind.Alarm, DeviceKind.FurnitureTrap, DeviceKind.NoiseLure, DeviceKind.Pit];

    private readonly List<Device> _devices = [];

    private IReadOnlyList<Device> DeviceSnapshot() => _devices.ToList().AsReadOnly();

    // The world's Horde nights so far: the Villages arm themselves as the Horde nights grow, a new Village too.
    private int HordeNightsSoFar => HordeNights.EveryNights == 0 ? 0 : _lastHordeNight / HordeNights.EveryNights;

    internal Device SetDevice(int villageId, DeviceKind kind, Vector2 position, int variant) => Publish(() =>
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (villageId < 0 || villageId >= _villages.Count) throw new ArgumentException($"No Village {villageId}.", nameof(villageId));
        return PlaceDevice(villageId, kind, position, variant, new StoreClock(_tick));
    });

    internal void SetOffLure(int deviceId) => Publish(() =>
    {
        var index = _devices.FindIndex(d => d.Id == deviceId && d.Kind == DeviceKind.NoiseLure);
        if (index < 0) throw new ArgumentException($"No noise lure {deviceId}.", nameof(deviceId));
        SetOff(index, new StoreClock(_tick));
        return true;
    });

    internal void TeachRecipe(int villageId, GearKind recipe) => Publish(() =>
    {
        if (villageId < 0 || villageId >= _villages.Count) throw new ArgumentException($"No Village {villageId}.", nameof(villageId));
        if (!Recipes.All.Any(r => r.Gear == recipe)) throw new ArgumentException("Unknown Recipe.", nameof(recipe));
        var village = _villages[villageId];
        if (!village.Recipes.Contains(recipe)) _villages[villageId] = village with { Recipes = Array.AsReadOnly(village.Recipes.Append(recipe).ToArray()) };
        return true;
    });

    // shortcut: a Device (and a trap's Furniture) appears in place at once; walk a Villager out to set it when Villagers get set-up tasks.
    private Device PlaceDevice(int villageId, DeviceKind kind, Vector2 position, int variant, StoreClock clock)
    {
        var at = _navigation.NearestWalkable(_space.Wrap(position));
        int[] stack = [];
        if (kind == DeviceKind.FurnitureTrap)
        {
            // A flat-pack box props up a dresser with a bookcase on top; pulled out, the stack falls (ADR 0002).
            var prop = PlaceFurniture(FurnitureType.FlatPackBox, at, 0, 0, 0);
            var dresser = PlaceFurniture(FurnitureType.Dresser, at, 0, 0, 0, FurnitureCatalog.Spec(prop).Height);
            var top = PlaceFurniture(FurnitureType.Bookcase, at, 0, 0, 0, dresser.Elevation + FurnitureCatalog.Spec(dresser).Height);
            stack = [prop.Id, dresser.Id, top.Id];
        }
        var device = new Device(_devices.Count == 0 ? 0 : _devices.Max(d => d.Id) + 1, kind, villageId, at, Array.AsReadOnly(stack)) { Variant = variant };
        _devices.Add(device);
        Emit(new DeviceSet(clock, device));
        return device;
    }

    private void SetOff(int index, StoreClock clock)
    {
        _devices[index] = _devices[index] with { PlayingUntil = clock.Tick + StoreClock.TicksFor(LurePlaysFor) };
        Emit(new LureSetOff(clock, _devices[index].Id, _devices[index].VillageId));
    }

    private void SetOffLures(int villageId, StoreClock clock)
    {
        for (var d = 0; d < _devices.Count; d++)
            if (_devices[d].VillageId == villageId && _devices[d].Kind == DeviceKind.NoiseLure && _devices[d].PlayingUntil <= clock.Tick)
                SetOff(d, clock);
    }

    /// <summary>A playing noise lure draws a Horde that is not besieging or chasing, if it carries as far as the Horde.</summary>
    private HordeState DrawToLures(HordeState horde, List<Villager> acting, StoreClock clock)
    {
        if (horde.SiegeVillageId >= 0 || horde.ChaseId >= 0 || !_devices.Any(d => d.Kind == DeviceKind.NoiseLure && d.PlayingUntil > clock.Tick)) return horde;
        var lead = acting[0].Position;
        // shortcut: a lure's sound is not muffled by fog or the mattress mountain the way noise is; add it if lures get tuned.
        var reach = LureReach * StaffNoiseSensitivity(_snapshot.SectorAt(lead).Id);
        var lure = _devices.Where(d => d.Kind == DeviceKind.NoiseLure && d.PlayingUntil > clock.Tick && _space.Distance(lead, d.Position) <= reach)
            .MinBy(d => _space.Distance(lead, d.Position));
        // A Horde that has searched round the lure already goes back to its way.
        if (lure is null || horde.Source == lure.Position || _space.Distance(lead, lure.Position) <= SearchRadius * 2) return horde;
        foreach (var s in acting) _staff.Stop(s.Id);
        Emit(new HordeLured(clock, lure.Id, lure.VillageId, horde.Id, lure.Position));
        return horde with { Source = lure.Position, SearchUntil = 0 };
    }

    /// <summary>Whether the Horde is on its way to (or searching round) a noise lure, so it does not besiege on the way.</summary>
    private bool Lured(HordeState horde) => horde.Source is { } source &&
        _devices.Any(d => d.Kind == DeviceKind.NoiseLure && d.PlayingUntil > _tick && d.Position == source);

    /// <summary>
    /// The Horde's Staff walk into a Village's devices: an alarm wakes its Guards and sets off its lures; a trap catches the Staff
    /// near it (a Furniture trap falls on them, hurting and pinning them down; a pit holds them). Whether a trap caught any.
    /// </summary>
    private bool SpringDevices(List<Villager> acting, StoreClock clock)
    {
        var caught = false;
        for (var d = 0; d < _devices.Count; d++)
        {
            var device = _devices[d];
            if (device.Sprung || device.Kind == DeviceKind.NoiseLure) continue;
            var reach = device.Kind == DeviceKind.Alarm ? AlarmSpan : TrapReach;
            // A Staff come for remains laid on a trap as bait springs it too.
            var bait = device.Kind == DeviceKind.Alarm ? [] : _remains.Where(r => _space.Distance(r.Position, device.Position) <= BaitReach).ToList();
            if (acting.FirstOrDefault(s => _space.Distance(s.Position, device.Position) <= reach ||
                    bait.Any(r => _space.Distance(s.Position, r.Position) <= CarryOffReach)) is not { } first) continue;
            _devices[d] = device with { Sprung = true };
            if (device.Kind == DeviceKind.Alarm)
            {
                Emit(new AlarmRaised(clock, device.Id, device.VillageId, first.Id));
                WakeGuards(device.VillageId, clock);
                SetOffLures(device.VillageId, clock);
                continue;
            }
            if (device.Kind == DeviceKind.FurnitureTrap)
            {
                // Taken apart, moved or fallen already: nothing comes down.
                if (!Stacked(device)) continue;
                // The trip line pulls the prop out from under the stack.
                var prop = Piece(device.FurnitureIds[0]);
                MoveFurnitureCore(prop.Id, prop.Position + new Vector2(FurnitureCatalog.Spec(prop).Width + 0.5f, 0), prop.Rotation);
            }
            var victims = acting.Where(s => _space.Distance(s.Position, device.Position) <= TrapReach + 0.5f || s.Id == first.Id).Select(s => s.Id).ToArray();
            foreach (var id in victims)
            {
                var state = _staffStates[id];
                var hurt = device.Kind == DeviceKind.FurnitureTrap ? Math.Max(TrapLeaves, state.Health - TrapBlow) : state.Health;
                _staffStates[id] = state with { Health = Math.Min(state.Health, hurt), FallenUntil = clock.Tick + StoreClock.TicksFor(device.Kind == DeviceKind.Pit ? PitHolds : TrapPins) };
                _staff.Stop(id);
            }
            Emit(new TrapSprung(clock, device.Id, device.Kind, device.VillageId, Array.AsReadOnly(victims),
                bait.Count > 0, device.Position));
            caught = true;
        }
        return caught;
    }

    // A Furniture trap's pieces are all there and the dresser still stands on the prop.
    private bool Stacked(Device trap) => trap.FurnitureIds.All(id => PieceIndex(id) >= 0) && Piece(trap.FurnitureIds[1]).Elevation > 0;

    /// <summary>The Village's unwounded Guards get up and stand guard at the Village until the Store opens.</summary>
    private void WakeGuards(int villageId, StoreClock clock)
    {
        var village = _villages[villageId];
        var woken = village.VillagerIds.Select(_villagers.ViewOf).OfType<Villager>()
            .Where(v => v.Role == Role.Guard && !_wounds.ContainsKey(v.Id) && IsAvailableWorker(v) &&
                v.Action is not (VillagerAction.StandingGuard or VillagerAction.Fighting or VillagerAction.HaulingRemains))
            .Select(v => v.Id).ToArray();
        if (woken.Length == 0) return;
        foreach (var id in woken) _villagers.Engage(id, VillagerAction.StandingGuard, _navigation.NearestWalkable(village.Position));
        Emit(new GuardsWoken(clock, villageId, Array.AsReadOnly(woken)));
    }

    /// <summary>
    /// While the Store is open: Guards go back to their day, alarms and pits are set again, and so is a Furniture trap whose
    /// stack stands again (the Staff Tidied it back up);
    /// and every Store hour or so a Village with late know-how sets more devices, the more the Horde nights it has seen.
    /// </summary>
    private void AdvanceDevices(StoreClock clock)
    {
        if (clock.Phase == DayPhase.ClosingTime || clock.Tick % 50 != 11) return;
        foreach (var guard in _villagers.Snapshot().Where(v => v.Action == VillagerAction.StandingGuard).ToList())
            _villagers.Release(guard.Id, VillagerAction.StandingGuard);
        for (var d = 0; d < _devices.Count; d++)
            if (_devices[d].Sprung && (_devices[d].Kind != DeviceKind.FurnitureTrap || Stacked(_devices[d]))) _devices[d] = _devices[d] with { Sprung = false };
        if (clock.Tick % StoreClock.TicksFor(TimeSpan.FromHours(1)) >= 50) return;
        var target = Math.Min(MaxDevices, HordeNightsSoFar * DevicesPerHordeNight);
        if (target == 0) return;
        foreach (var village in _villages.ToList())
        {
            if (village.Fallen || village.VillagerIds.Count == 0 || !Recipes.Late.Any(r => village.Recipes.Contains(r.Gear))) continue;
            // A sprung Furniture trap is spent until its stack stands again.
            var own = _devices.Where(d => d.VillageId == village.Id && !(d.Kind == DeviceKind.FurnitureTrap && d.Sprung)).ToList();
            if (own.Count >= target) continue;
            // The kind it has fewest of among those it has the Materials for.
            var affordable = DeviceOrder.Where(k => DeviceCost(k) is not ({ } m, var need) || village.Materials.GetValueOrDefault(m) >= need).ToList();
            if (affordable.Count == 0) continue;
            var kind = affordable.MinBy(k => own.Count(d => d.Kind == k));
            var (material, amount) = DeviceCost(kind);
            if (material is { } spent) SetMaterialsCore(village.Id, spent, village.Materials[spent] - amount);
            var belief = HordeBelief(village);
            var towards = _space.Delta(village.Position, belief.Where);
            var direction = towards.LengthSquared() > 0.01f ? Vector2.Normalize(towards) : Vector2.UnitX;
            var side = new Vector2(-direction.Y, direction.X);
            var nth = own.Count(d => d.Kind == kind);
            var spread = side * ((nth % 2 == 0 ? 1 : -1) * 6f * ((nth + 1) / 2));
            var position = kind switch
            {
                DeviceKind.Alarm => direction * AlarmRange + spread,
                DeviceKind.NoiseLure => side * LureRange * (nth % 2 == 0 ? 1 : -1) + direction * 12f * (nth / 2),
                DeviceKind.Pit => direction * TrapRange + side * 8f + spread,
                _ => direction * TrapRange + spread,
            };
            PlaceDevice(village.Id, kind, village.Position + position, kind == DeviceKind.NoiseLure ? nth % 2 : 0, clock);
        }
    }

    private static (MaterialKind?, int) DeviceCost(DeviceKind kind) => kind switch
    {
        DeviceKind.NoiseLure => (MaterialKind.Electrics, 2),
        DeviceKind.Alarm => (MaterialKind.Metal, 1),
        DeviceKind.Pit => (MaterialKind.WoodPanel, 2),
        DeviceKind.FurnitureTrap => (MaterialKind.WoodPanel, 4),
        _ => (null, 0),
    };

    /// <summary>
    /// Where the Village believes the Hordes come from: before any Siege, from the nearest Ball pit (the Staff do come out of
    /// them, but walk their Patrol routes, so it may be false); after one, from where that Siege came.
    /// </summary>
    private Belief HordeBelief(Village village)
    {
        if (village.HordeBelief is { } held) return held;
        var pit = _ballPitNetwork.BallPitIds.Select(id => _pointsOfInterest[id]).MinBy(p => _space.Distance(p.Centre, village.Position));
        var belief = new Belief("The Hordes come from the Ball pit", pit?.Centre ?? village.Position + Vector2.UnitX, 0.4);
        _villages[village.Id] = _villages[village.Id] with { HordeBelief = belief };
        return belief;
    }

    /// <summary>A Siege teaches the Village where the Hordes come from.</summary>
    private void LearnHordeSide(int villageId, Vector2 from)
    {
        var village = _villages[villageId];
        _villages[villageId] = village with { HordeBelief = new("The Hordes come from that side", from, Math.Min(1, (village.HordeBelief?.Confidence ?? 0.4) + 0.2)) };
    }

    /// <summary>An unsprung trap of the Village on the remains' side of it, to leave them on as bait, if it has one.</summary>
    private Device? BaitTrap(Village village, Vector2 remains) => _devices
        .Where(d => d.VillageId == village.Id && d.Kind is DeviceKind.FurnitureTrap or DeviceKind.Pit && !d.Sprung &&
            Vector2.Dot(_space.Delta(village.Position, d.Position), _space.Delta(village.Position, remains)) >= 0 &&
            !_remains.Any(r => r.HaulTo is { } to && _space.Distance(to, d.Position) <= BaitReach))
        .MinBy(d => _space.Distance(d.Position, village.Position));

    private void RestoreDevices(WorldSnapshot world)
    {
        var devices = world.Devices;
        if (devices.Select(d => d.Id).Distinct().Count() != devices.Count ||
            devices.Any(d => !Enum.IsDefined(d.Kind) || d.VillageId < 0 || d.VillageId >= world.Villages.Count ||
                !float.IsFinite(d.Position.X) || !float.IsFinite(d.Position.Y) ||
                (d.Kind == DeviceKind.FurnitureTrap) != (d.FurnitureIds.Count == 3) || d.Kind != DeviceKind.FurnitureTrap && d.FurnitureIds.Count > 0) ||
            devices.SelectMany(d => d.FurnitureIds).Except(world.Furniture.Select(p => p.Id)).Any() ||
            world.Villages.Any(v => v.HordeBelief is { } b && (!float.IsFinite(b.Where.X) || !float.IsFinite(b.Where.Y) || !(b.Confidence is >= 0 and <= 1))))
            throw new InvalidDataException("Invalid world save: inconsistent devices.");
        _devices.AddRange(devices);
    }
}
