using System.Collections;
using System.Runtime.InteropServices;

namespace Scp3008.Core;

/// <summary>
/// The Store's Goods as one list that never changes once published, with the Goods on each piece of Furniture found without
/// going through all of them. A change makes a new list; one that only changes an amount shares the old list's index.
/// </summary>
public sealed class StockList : IReadOnlyList<StockedGoods>
{
    public static readonly StockList Empty = new([]);

    private readonly StockedGoods[] _items;
    // Built on first use by whichever thread asks first; every build of one list gives the same index.
    private PieceIndex? _index;

    public StockList(IEnumerable<StockedGoods> items) : this(items.ToArray(), null)
    {
    }

    private StockList(StockedGoods[] items, PieceIndex? index)
    {
        _items = items;
        _index = index;
    }

    public StockedGoods this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<StockedGoods> GetEnumerator() => ((IEnumerable<StockedGoods>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Where in the list the Goods on one piece are, in list order; empty when it holds none.</summary>
    internal ReadOnlySpan<int> On(int furnitureId) => Index().On(furnitureId);

    /// <summary>Where one kind of Goods on a piece is in the list, or -1.</summary>
    internal int IndexOf(int furnitureId, GoodsKind kind, bool leftBehind)
    {
        foreach (var i in On(furnitureId))
            if (_items[i].Kind == kind && _items[i].LeftBehind == leftBehind)
                return i;
        return -1;
    }

    /// <summary>The list with the Goods at <paramref name="index"/> holding <paramref name="amount"/> units; none takes them off.</summary>
    internal StockList WithAmount(int index, int amount)
    {
        if (amount <= 0)
        {
            var fewer = new StockedGoods[_items.Length - 1];
            Array.Copy(_items, fewer, index);
            Array.Copy(_items, index + 1, fewer, index, _items.Length - index - 1);
            return new(fewer, null);
        }
        var items = (StockedGoods[])_items.Clone();
        items[index] = items[index] with { Amount = amount };
        return new(items, _index);
    }

    /// <summary>The list with <paramref name="goods"/> added at the end.</summary>
    internal StockList With(StockedGoods goods)
    {
        var items = new StockedGoods[_items.Length + 1];
        Array.Copy(_items, items, _items.Length);
        items[^1] = goods;
        return new(items, null);
    }

    /// <summary>The list with each change made: new Goods in place of those at an index, or added at the end for index -1.</summary>
    internal StockList With(IReadOnlyList<(int Index, StockedGoods Goods)> changes)
    {
        var added = 0;
        foreach (var (index, _) in changes)
            if (index < 0)
                added++;
        var items = new StockedGoods[_items.Length + added];
        Array.Copy(_items, items, _items.Length);
        var end = _items.Length;
        foreach (var (index, goods) in changes)
            items[index < 0 ? end++ : index] = goods;
        return new(items, added == 0 ? _index : null);
    }

    private PieceIndex Index() => _index ??= new PieceIndex(_items);

    private sealed class PieceIndex
    {
        private readonly Dictionary<int, (int Start, int Count)> _ranges = [];
        private readonly int[] _positions;

        public PieceIndex(StockedGoods[] items)
        {
            // Count each piece's Goods, then give each piece its run of positions in the order the pieces first appear.
            foreach (var goods in items)
            {
                ref var range = ref CollectionsMarshal.GetValueRefOrAddDefault(_ranges, goods.FurnitureId, out var known);
                if (!known) range.Start = -1;
                range.Count++;
            }
            var start = 0;
            _positions = new int[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                ref var range = ref CollectionsMarshal.GetValueRefOrNullRef(_ranges, items[i].FurnitureId);
                if (range.Start < 0)
                {
                    range.Start = start;
                    start += range.Count;
                    range.Count = 0;
                }
                _positions[range.Start + range.Count++] = i;
            }
        }

        public ReadOnlySpan<int> On(int furnitureId) =>
            _ranges.TryGetValue(furnitureId, out var range) ? _positions.AsSpan(range.Start, range.Count) : [];
    }
}
