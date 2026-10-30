using System.Numerics;

namespace Scp3008.Core;

/// <summary>Power attached to a Lamp; a moved Lamp never draws from the Store again.</summary>
public sealed record LampPower(int FurnitureId, bool TakenFromDisplay = false, bool SwitchedOn = false,
    double BatteryHours = 0, bool GeneratorPowered = false);

/// <summary>A light's footprint and strength, independent of the camera and its real-light budget.</summary>
public sealed record LitArea(int Id, Vector2 Position, float Elevation, float Radius, float Brightness);

public sealed partial class Simulation
{
    private readonly Dictionary<int, LampPower> _lampPower = [];
    private IReadOnlyList<FurniturePiece>? _powerFurniture;
    private FurniturePiece[] _lamps = [];
    private ILookup<ChunkCoord, FurniturePiece>? _lampsByChunk;
    private FurniturePiece[] _villageLamps = [];
    private int _powerVillageCount = -1;

    private FurniturePiece[] Lamps()
    {
        if (!ReferenceEquals(_powerFurniture, _furniture.Pieces))
        {
            _powerFurniture = _furniture.Pieces;
            _lamps = _furniture.Pieces.Where(p => FurnitureCatalog.Spec(p).IsLamp).ToArray();
            _lampsByChunk = _lamps.ToLookup(p => _space.ChunkOf(p.Position));
            var present = _lamps.Select(p => p.Id).ToHashSet();
            foreach (var id in _lampPower.Keys.Where(id => !present.Contains(id)).ToArray()) _lampPower.Remove(id);
            _powerVillageCount = -1;
        }
        if (_powerVillageCount != _villages.Count)
        {
            _powerVillageCount = _villages.Count;
            _villageLamps = _lamps.Where(p => _lampPower.ContainsKey(p.Id) ||
                _villages.Any(v => _space.Distance(v.Position, p.Position) <= 48)).ToArray();
        }
        return _lamps;
    }

    private void AdvancePower(StoreClock clock)
    {
        if (_villages.Count == 0 && _lampPower.Count == 0) return;
        _villagers.AdvanceCranking(clock, _villages, NeedsFridgeCranking);
        var people = _villagers.Snapshot();
        Lamps();
        foreach (var lamp in _villageLamps)
        {
            if (lamp.Damage >= 1 || _snapshot.GroundAt(lamp.Position).HasFlag(GroundProperty.Darkness)) continue;
            _lampPower.TryGetValue(lamp.Id, out var power);
            var home = _villages.Where(v => _space.Distance(v.Position, lamp.Position) <= 24)
                .OrderBy(v => _space.Distance(v.Position, lamp.Position)).FirstOrDefault();
            if (power?.TakenFromDisplay != true)
            {
                var on = clock.Phase == DayPhase.Open && home is not null && people.Any(v =>
                    v.VillageId == home.Id && v.Action != VillagerAction.Resting && _space.Distance(v.Position, lamp.Position) <= 4);
                if (on || power is not null) _lampPower[lamp.Id] = (power ?? new(lamp.Id)) with { SwitchedOn = on };
                continue;
            }
            var generator = home is not null && people.Any(v => v.VillageId == home.Id && v.Action == VillagerAction.Cranking);
            var hours = power.BatteryHours;
            if (clock.Phase == DayPhase.ClosingTime && !generator)
            {
                if (hours <= 1e-9 && home is not null && home.Stores.GetValueOrDefault(GoodsKind.Batteries) > 0)
                {
                    ChangeStores(home.Id, GoodsKind.Batteries, -1);
                    hours = 4;
                }
                hours = Math.Max(0, hours - StoreClock.TickDuration.TotalHours);
            }
            _lampPower[lamp.Id] = power with { BatteryHours = hours, GeneratorPowered = generator };
        }
    }

    private IReadOnlyList<LitArea> LightAreas(WorldSnapshot world, bool local = false)
    {
        var areas = new List<LitArea>();
        var lamps = Lamps();
        var nearby = local ? world.Villagers.SelectMany(v => _space.ChunkImagesAround(v.Position, 16))
            .Select(c => c.Chunk).Distinct().SelectMany(c => _lampsByChunk![c]) : lamps.AsEnumerable();
        foreach (var lamp in nearby)
        {
            var brightness = world.LampLevel(lamp);
            if (brightness > 0) areas.Add(new(lamp.Id, lamp.Position, lamp.Elevation + 1, FurnitureCatalog.Spec(lamp).LightRadius, brightness));
        }
        if (world.Phase == DayPhase.ClosingTime)
            foreach (var person in world.Villagers)
            {
                var hand = person.Hand;
                var candle = hand?.Kind == GearKind.Candle;
                if (candle || hand is { Kind: GearKind.Flashlight or GearKind.Lantern, BatteryHours: > 0 })
                {
                    // Put the bulb beside the hand, outside the figure's shadow-casting torso.
                    var offset = new Vector2(MathF.Sin(person.Heading), -MathF.Cos(person.Heading)) * 0.5f +
                        new Vector2(MathF.Cos(person.Heading), MathF.Sin(person.Heading)) * 0.1f;
                    areas.Add(new(-person.Id - 1, _space.Wrap(person.Position + offset), person.Elevation + 1, candle ? 4 : 10, candle ? 0.3f : 1));
                }
            }
        return areas.AsReadOnly();
    }
}

internal sealed partial class Villagers
{
    public void AdvanceCranking(StoreClock clock, IReadOnlyList<Village> villages, Func<int, bool> needsFridgeCranking)
    {
        foreach (var w in _walkers)
        {
            var home = villages.FirstOrDefault(v => v.Id == w.VillageId);
            var crank = w.Hand?.Kind == GearKind.HandCrankedGenerator &&
                home is not null && space.Distance(w.Position, home.Position) <= 2 && w.Destination is null &&
                w.FurnitureTaskId is null && !w.Crafting && w.ClimbWaypoints.Count == 0 &&
                w.Needs.Rest > (w.Action == VillagerAction.Cranking ? 0.25 : 0.6) && w.Needs.Hunger < 0.7 && w.Needs.Thirst < 0.7 && w.Needs.Safety > 0 &&
                (clock.Phase == DayPhase.ClosingTime || needsFridgeCranking(home.Id));
            if (crank)
            {
                w.Action = VillagerAction.Cranking;
                w.Target = null; w.Waypoints.Clear(); w.Destination = null;
                w.Needs = w.Needs with { Rest = Math.Max(0, w.Needs.Rest - StoreClock.TickDuration.TotalHours / 4) };
            }
            else if (w.Action == VillagerAction.Cranking) w.Action = VillagerAction.Idle;
        }
    }
}
