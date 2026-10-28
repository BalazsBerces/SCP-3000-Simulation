using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private const int SaveVersion = 11;
    private static readonly JsonSerializerOptions SaveJson = CreateSaveJson();

    private sealed record SaveFile(int Version, SaveState State);
    private sealed record StockTarget(int FurnitureId, GoodsKind Kind, int Amount);
    private sealed record PatrolState(int Id, System.Numerics.Vector2[] Route, int Next);
    // Extend this state and the subsystem states whenever a ticket adds mutable world state.
    private sealed record SaveState(WorldConfig Config, WorldSnapshot Snapshot, Weather.State Weather,
        Villagers.State Villagers, Villagers.State Staff, PatrolState[] Patrols,
        TimeSpan UnspentRealTime, int NextBandId, FurniturePiece[] RestockOrigins,
        MissingFurniture[] MissingFurniture, int NextFurnitureId, double RestockHours,
        double NextFurnitureCheck, StockTarget[] StockTargets, Dictionary<int, double> GoodsRestockCredit,
        ulong RestockRandom, DrySpell? DrySpell, double NextDrySpellHour, Gear[] GearSources, int NextGearId,
        ulong ArrivalRandom, long NextArrivalTick, SpoilingShelf[] SpoilingShelves);
    private sealed record SpoilingShelf(int FurnitureId, GoodsKind Kind, int Units, float Progress);

    /// <summary>Saves the entire world at a tick boundary; leaves the caller's stream open.</summary>
    public void Save(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_stepLock)
        {
            ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
            var state = new SaveState(_config, BuildSnapshot(), _weather.Save(), _villagers.Save(), _staff.Save(),
                _patrols.Select(p => new PatrolState(p.Id, p.Route, p.Next)).ToArray(), _unspentRealTime,
                _nextBandId, _restockOrigins.Values.ToArray(), _missingFurniture.ToArray(), _nextFurnitureId,
                _restockHours, _nextFurnitureCheck,
                _stockTargets.Select(t => new StockTarget(t.Key.FurnitureId, t.Key.Kind, t.Value)).ToArray(),
                new(_goodsRestockCredit), _restockRandom.State, _drySpell, _nextDrySpellHour, _gearSources.ToArray(), _nextGearId,
                _arrivalRandom.State, _nextArrivalTick,
                _spoilingShelf.Select(s => new SpoilingShelf(s.Key.FurnitureId, s.Key.Kind, s.Value.Units, s.Value.Progress)).ToArray());
            JsonSerializer.Serialize(destination, new SaveFile(SaveVersion, state), SaveJson);
        }
    }

    /// <summary>Loads a versioned world without starting its thread; leaves the caller's stream open.</summary>
    /// <exception cref="InvalidDataException">The save is damaged or its version is incompatible.</exception>
    public static Simulation Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var document = JsonDocument.Parse(source);
            if (!document.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number))
                throw new InvalidDataException("Invalid world save: missing format version.");
            if (number != SaveVersion)
                throw new InvalidDataException($"Incompatible world save version {number}; this build supports version {SaveVersion}.");
            var file = document.Deserialize<SaveFile>(SaveJson) ?? throw new JsonException("Empty save.");
            return new Simulation(file.State);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NullReferenceException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Invalid or damaged world save: " + error.Message, error);
        }
    }

    private Simulation(SaveState state)
    {
        var s = state.Snapshot;
        _config = state.Config;
        _config.Needs?.Validate();
        _config.Restock?.Validate();
        _config.Arrivals?.Validate();
        if (state.NextArrivalTick <= s.Clock.Tick)
            throw new InvalidDataException("Invalid world save: arrival schedule is in the past.");
        _arrivalRandom = new SeededRandom(state.ArrivalRandom);
        _nextArrivalTick = state.NextArrivalTick;
        var furnitureIds = s.Furniture.Select(p => p.Id).ToHashSet();
        if (!Enum.IsDefined(_config.PillarLayout) || !Enum.IsDefined(s.Speed) || s.Clock.Tick < 0 ||
            s.Seed != _config.Seed || s.PillarLayout != _config.PillarLayout || s.Space != _config.PillarLayout.Space() ||
            state.UnspentRealTime < TimeSpan.Zero || state.UnspentRealTime >= RealTimeStep ||
            !double.IsFinite(state.RestockHours) || state.RestockHours < 0 ||
            furnitureIds.Count != s.Furniture.Count ||
            s.Villages.Where((v, i) => v.Id != i).Any() ||
            s.Villages.Any(v => v.Materials.Any(m => !Enum.IsDefined(m.Key) || m.Value < 0)) ||
            s.Furniture.Any(p => p.Id < 0 || !float.IsFinite(p.Position.X) || !float.IsFinite(p.Position.Y) ||
                !float.IsFinite(p.Elevation) || !float.IsFinite(p.Rotation)) ||
            s.Stock.Any(g => g.Amount < 0 || !furnitureIds.Contains(g.FurnitureId)))
            throw new InvalidDataException("Invalid world save: inconsistent world state.");
        _space = s.Space;
        _departments = s.Departments;
        _furniture = new(s.Furniture, s.Displays, s.CellLayouts);
        _pillars = s.Pillars;
        _sectors = s.Sectors;
        _landmarks = s.Landmarks;
        _pointsOfInterest = s.PointsOfInterest;
        _ballPitNetwork = s.BallPitNetwork;
        _tankFish = s.TankFish;
        _counterMeals = s.CounterMeals;
        _campGoods = s.CampGoods;
        _stock = s.Stock;
        _sectorStocks = s.SectorStocks;
        _waterPoints = s.WaterPoints;
        _tick = s.Clock.Tick;
        _speed = s.Speed;
        _unspentRealTime = state.UnspentRealTime;
        _weather = new Weather(_space, new SeededRandom(0));
        _weather.Restore(state.Weather);
        _navigation = new Navigation(_space, () => _furniture.Pieces, _pillars);
        _villagers = new Villagers(_space, _navigation);
        _villagers.Restore(state.Villagers);
        _staff = new Villagers(_space, _navigation);
        _staff.Restore(state.Staff);
        var people = _villagers.Snapshot();
        if (!MembershipConsistent(people, s.Villages, s.Bands, state.NextBandId))
            throw new InvalidDataException("Invalid world save: inconsistent community membership.");
        var staffIds = _staff.Snapshot().Select(v => v.Id).ToHashSet();
        if (state.Patrols.Any(p => !staffIds.Contains(p.Id) || p.Route.Length == 0 || p.Next < 0 ||
                p.Next >= p.Route.Length || p.Route.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y))))
            throw new InvalidDataException("Invalid world save: inconsistent Staff patrol.");
        RestoreFurnitureTasks(s.FurnitureTasks, s.Villages);
        _bands.AddRange(s.Bands);
        _villages.AddRange(s.Villages);
        ValidateBlueprints();
        ValidateMessHalls();
        ValidateStorehouses();
        ValidateFurnitureYards();
        if (state.SpoilingShelves.Any(h => !Enum.IsDefined(h.Kind) || GoodsCatalog.ClassOf(h.Kind) != GoodsClass.Food || h.Units <= 0 ||
                !float.IsFinite(h.Progress) || h.Progress < 0 || h.Progress >= 1 || !furnitureIds.Contains(h.FurnitureId)) ||
            state.SpoilingShelves.Select(h => (h.FurnitureId, h.Kind)).Distinct().Count() != state.SpoilingShelves.Length)
            throw new InvalidDataException("Invalid world save: inconsistent spoiling stock.");
        foreach (var h in state.SpoilingShelves) _spoilingShelf[(h.FurnitureId, h.Kind)] = (h.Units, h.Progress);
        ValidateOutposts();
        _chronicle.AddRange(s.Chronicle);
        foreach (var gear in s.Gear.Concat(s.Villages.SelectMany(v => v.Gear)).Concat(state.GearSources)) gear.Validate();
        var liveGear = s.Gear.Concat(s.Villages.SelectMany(v => v.Gear)).Concat(s.Villagers.SelectMany(v => new[] { v.Hand, v.Bag, v.Armour }).OfType<Gear>()).ToArray();
        if (liveGear.Select(g => g.Id).Distinct().Count() != liveGear.Length || state.NextGearId < 0 ||
            liveGear.Concat(state.GearSources).Any(g => g.Id >= state.NextGearId))
            throw new InvalidDataException("Invalid world save: inconsistent Gear ids.");
        RestoreCrafting(s);
        RestoreKnowHow(s);
        _gear.AddRange(s.Gear);
        _gearSources.AddRange(state.GearSources);
        _nextGearId = state.NextGearId;
        _patrols.AddRange(state.Patrols.Select(p => (p.Id, p.Route, p.Next)));
        _nextBandId = state.NextBandId;
        foreach (var piece in state.RestockOrigins) _restockOrigins.Add(piece.Id, piece);
        _missingFurniture.AddRange(state.MissingFurniture);
        _nextFurnitureId = state.NextFurnitureId;
        _restockHours = state.RestockHours;
        _nextFurnitureCheck = state.NextFurnitureCheck;
        foreach (var target in state.StockTargets) _stockTargets.Add((target.FurnitureId, target.Kind), target.Amount);
        foreach (var credit in state.GoodsRestockCredit) _goodsRestockCredit.Add(credit.Key, credit.Value);
        _restockRandom = new SeededRandom(state.RestockRandom);
        _drySpell = state.DrySpell;
        _nextDrySpellHour = state.NextDrySpellHour;
        Scenario = new ScenarioBuilder(this);
        _snapshot = BuildSnapshot();
    }

    // Every Villager belongs to at most one Village or Band, and both sides of each membership agree.
    private static bool MembershipConsistent(IReadOnlyList<Villager> people, IReadOnlyList<Village> villages,
        IReadOnlyList<Band> bands, int nextBandId) =>
        !(people.Any(v => v.VillageId is { } id &&
                !villages.Any(home => home.Id == id && home.VillagerIds.Contains(v.Id))) ||
            villages.Any(home => home.VillagerIds.Distinct().Count() != home.VillagerIds.Count ||
                people.Any(v => home.VillagerIds.Contains(v.Id) && v.VillageId != home.Id)) ||
            nextBandId < 0 || bands.Any(b => b.Id < 0 || b.Id >= nextBandId) ||
            bands.Select(b => b.Id).Distinct().Count() != bands.Count ||
            bands.SelectMany(b => b.VillagerIds).Distinct().Count() != bands.Sum(b => b.VillagerIds.Count) ||
            bands.Any(b => people.Any(v => b.VillagerIds.Contains(v.Id) && v.VillageId is not null)));

    private static JsonSerializerOptions CreateSaveJson()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            foreach (var property in type.Properties.Where(p => p.Set is not null))
                property.IsRequired = true;
        });
        return new JsonSerializerOptions
        {
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new DepartmentMapConverter() },
        };
    }

    private sealed class DepartmentMapConverter : JsonConverter<DepartmentMap>
    {
        private sealed record State(float CellSize, int CellsPerSide, int[] Cells, Department[] All);

        public override DepartmentMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var state = JsonSerializer.Deserialize<State>(ref reader, options) ?? throw new JsonException("Missing Departments.");
            if (!float.IsFinite(state.CellSize) || state.CellSize <= 0 || state.CellsPerSide <= 0 ||
                state.Cells.LongLength != (long)state.CellsPerSide * state.CellsPerSide ||
                state.Cells.Any(id => id < 0 || id >= state.All.Length))
                throw new JsonException("Invalid Department cells.");
            return new DepartmentMap(state.CellSize, state.CellsPerSide, state.Cells, state.All);
        }

        public override void Write(Utf8JsonWriter writer, DepartmentMap value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new State(value.CellSize, value.CellsPerSide,
                Enumerable.Range(0, value.CellsPerSide * value.CellsPerSide)
                    .Select(i => value.AtCell(i % value.CellsPerSide, i / value.CellsPerSide).Id).ToArray(),
                value.All.ToArray()), options);
    }
}
