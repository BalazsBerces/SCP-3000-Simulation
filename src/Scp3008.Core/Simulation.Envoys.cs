using System.Numerics;

namespace Scp3008.Core;

public enum EnvoyOffer { Trade, Alliance, Warning, Help }
public enum MarkKind { Border, Warning }

/// <summary>A Villager walking to another Village with a message, news or an offer; it comes home with the answer.</summary>
/// <param name="Accepted">The answer, known once it has delivered the offer.</param>
public sealed record Envoy(int Id, int FromVillageId, int ToVillageId, int VillagerId, EnvoyOffer Offer, bool Returning, bool? Accepted, Vector2 Position);

/// <summary>A Village's lamp signal from its Tower at Closing time while it is besieged; light does not draw the Staff.</summary>
public sealed record LampSignal(int VillageId, Vector2 Position, IReadOnlyList<int> SeenBy);

/// <summary>A sign painted on a Pillar: a Village's border, or a warning of a Staff seen nearby.</summary>
public sealed record PillarMark(int Id, int VillageId, int PillarId, MarkKind Kind, Vector2 Position, int StaffId, Vector2 Danger);

/// <summary>A Village that knows of another (since someone of theirs met its people or territory).</summary>
public sealed record Contact(int VillageId, int OtherVillageId);

public sealed record FirstContact(StoreClock At, int VillageId, int OtherVillageId) : DomainEvent(At);
public sealed record EnvoySent(StoreClock At, int EnvoyId, int FromVillageId, int ToVillageId, EnvoyOffer Offer, int VillagerId) : DomainEvent(At);
public sealed record EnvoyReturned(StoreClock At, int EnvoyId, int FromVillageId, int ToVillageId, EnvoyOffer Offer, bool Accepted) : DomainEvent(At);
public sealed record EnvoyLost(StoreClock At, int EnvoyId, int FromVillageId, int ToVillageId) : DomainEvent(At);
public sealed record LampSignalSent(StoreClock At, int VillageId) : DomainEvent(At);
public sealed record LampSignalSeen(StoreClock At, int VillageId, int SeenByVillageId, bool SendingHelp) : DomainEvent(At);
public sealed record PillarMarkPainted(StoreClock At, int MarkId, int VillageId, MarkKind Kind, string Pillar) : DomainEvent(At);
public sealed record PillarMarkRead(StoreClock At, int MarkId, int VillagerId, MarkKind Kind) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Sends one of the Village's free Villagers to another Village with an offer.</summary>
    public Envoy SendEnvoy(int fromVillageId, int toVillageId, EnvoyOffer offer) => _simulation.SendEnvoy(fromVillageId, toVillageId, offer);

    /// <summary>Paints a mark on a Pillar for the Village; a warning is about the Staff at <paramref name="danger"/>.</summary>
    public PillarMark PaintMark(int villageId, int pillarId, MarkKind kind, int staffId = -1, Vector2 danger = default) =>
        _simulation.PaintMark(villageId, pillarId, kind, staffId, danger);

    /// <summary>Gives a Village a Tower with its lamp, as if built.</summary>
    public void BuildTower(int villageId) => _simulation.BuildTower(villageId);
}

public sealed partial class Simulation
{
    private const int EnvoyEvery = 5, SignalEvery = 10, DispatchEnvoysEvery = 900, PaintEvery = 500;
    private const float EnvoyReach = 6, SignalRange = 50, MarkReach = 3, MarkPaintRange = 40, SightStep = 2;
    private const int MaxMarks = 200;
    private readonly List<Envoy> _envoys = [];
    private readonly List<LampSignal> _signals = [];
    private readonly List<PillarMark> _marks = [];
    private readonly HashSet<Contact> _contacts = [];
    private int _nextEnvoyId, _nextMarkId;

