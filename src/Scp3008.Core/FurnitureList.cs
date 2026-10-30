using System.Collections;
using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// One piece of Furniture changed between two Furniture lists of the same world: the piece at <paramref name="Index"/> was
/// <paramref name="Before"/> and is now <paramref name="After"/>; <paramref name="Before"/> is null when it was added at the end.
/// </summary>
public readonly record struct FurnitureChange(int Index, FurniturePiece? Before, FurniturePiece After);

/// <summary>
/// The Store's Furniture as an immutable list kept in blocks, so changing or adding one piece copies a single block
/// instead of every piece in the Store. Each list remembers the one it was made from and what changed, so indexes
/// over the Furniture can catch up instead of starting over (<see cref="ChangesSince"/>).
/// </summary>
internal sealed class FurnitureList : IReadOnlyList<FurniturePiece>
{
    private const int BlockSize = 1024;
    // How many changes back a list remembers; an index further behind starts over.
    private const int MaxHistory = 64;

    private readonly FurniturePiece[][] _blocks;
    private readonly FurnitureList? _parent;
    private readonly FurnitureChange _change;
    private readonly int _history;
    // Id → first index, shared along lists that keep every id in place; added ids are entered as they come.
    private Dictionary<int, int>? _indexById;

    public static FurnitureList Empty { get; } = new([], 0, null, default, 0, null);

    private FurnitureList(FurniturePiece[][] blocks, int count, FurnitureList? parent, FurnitureChange change, int history, Dictionary<int, int>? indexById)
    {
        _blocks = blocks;
        Count = count;
        _parent = history > MaxHistory ? null : parent;
        _change = change;
        _history = history > MaxHistory ? 0 : history;
        _indexById = indexById;
    }

    public static FurnitureList From(IEnumerable<FurniturePiece> pieces)
    {
        if (pieces is FurnitureList list)
            return list;
        var all = pieces as FurniturePiece[] ?? pieces.ToArray();
        var blocks = new FurniturePiece[(all.Length + BlockSize - 1) / BlockSize][];
        for (var b = 0; b < blocks.Length; b++)
            blocks[b] = all.AsSpan(b * BlockSize, Math.Min(BlockSize, all.Length - b * BlockSize)).ToArray();
        return new(blocks, all.Length, null, default, 0, null);
    }

    public int Count { get; }

    public FurniturePiece this[int index] => (uint)index < (uint)Count
        ? _blocks[index / BlockSize][index % BlockSize]
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>A list with the piece at <paramref name="index"/> replaced.</summary>
    public FurnitureList SetItem(int index, FurniturePiece piece)
    {
        var before = this[index];
        var blocks = (FurniturePiece[][])_blocks.Clone();
        var block = blocks[index / BlockSize] = (FurniturePiece[])blocks[index / BlockSize].Clone();
        block[index % BlockSize] = piece;
        return new(blocks, Count, this, new FurnitureChange(index, before, piece), _history + 1, before.Id == piece.Id ? _indexById : null);
    }

    /// <summary>A list with <paramref name="piece"/> added at the end.</summary>
    public FurnitureList Add(FurniturePiece piece)
    {
        FurniturePiece[][] blocks;
        if (Count % BlockSize == 0)
            blocks = [.. _blocks, [piece]];
        else
        {
            blocks = (FurniturePiece[][])_blocks.Clone();
            blocks[^1] = [.. blocks[^1], piece];
        }
        _indexById?.TryAdd(piece.Id, Count);
        return new(blocks, Count + 1, this, new FurnitureChange(Count, null, piece), _history + 1, _indexById);
    }

