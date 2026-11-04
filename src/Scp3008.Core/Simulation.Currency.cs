namespace Scp3008.Core;

/// <summary>
/// The Goods kind a Village takes as currency changed: <see cref="To"/> is now accepted in trade beyond the Village's own need,
/// because the Villages it knows want it; null when it went back to pure barter.
/// </summary>
public sealed record CurrencyChanged(StoreClock At, int VillageId, GoodsKind? From, GoodsKind? To) : DomainEvent(At);

public sealed partial class Simulation
{
    private const int CurrencyEvery = 500;
    // Share of the known Villages that must want a kind for it to become currency, and to stay currency.
    private const double CurrencyWantedShare = 0.75, CurrencyKeptShare = 0.5;

    // The Villages whose wants a Village knows: those on its Known map that still stand and are peopled.
    private Village[] KnownVillages(Village village)
    {
        var known = village.KnownMap.Villages.Where(v => !v.Abandoned).Select(v => v.VillageId).ToHashSet();
        return _villages.Where(v => v.Id != village.Id && known.Contains(v.Id) && !v.Fallen && v.VillagerIds.Count > 0).ToArray();
    }

    // What a unit of the Goods is worth to the Village in trade: its own need, or for its currency what the Villages it knows would give.
    private double TradeValue(Village village, GoodsKind kind) => village.Currency != kind ? Price(village, kind) :
        Math.Max(Price(village, kind), KnownVillages(village).Select(v => Price(v, kind)).DefaultIfEmpty(1).Average());

    // A kind becomes currency of its own accord: it keeps well, someone in the region holds it, and most of the Villages known lack it.
    private void AdvanceCurrency(StoreClock clock)
    {
        if (_tick % CurrencyEvery != 9) return;
        foreach (var village in _villages.ToArray())
        {
            if (village.Fallen || village.VillagerIds.Count == 0) continue;
            var others = KnownVillages(village);
            GoodsKind? currency = null;
            if (others.Length >= 2)
            {
                var region = others.Append(village).ToArray();
                currency = Enum.GetValues<GoodsKind>()
                    .Where(k => !GoodsCatalog.SpoilsFast(k) && region.Any(v => v.Stores.GetValueOrDefault(k) > 0))
                    .Select(k => (Kind: k, Wanted: others.Count(o => o.Stores.GetValueOrDefault(k) < Need(o, k)) / (double)others.Length,
                        Held: region.Sum(v => v.Stores.GetValueOrDefault(k))))
                    .Where(c => c.Wanted >= (c.Kind == village.Currency ? CurrencyKeptShare : CurrencyWantedShare))
                    // The current currency holds while it still qualifies; otherwise the most wanted, then most circulating, kind wins.
                    .OrderByDescending(c => c.Kind == village.Currency).ThenByDescending(c => c.Wanted).ThenByDescending(c => c.Held)
                    .Select(c => (GoodsKind?)c.Kind).FirstOrDefault();
            }
            if (currency == village.Currency) continue;
            _villages[village.Id] = village with { Currency = currency };
            Emit(new CurrencyChanged(clock, village.Id, village.Currency, currency));
        }
    }
}
