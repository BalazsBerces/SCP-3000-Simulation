namespace Scp3008.Core;

/// <summary>Food taken out of Limbo is spoiling food: a Villager takes it from the Store's shelves into the Village's stores.</summary>
public static class Spoilage
{
    /// <summary>Units lost per spoiling unit per Store day: fresh food, and tinned and dry food.</summary>
    public const float FastRate = 0.5f, SlowRate = 0.05f;
    /// <summary>Food in the open pile spoils this many times faster than in a Storehouse.</summary>
    public const float PileFactor = 3;

    public static float RateOf(GoodsKind kind) => GoodsCatalog.SpoilsFast(kind) ? FastRate : SlowRate;
}

/// <summary>Food that spoiled and was thrown out of a Village's stores.</summary>
public sealed record FoodSpoiled(StoreClock At, int VillageId, GoodsKind Kind, int Amount) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>
    /// Puts taken food back on a piece of Stock Furniture, as if a Villager had returned it. It stays in the stock but keeps spoiling:
    /// putting it back does not restore Limbo.
    /// </summary>
    /// <exception cref="ArgumentException">The piece is not Stock Furniture, or the kind is not food.</exception>
    public void PutBack(int furnitureId, GoodsKind kind, int amount) => _simulation.PutBack(furnitureId, kind, amount);
}

public sealed partial class Simulation
{
    // Units of put-back food on each shelf, and the fraction of a unit lost so far: still spoiling after a return to the shelf.
    private readonly Dictionary<(int FurnitureId, GoodsKind Kind), (int Units, float Progress)> _spoilingShelf = [];
    private const long SpoilageTicks = 50;

    internal void PutBack(int furnitureId, GoodsKind kind, int amount) => Publish(() =>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (GoodsCatalog.ClassOf(kind) != GoodsClass.Food) throw new ArgumentException("Only food spoils.", nameof(kind));
        if (!_furniture.Pieces.Any(p => p.Id == furnitureId && _snapshot.IsStockFurniture(p)))
            throw new ArgumentException($"Piece {furnitureId} is not Stock Furniture.", nameof(furnitureId));
        var stock = _stock.ToList();
        var index = stock.FindIndex(g => g.FurnitureId == furnitureId && g.Kind == kind && !g.LeftBehind);
        if (index < 0) stock.Add(new StockedGoods(furnitureId, kind, amount));
        else stock[index] = stock[index] with { Amount = stock[index].Amount + amount };
        _stock = stock.AsReadOnly();
        var spoiling = _spoilingShelf.GetValueOrDefault((furnitureId, kind));
        _spoilingShelf[(furnitureId, kind)] = (spoiling.Units + amount, spoiling.Progress);
        _stockChanged = true;
        return true;
    });

    private void AdvanceSpoilage(StoreClock clock)
    {
        if (_tick % SpoilageTicks != 1) return;
        var days = (float)SpoilageTicks / StoreClock.TicksPerStoreDay;
        foreach (var village in _villages.ToArray())
        {
            var progress = village.SpoilProgress.ToDictionary();
            foreach (var (kind, count) in village.Stores.Where(g => g.Value > 0 && GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).ToArray())
            {
                var pile = village.PileGoods().GetValueOrDefault(kind);
                var total = progress.GetValueOrDefault(kind) + Spoilage.RateOf(kind) * (count - pile + pile * Spoilage.PileFactor) * days;
                var lost = Math.Min(count, (int)total);
                total -= (int)total;
                if (total > 0) progress[kind] = total; else progress.Remove(kind);
                if (lost <= 0) continue;
                ChangeStores(village.Id, kind, -lost);
                Emit(new FoodSpoiled(clock, village.Id, kind, lost));
            }
            var current = _villages[village.Id];
            foreach (var kind in progress.Keys.Where(k => !current.Stores.ContainsKey(k)).ToArray()) progress.Remove(kind);
            _villages[village.Id] = current with { SpoilProgress = new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, float>(progress) };
        }
        foreach (var key in _spoilingShelf.Keys.ToArray())
        {
            var index = _stock.ToList().FindIndex(g => g.FurnitureId == key.FurnitureId && g.Kind == key.Kind && !g.LeftBehind);
            if (index < 0) { _spoilingShelf.Remove(key); continue; }
            var (units, fraction) = _spoilingShelf[key];
            units = Math.Min(units, _stock[index].Amount);
            fraction += Spoilage.RateOf(key.Kind) * units * days;
            var lost = Math.Min(units, (int)fraction);
            fraction -= (int)fraction;
            if (lost > 0)
            {
                var stock = _stock.ToList();
                if (stock[index].Amount == lost) stock.RemoveAt(index); else stock[index] = stock[index] with { Amount = stock[index].Amount - lost };
                _stock = stock.AsReadOnly();
                _stockChanged = true;
            }
            if (units - lost <= 0) _spoilingShelf.Remove(key); else _spoilingShelf[key] = (units - lost, fraction);
        }
    }
}
