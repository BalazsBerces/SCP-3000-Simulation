using Scp3008.Core.Generation;

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
    private double _goodsRestockCredit;

    private void InitializeRestock()
    {
        foreach (var piece in _furniture.Pieces)
            _restockOrigins[piece.Id] = piece;
        _nextFurnitureId = _furniture.Pieces.Count == 0 ? 0 : _furniture.Pieces.Max(p => p.Id) + 1;
        // Give initially bare Stock Furniture a target too, using the same catalogue and placement rules as generation.
        var fullStock = StockGenerator.Generate(_config.Seed, StockConfig with { StockedShelfShare = 1, SectorGuarantee = false },
            _furniture.Pieces, _furniture.Displays, _departments, _pointsOfInterest, _tankFish, _counterMeals, _campGoods,
            _landmarks, _space, _config.PillarLayout);
        var initial = _stock.Where(g => !g.LeftBehind).ToLookup(g => g.FurnitureId);
        foreach (var group in fullStock.Where(g => !g.LeftBehind).GroupBy(g => g.FurnitureId))
            foreach (var goods in initial.Contains(group.Key) ? initial[group.Key] : group)
                _stockTargets[(goods.FurnitureId, goods.Kind)] = goods.Amount;
        foreach (var goods in _stock.Where(g => !g.LeftBehind))
            _stockTargets[(goods.FurnitureId, goods.Kind)] = goods.Amount;
    }

    private void AdvanceRestock()
    {
        _restockHours += StoreClock.TickDuration.TotalHours;
        RestoreFurniture();
        _goodsRestockCredit += StoreClock.TickDuration.TotalHours * (_config.Restock ?? new RestockConfig()).GoodsPerStoreHour;
        if (_goodsRestockCredit + 1e-9 < 1)
            return;
        var units = Math.Floor(_goodsRestockCredit + 1e-9);
        _goodsRestockCredit = Math.Max(0, _goodsRestockCredit - units);
        var present = _furniture.Pieces.Select(p => p.Id).ToHashSet();
        var stock = _stock.ToDictionary(g => (g.FurnitureId, g.Kind, g.LeftBehind));
        var changed = false;
        foreach (var (key, capacity) in _stockTargets)
        {
            if (!present.Contains(key.FurnitureId) || !_restockOrigins.ContainsKey(key.FurnitureId))
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
            var occupied = _furniture.Pieces.Any(p => !OriginalNeighbour(p) && RestockSight.Overlaps(_space, piece, p)) ||
                villagers.Any(v => _space.Distance(v.Position, piece.Position) < Math.Max(FurnitureCatalog.Spec(piece).Width, FurnitureCatalog.Spec(piece).Depth) / 2 + 0.4f);
            if (occupied)
                continue;
            if (villagers.Any(v => RestockSight.CanSee(_space, v, piece, _furniture.Pieces, _pillars, fog)))
                continue;
            var restored = _furniture.Pieces.Any(p => p.Id == piece.Id) ? piece with { Id = _nextFurnitureId++ } : piece;
            if (restored.Id != piece.Id)
            {
                foreach (var target in _stockTargets.Where(t => t.Key.FurnitureId == piece.Id).ToArray())
                    _stockTargets[(restored.Id, target.Key.Kind)] = target.Value;
                _tankFish = _tankFish.Concat(_tankFish.Where(f => f.TankId == piece.Id).Select(f => f with { TankId = restored.Id })).ToList().AsReadOnly();
                _counterMeals = _counterMeals.Concat(_counterMeals.Where(m => m.CounterId == piece.Id).Select(m => m with { CounterId = restored.Id })).ToList().AsReadOnly();
                _campGoods = _campGoods.Concat(_campGoods.Where(g => g.StandId == piece.Id).Select(g => g with { StandId = restored.Id })).ToList().AsReadOnly();
            }
            _restockOrigins[restored.Id] = restored;
            _furniture = _furniture with { Pieces = _furniture.Pieces.Append(restored).ToList().AsReadOnly() };
            _waterPoints = _waterPoints.Concat(missing.Water.Select(w => w with { FurnitureId = restored.Id })).ToList().AsReadOnly();
            _navigation.Update([], [restored]);
            _villagers.Replan();
            _staff.Replan();
            _missingFurniture.Remove(missing);
        }
        _nextFurnitureCheck = _missingFurniture.Count == 0 ? double.PositiveInfinity : _missingFurniture.Min(m => m.ReadyHour);
    }
}