    internal void BuildTower(int villageId) => Publish(() =>
    {
        var village = _villages[villageId];
        if (!village.Castle.Any(c => c.Part == CastlePart.Tower))
        {
            var slot = new BlueprintSlot(_space.Wrap(village.Position + new Vector2(4, 0)), 0, 0.8f, WallRun.Corner, 1.2f);
            _villages[villageId] = village with { Castle = Array.AsReadOnly(village.Castle.Append(new CastleStructure(CastlePart.Tower, 0, [slot, slot, slot])).ToArray()) };
        }
        var c = _villages[villageId].Castle.ToList().FindIndex(s => s.Part == CastlePart.Tower);
        for (var i = 0; i < 3; i++)
        {
            var slot = _villages[villageId].Castle[c].Slots[i];
            if (slot.FurnitureId is not null) continue;
            var piece = PlaceFurniture(FurnitureType.Wardrobe, slot.Position, slot.Rotation, 0, 0, i * 0.5f);
            SetCastleSlot(villageId, c, i, slot with { Elevation = i * 0.5f, FurnitureId = piece.Id, ReservedFurnitureId = null });
        }
        return true;
    });

    internal Envoy SendEnvoy(int fromId, int toId, EnvoyOffer offer) => Publish(() =>
    {
        if (fromId == toId || fromId < 0 || toId < 0 || fromId >= _villages.Count || toId >= _villages.Count)
            throw new ArgumentException("An Envoy goes between two different Villages.");
        var people = _villagers.Snapshot();
        var sender = _villages[fromId].VillagerIds.Select(id => people.FirstOrDefault(p => p.Id == id)).OfType<Villager>()
            .Where(p => IsAvailableWorker(p) && p.Action != VillagerAction.Trading && !_wounds.ContainsKey(p.Id) &&
                _villages[fromId].Captives.All(c => c.VillagerId != p.Id))
            .OrderBy(p => offer == EnvoyOffer.Help ? p.Role != Role.Guard : p.Role != Role.Trader).FirstOrDefault()
            ?? throw new InvalidOperationException("No free Villager to send as an Envoy.");
        var envoy = new Envoy(_nextEnvoyId++, fromId, toId, sender.Id, offer, false, null, sender.Position);
        _envoys.Add(envoy);
        _villagers.Engage(sender.Id, VillagerAction.Trading, _villages[toId].Position);
        Emit(new EnvoySent(new StoreClock(_tick), envoy.Id, fromId, toId, offer, sender.Id));
        return envoy;
    });

    internal PillarMark PaintMark(int villageId, int pillarId, MarkKind kind, int staffId, Vector2 danger) => Publish(() =>
    {
        if (villageId < 0 || villageId >= _villages.Count || pillarId < 0 || pillarId >= _pillars.Count)
            throw new ArgumentException("A mark needs a Village and a Pillar.");
        var pillar = _pillars[pillarId];
        var mark = new PillarMark(_nextMarkId++, villageId, pillarId, kind, pillar.Position, staffId, danger);
        _marks.Add(mark);
        Emit(new PillarMarkPainted(new StoreClock(_tick), mark.Id, villageId, kind, pillar.Coordinate));
        return mark;
    });

    // Whether no Pillar stands between two places.
    private bool LineClear(Vector2 a, Vector2 b)
    {
        var delta = _space.Delta(a, b);
        var steps = (int)(delta.Length() / SightStep) + 1;
        for (var i = 1; i < steps; i++)
            if (InPillar(_space.Wrap(a + delta * (i / (float)steps)), 0)) return false;
        return true;
    }

    private void Learn(int villageId, KnownMap news)
    {
        var village = _villages[villageId];
        _villages[villageId] = village with { KnownMap = MergeMaps([village.KnownMap, news]) };
        _villagers.ShareKnowledge(village.VillagerIds, news);
    }

    private KnownMap About(Village village) => new() { Villages = [new KnownVillage(village.Id, village.Position, new StoreClock(_tick))] };