    /// <summary>The index of the first piece with this id, or -1.</summary>
    public int IndexOf(int id)
    {
        if (_indexById is null)
        {
            _indexById = new Dictionary<int, int>(Count);
            for (var i = 0; i < Count; i++)
                _indexById.TryAdd(this[i].Id, i);
        }
        // A shared map may hold ids added to a later list; then look the slow way.
        if (_indexById.TryGetValue(id, out var index) && index < Count && this[index].Id == id)
            return index;
        for (var i = 0; i < Count; i++)
            if (this[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>
    /// The changes, oldest first, that lead from <paramref name="older"/> to this list; false when this list does not
    /// remember coming from it.
    /// </summary>
    public bool ChangesSince(IReadOnlyList<FurniturePiece>? older, List<FurnitureChange> changes)
    {
        changes.Clear();
        for (var list = this; list is not null; list = list._parent)
        {
            if (ReferenceEquals(list, older))
            {
                changes.Reverse();
                return true;
            }
            if (list._parent is not null)
                changes.Add(list._change);
        }
        changes.Clear();
        return false;
    }

    public IEnumerator<FurniturePiece> GetEnumerator()
    {
        foreach (var block in _blocks)
            foreach (var piece in block)
                yield return piece;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Furniture by chunk, kept up to date with a <see cref="FurnitureList"/> as it changes.</summary>
internal sealed class FurnitureIndex(StoreSpace space)
{
    private IReadOnlyList<FurniturePiece>? _pieces;
    // Indices into the pieces, ascending, so pieces come out in list order.
    private readonly Dictionary<ChunkCoord, List<int>> _byChunk = [];
    private readonly List<FurnitureChange> _changes = [];

    /// <summary>The farthest any piece's box reaches from its base centre, corner to corner.</summary>
    public float LargestPiece { get; private set; }

    public void Update(IReadOnlyList<FurniturePiece> pieces)
    {
        if (ReferenceEquals(pieces, _pieces))
            return;
        if (_pieces is not null && pieces is FurnitureList list && list.ChangesSince(_pieces, _changes) && _changes.All(SameSize))
        {
            foreach (var change in _changes)
            {
                var to = space.ChunkOf(change.After.Position);
                if (change.Before is { } before)
                {
                    var from = space.ChunkOf(before.Position);
                    if (from == to)
                        continue;
                    var inChunk = _byChunk[from];
                    inChunk.RemoveAt(inChunk.BinarySearch(change.Index));
                }
                else
                    LargestPiece = Math.Max(LargestPiece, Size(change.After));
                if (!_byChunk.TryGetValue(to, out var target))
                    _byChunk[to] = target = [];
                var at = target.BinarySearch(change.Index);
                target.Insert(at < 0 ? ~at : at, change.Index);
            }
            _pieces = pieces;
            return;
        }
        _pieces = pieces;
        _byChunk.Clear();
        for (var i = 0; i < pieces.Count; i++)
        {
            var chunk = space.ChunkOf(pieces[i].Position);
            if (!_byChunk.TryGetValue(chunk, out var inChunk))
                _byChunk[chunk] = inChunk = [];
            inChunk.Add(i);
        }
        LargestPiece = pieces.Select(Size).DefaultIfEmpty().Max();
    }

    /// <summary>Every piece in the chunks within <paramref name="range"/> of <paramref name="position"/>, chunk by chunk in list order.</summary>
    public IEnumerable<FurniturePiece> Near(Vector2 position, float range)
    {
        var pieces = _pieces!;
        foreach (var index in NearIndices(position, range))
            yield return pieces[index];
    }

    /// <summary>The list indices of <see cref="Near"/>.</summary>
    public IEnumerable<int> NearIndices(Vector2 position, float range)
    {
        foreach (var chunk in space.ChunkImagesAround(position, range).Select(c => c.Chunk).Distinct())
            if (_byChunk.TryGetValue(chunk, out var inChunk))
                foreach (var index in inChunk)
                    yield return index;
    }

    private static float Size(FurniturePiece piece)
    {
        var spec = FurnitureCatalog.Spec(piece);
        return new Vector3(spec.Width, spec.Depth, spec.Height).Length();
    }

    private static bool SameSize(FurnitureChange change) => change.Before is not { } before || Size(before) == Size(change.After);
}
