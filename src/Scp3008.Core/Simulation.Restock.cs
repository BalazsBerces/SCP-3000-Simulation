using Scp3008.Core.Generation;
using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private readonly Dictionary<int, FurniturePiece> _restockOrigins = [];
    private readonly List<MissingFurniture> _missingFurniture = [];
    private int _nextFurnitureId;
    private sealed record MissingFurniture(FurniturePiece Piece, WaterPoint[] Water, double ReadyHour);
    private double _restockHours;
    private double _nextFurnitureCheck = double.PositiveInfinity;
    private readonly Dictionary<(int FurnitureId, GoodsKind Kind), int> _stockTargets = [];
    private readonly Dictionary<int, double> _goodsRestockCredit = [];
    private readonly SeededRandom _restockRandom;
    private DrySpell? _drySpell;
    private double _nextDrySpellHour;

    private void ScheduleDrySpell()
    {
        var interval = (_config.Restock ?? new RestockConfig()).DrySpellIntervalHours;
        _nextDrySpellHour = interval == 0 ? double.PositiveInfinity :
            _restockHours + interval * (0.5 + _restockRandom.NextFloat());
    }

    private double RestockMultiplier(int departmentId) => _drySpell is not { } spell ? 1 :
        departmentId == spell.DepartmentId ? 1 - spell.Strength :
        departmentId == spell.BoostedDepartmentId ? 1 + spell.Strength : 1;

    private void AdvanceDrySpell()
    {
        var now = new StoreClock(_tick);
        if (_drySpell is { } ended && now.Tick >= ended.EndsAt.Tick)
        {
            _drySpell = null;
            Emit(new DrySpellEnded(now, ended));
            ScheduleDrySpell();
        }
        if (_drySpell is not null || _restockHours + 1e-9 < _nextDrySpellHour)
            return;
        // Choose actual Restock sources, and never slow the only Department that can supply food.
        var sources = _stockTargets.Keys.Where(k => _restockOrigins.ContainsKey(k.FurnitureId)).ToArray();
        var departments = sources.Select(k => _departments.At(_restockOrigins[k.FurnitureId].Position).Id).Distinct().ToArray();
        var food = sources.Where(k => GoodsCatalog.ClassOf(k.Kind) == GoodsClass.Food)
            .Select(k => _departments.At(_restockOrigins[k.FurnitureId].Position).Id).Distinct().ToArray();
        var candidates = departments.Where(id => food.Length != 1 || id != food[0]).ToArray();
        if (departments.Length < 2 || candidates.Length == 0)
        {
            ScheduleDrySpell();
            return;
        }
        var affected = _restockRandom.Pick(candidates);
        var boosted = _restockRandom.Pick(departments.Where(id => id != affected).ToArray());
        var config = _config.Restock ?? new RestockConfig();
        _drySpell = new DrySpell(affected, boosted, now,
            new StoreClock(_tick + StoreClock.TicksFor(TimeSpan.FromHours(config.DrySpellDurationHours))), config.DrySpellStrength);
        Emit(new DrySpellStarted(now, _drySpell));
    }

    private HashSet<int>? _restockedPlaces;
    private HashSet<int>? _restockedLandmarks;

    /// <summary>
    /// Whether Restock looks after a piece: only the Cafeterias and the Camping ground are restocked, their Furniture
    /// coming back and their Goods laid again; everywhere else what is taken stays gone.
    /// </summary>
    private bool Restocked(FurniturePiece piece)
    {
        _restockedPlaces ??= _pointsOfInterest.Where(p => p.Type == PointOfInterestType.Cafeteria).Select(p => p.Id).ToHashSet();
        _restockedLandmarks ??= _landmarks.Where(l => l.Type == LandmarkType.CampingGround).Select(l => l.Id).ToHashSet();
        return piece.PointOfInterestId >= 0 && _restockedPlaces.Contains(piece.PointOfInterestId) ||
            piece.LandmarkId >= 0 && _restockedLandmarks.Contains(piece.LandmarkId);
    }

    private void InitializeRestock()
    {
        foreach (var piece in _furniture.Pieces.Where(Restocked))
            _restockOrigins[piece.Id] = piece;
        _nextFurnitureId = _furniture.Pieces.Count == 0 ? 0 : _furniture.Pieces.Max(p => p.Id) + 1;
        // Give initially bare Stock Furniture a target too, using the same catalogue and placement rules as generation.
        var fullStock = StockGenerator.Generate(_config.Seed, StockConfig with { StockedShelfShare = 1, SectorGuarantee = false },
            _furniture.Pieces, _furniture.Displays, _departments, _pointsOfInterest, _tankFish, _counterMeals, _campGoods,
            _landmarks, _space, _config.PillarLayout);
        var initial = _stock.Where(g => !g.LeftBehind).ToLookup(g => g.FurnitureId);
        foreach (var group in fullStock.Where(g => !g.LeftBehind && _restockOrigins.ContainsKey(g.FurnitureId)).GroupBy(g => g.FurnitureId))
            foreach (var goods in initial.Contains(group.Key) ? initial[group.Key] : group)
                _stockTargets[(goods.FurnitureId, goods.Kind)] = goods.Amount;
        foreach (var goods in _stock.Where(g => !g.LeftBehind && _restockOrigins.ContainsKey(g.FurnitureId)))
            _stockTargets[(goods.FurnitureId, goods.Kind)] = goods.Amount;
    }

    private void AdvanceRestock()
    {
        _restockHours += StoreClock.TickDuration.TotalHours;
        AdvanceDrySpell();
        if (_drySpell is not null && _missingFurniture.Count > 0)
        {
            for (var i = 0; i < _missingFurniture.Count; i++)
            {
                var missing = _missingFurniture[i];
                var multiplier = RestockMultiplier(_departments.At(missing.Piece.Position).Id);
                _missingFurniture[i] = missing with { ReadyHour = missing.ReadyHour +
                    StoreClock.TickDuration.TotalHours * (1 - multiplier) };
            }
            _nextFurnitureCheck = _missingFurniture.Min(m => m.ReadyHour);
        }
        RestoreFurniture();
        var rate = (_config.Restock ?? new RestockConfig()).GoodsPerStoreHour;
        if (rate == 0)
            return;
        var refill = new Dictionary<int, double>();
        foreach (var department in _departments.All)
        {
            var credit = _goodsRestockCredit.GetValueOrDefault(department.Id) + StoreClock.TickDuration.TotalHours *
                rate * RestockMultiplier(department.Id);
            var units = Math.Floor(credit + 1e-9);
            _goodsRestockCredit[department.Id] = Math.Max(0, credit - units);
            if (units > 0)
                refill[department.Id] = units;
        }
        if (refill.Count == 0)
            return;
        bool present(int id) => PieceIndex(id) >= 0;
        RestockGear(refill, present);
        var stock = _stock.ToDictionary(g => (g.FurnitureId, g.Kind, g.LeftBehind));
        var changed = false;
        foreach (var (key, capacity) in _stockTargets)
        {
            if (!present(key.FurnitureId) || !_restockOrigins.ContainsKey(key.FurnitureId))
                continue;
            var units = refill.GetValueOrDefault(_departments.At(_restockOrigins[key.FurnitureId].Position).Id);
            if (units == 0)
                continue;
            var lookup = (key.FurnitureId, key.Kind, false);
            var amount = stock.GetValueOrDefault(lookup).Amount;
            if (amount >= capacity)
                continue;
            stock[lookup] = new StockedGoods(key.FurnitureId, key.Kind, (int)Math.Min(capacity, amount + units));
            changed = true;
        }
        if (changed)
        {
            _stock = stock.Values.ToList().AsReadOnly();
            _stockChanged = true;
        }
    }

    private void VacateFurniture(int id)
    {
        if (_restockOrigins.Remove(id, out var original) && !FurnitureCatalog.Spec(original).IsFixed)
        {
            var config = _config.Restock ?? new RestockConfig();
            var delay = original.LandmarkId >= 0 ? config.LandmarkFurnitureHours : config.FurnitureHours;
            if (delay == 0)
                return;
            var ready = _restockHours + delay;
            _missingFurniture.Add(new MissingFurniture(original, _waterPoints.Where(w => w.FurnitureId == id).ToArray(), ready));
            _nextFurnitureCheck = Math.Min(_nextFurnitureCheck, ready);
        }
    }

    private FurnitureIndex? _piecesIndexStore;
    // The current Furniture by chunk, for questions about one place; bring it up to date before use.
    private FurnitureIndex _piecesIndex => _piecesIndexStore ??= new FurnitureIndex(_space);

    private readonly List<FurniturePiece> _restockBlockers = [];
    // The Furniture that may hide pieces from each Villager, by id, while the Furniture and the Villager stay as they were.
    private readonly Dictionary<int, (SightBlockers Sight, IReadOnlyList<FurniturePiece> Furniture, Vector2 Position, float Extent)> _restockSight = [];

    private SightBlockers SightAround(Villager villager)
    {
        var extent = villager.ViewRange + _piecesIndex.LargestPiece;
        if (_restockSight.TryGetValue(villager.Id, out var known) && ReferenceEquals(known.Furniture, _furniture.Pieces) &&
            known.Position == villager.Position && known.Extent == extent)
            return known.Sight;
        var sight = known.Sight ?? new SightBlockers();
        _restockBlockers.Clear();
        _restockBlockers.AddRange(_piecesIndex.Near(villager.Position, extent));
        sight.Prepare(_space, villager.Position, _restockBlockers, extent);
        _restockSight[villager.Id] = (sight, _furniture.Pieces, villager.Position, extent);
        return sight;
    }

    private void RestoreFurniture()
    {
        if (_missingFurniture.Count == 0 || _restockHours + 1e-9 < _nextFurnitureCheck)
            return;
        var villagers = _villagers.Snapshot();
        var fog = _weather.Snapshot();
        foreach (var missing in _missingFurniture.ToArray())
        {
            var piece = missing.Piece;
            if (_restockHours + 1e-9 < missing.ReadyHour)
                continue;
            // Keep generated overlaps, such as boxes on racks and chairs under tables; moved neighbours still block return.
            bool OriginalNeighbour(FurniturePiece p) => _restockOrigins.TryGetValue(p.Id, out var origin) && p == origin &&
                ((piece.DisplayId >= 0 && p.DisplayId == piece.DisplayId) ||
                 (piece.LandmarkId >= 0 && p.LandmarkId == piece.LandmarkId) ||
                 (piece.PointOfInterestId >= 0 && p.PointOfInterestId == piece.PointOfInterestId));
            // Only pieces within both boxes' reach can overlap it, and only those within sight range can hide it.
            _piecesIndex.Update(_furniture.Pieces);
            var spec = FurnitureCatalog.Spec(piece);
            var reach = new System.Numerics.Vector3(spec.Width, spec.Depth, spec.Height).Length() + _piecesIndex.LargestPiece;
            var occupied = _piecesIndex.Near(piece.Position, reach).Any(p => !OriginalNeighbour(p) && RestockSight.Overlaps(_space, piece, p)) ||
                villagers.Any(v => _space.Distance(v.Position, piece.Position) < Math.Max(FurnitureCatalog.Spec(piece).Width, FurnitureCatalog.Spec(piece).Depth) / 2 + 0.4f);
            if (occupied)
                continue;
            if (villagers.Any(v => _space.Distance(v.Position, piece.Position) <= v.ViewRange &&
                    RestockSight.CanSee(_space, v, piece, SightAround(v), _pillars, fog)))
                continue;
            var restored = PieceIndex(piece.Id) >= 0 ? piece with { Id = _nextFurnitureId++ } : piece;
            if (restored.Id != piece.Id)
            {
                foreach (var target in _stockTargets.Where(t => t.Key.FurnitureId == piece.Id).ToArray())
                    _stockTargets[(restored.Id, target.Key.Kind)] = target.Value;
                _tankFish = _tankFish.Concat(_tankFish.Where(f => f.TankId == piece.Id).Select(f => f with { TankId = restored.Id })).ToList().AsReadOnly();
                _counterMeals = _counterMeals.Concat(_counterMeals.Where(m => m.CounterId == piece.Id).Select(m => m with { CounterId = restored.Id })).ToList().AsReadOnly();
                _campGoods = _campGoods.Concat(_campGoods.Where(g => g.StandId == piece.Id).Select(g => g with { StandId = restored.Id })).ToList().AsReadOnly();
            }
            for (var i = 0; i < _gearSources.Count; i++)
                if (_gearSources[i].FurnitureId == piece.Id)
                    _gearSources[i] = _gearSources[i] with { FurnitureId = restored.Id };
            _restockOrigins[restored.Id] = restored;
            _furniture = _furniture with { Pieces = FurnitureList.From(_furniture.Pieces).Add(restored) };
            _stabilityChanges.Add(restored);
            _waterPoints = _waterPoints.Concat(missing.Water.Select(w => w with { FurnitureId = restored.Id })).ToList().AsReadOnly();
            _navigation.Update([], [restored]);
            _villagers.Replan();
            _staff.Replan();
            _missingFurniture.Remove(missing);
        }
        _nextFurnitureCheck = _missingFurniture.Count == 0 ? double.PositiveInfinity : _missingFurniture.Min(m => m.ReadyHour);
    }
}