    private void AdvanceEnvoys(StoreClock clock)
    {
        if (_tick % DispatchEnvoysEvery == 11 && clock.Phase != DayPhase.ClosingTime) DispatchEnvoys();
        if (_tick % PaintEvery == 13) PaintMarks();
        if (_tick % SignalEvery == 4) { NoteContacts(clock); AdvanceSignals(clock); ReadMarks(clock); }
        if (_envoys.Count == 0 || _tick % EnvoyEvery != 3) return;
        foreach (var listed in _envoys.ToArray())
        {
            var i = _envoys.IndexOf(listed);
            var envoy = listed;
            var person = _villagers.ViewOf(envoy.VillagerId);
            if (person is null)
            {
                _envoys.RemoveAt(i);
                Emit(new EnvoyLost(clock, envoy.Id, envoy.FromVillageId, envoy.ToVillageId));
                continue;
            }
            _envoys[i] = envoy = envoy with { Position = person.Position };
            var goal = _villages[envoy.Returning ? envoy.FromVillageId : envoy.ToVillageId].Position;
            if (_space.Distance(person.Position, goal) > EnvoyReach)
            {
                if (person.Destination is null) _villagers.Engage(envoy.VillagerId, VillagerAction.Trading, goal);
                continue;
            }
            if (envoy.Returning)
            {
                _envoys.RemoveAt(i);
                _villagers.Release(envoy.VillagerId, VillagerAction.Trading);
                Emit(new EnvoyReturned(clock, envoy.Id, envoy.FromVillageId, envoy.ToVillageId, envoy.Offer, envoy.Accepted == true));
                continue;
            }
            var to = _villages[envoy.ToVillageId];
            var from = _villages[envoy.FromVillageId];
            var accepted = !to.Fallen && to.VillagerIds.Count > 0 &&
                (envoy.Offer is EnvoyOffer.Warning or EnvoyOffer.Help || to.Personality is not (Personality.Raiders or Personality.Isolationists));
            if (!to.Fallen)
            {
                Learn(to.Id, About(from));
                // A warning carries the sender's fresh knowledge of danger.
                if (envoy.Offer == EnvoyOffer.Warning) Learn(to.Id, new KnownMap { Dangers = from.KnownMap.Dangers.Where(d => !d.Stale).ToArray() });
            }
            Learn(from.Id, About(to));
            if (accepted && envoy.Offer == EnvoyOffer.Alliance) FormAlliance(from.Id, to.Id, clock);
            _envoys[i] = envoy with { Returning = true, Accepted = accepted };
            _villagers.Engage(envoy.VillagerId, VillagerAction.Trading, from.Position);
        }
    }

    // Traders send an Envoy with a trade offer to the nearest Village they know, now and again.
    private void DispatchEnvoys()
    {
        foreach (var from in _villages.ToArray())
        {
            if (from.Personality != Personality.Traders || from.Fallen || from.VillagerIds.Count < 2 || _envoys.Any(e => e.FromVillageId == from.Id)) continue;
            var target = from.KnownMap.Villages.Where(v => !v.Abandoned && v.VillageId != from.Id && !_villages[v.VillageId].Fallen)
                .OrderBy(v => _space.Distance(from.Position, v.Position)).FirstOrDefault();
            if (target is null) continue;
            var ally = !from.Allies.Contains(target.VillageId) && _villages[target.VillageId].Personality is not (Personality.Raiders or Personality.Isolationists);
            try { SendEnvoy(from.Id, target.VillageId, ally ? EnvoyOffer.Alliance : EnvoyOffer.Trade); }
            catch (InvalidOperationException) { /* nobody free to go */ }
        }
    }

    // A Village learns of another only through its people: this notes the first time its Known map holds one.
    private void NoteContacts(StoreClock clock)
    {
        foreach (var village in _villages)
            foreach (var known in village.KnownMap.Villages)
                if (known.VillageId != village.Id && _contacts.Add(new Contact(village.Id, known.VillageId)))
                    Emit(new FirstContact(clock, village.Id, known.VillageId));
    }

    private void AdvanceSignals(StoreClock clock)
    {
        _signals.RemoveAll(s => !_hordes.Any(h => h.SiegeVillageId == s.VillageId));
        foreach (var besieged in _villages.Where(v => !v.Fallen && _hordes.Any(h => h.SiegeVillageId == v.Id) && _signals.All(s => s.VillageId != v.Id)).ToArray())
        {
            if (besieged.Castle.FirstOrDefault(c => c.Part == CastlePart.Tower && c.Complete) is not { } tower) continue;
            _signals.Add(new LampSignal(besieged.Id, tower.Slots[0].Position, []));
            Emit(new LampSignalSent(clock, besieged.Id));
        }
        for (var i = 0; i < _signals.Count; i++)
        {
            var signal = _signals[i];
            foreach (var other in _villages.Where(v => v.Id != signal.VillageId && !v.Fallen && v.VillagerIds.Count > 0 && !signal.SeenBy.Contains(v.Id) &&
                _space.Distance(v.Position, signal.Position) <= SignalRange && LineClear(v.Position, signal.Position)).ToArray())
            {
                _signals[i] = signal = signal with { SeenBy = [.. signal.SeenBy, other.Id] };
                Learn(other.Id, About(_villages[signal.VillageId]));
                var help = other.Personality is not (Personality.Isolationists or Personality.Raiders) && other.VillagerIds.Count >= 3 &&
                    !_hordes.Any(h => h.SiegeVillageId == other.Id) && !_envoys.Any(e => e.FromVillageId == other.Id);
                if (help)
                    try { SendEnvoy(other.Id, signal.VillageId, EnvoyOffer.Help); }
                    catch (InvalidOperationException) { help = false; }
                Emit(new LampSignalSeen(clock, signal.VillageId, other.Id, help));
            }
        }
    }

    // Villages mark the Pillar nearest them as a border, and the Pillar nearest a Staff they know of as a warning.
    private void PaintMarks()
    {
        Pillar? Nearest(Vector2 at) => _pillars.OrderBy(p => _space.Distance(p.Position, at)).FirstOrDefault(p => _space.Distance(p.Position, at) <= MarkPaintRange);
        foreach (var village in _villages.Where(v => !v.Fallen && v.VillagerIds.Count > 0).ToArray())
        {
            if (_marks.Count >= MaxMarks) return;
            if (Nearest(village.Position) is { } border && !_marks.Any(m => m.VillageId == village.Id && m.Kind == MarkKind.Border))
                PaintMark(village.Id, border.Id, MarkKind.Border, -1, default);
            foreach (var danger in village.KnownMap.Dangers.Where(d => !d.Stale))
                if (Nearest(danger.Position) is { } pillar && !_marks.Any(m => m.VillageId == village.Id && m.Kind == MarkKind.Warning && m.StaffId == danger.StaffId))
                    PaintMark(village.Id, pillar.Id, MarkKind.Warning, danger.StaffId, danger.Position);
        }
    }

    // Villagers passing a Pillar read its marks: a warning tells of the Staff, a border of the Village.
    private void ReadMarks(StoreClock clock)
    {
        if (_marks.Count == 0) return;
        var people = _villagers.Snapshot();
        foreach (var mark in _marks.ToArray())
        {
            var reach = _pillars[mark.PillarId].Width / 2 + MarkReach;
            foreach (var person in people.Where(p => p.Elevation == 0 && !_villages[mark.VillageId].VillagerIds.Contains(p.Id) &&
                _space.Delta(mark.Position, p.Position) is var d && MathF.Abs(d.X) <= reach && MathF.Abs(d.Y) <= reach))
            {
                var warning = mark.Kind == MarkKind.Warning;
                if (warning ? person.KnownMap.Dangers.Any(d => d.StaffId == mark.StaffId) : person.KnownMap.Villages.Any(v => v.VillageId == mark.VillageId))
                    continue;
                _villagers.ShareKnowledge([person.Id], warning
                    ? new KnownMap { Dangers = [new KnownDanger(mark.StaffId, mark.Danger, clock)] }
                    : About(_villages[mark.VillageId]));
                Emit(new PillarMarkRead(clock, mark.Id, person.Id, mark.Kind));
            }
        }
    }

    private void RestoreEnvoys(WorldSnapshot s)
    {
        if (s.Envoys.Any(e => e.FromVillageId < 0 || e.FromVillageId >= s.Villages.Count || e.ToVillageId < 0 || e.ToVillageId >= s.Villages.Count) ||
            s.Marks.Any(m => m.VillageId < 0 || m.VillageId >= s.Villages.Count || m.PillarId < 0 || m.PillarId >= s.Pillars.Count) ||
            s.Signals.Any(g => g.VillageId < 0 || g.VillageId >= s.Villages.Count) ||
            s.Contacts.Any(c => c.VillageId < 0 || c.VillageId >= s.Villages.Count) ||
            s.Envoys.Select(e => e.Id).Distinct().Count() != s.Envoys.Count)
            throw new InvalidDataException("Invalid world save: inconsistent Envoys or marks.");
        _envoys.AddRange(s.Envoys);
        _signals.AddRange(s.Signals);
        _marks.AddRange(s.Marks);
        _contacts.UnionWith(s.Contacts);
        _nextEnvoyId = s.Envoys.Select(e => e.Id + 1).DefaultIfEmpty().Max();
        _nextMarkId = s.Marks.Select(m => m.Id + 1).DefaultIfEmpty().Max();
    }
}
