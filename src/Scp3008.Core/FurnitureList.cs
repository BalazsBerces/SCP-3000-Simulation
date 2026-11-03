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
/// instead of every piece in the Store. Lists made one from another share a journal of what changed, so indexes over
/// the Furniture can catch up instead of starting over (<see cref="ChangesSince"/>); the journal holds the changes
/// themselves, not the lists, so old lists are collected as soon as nothing else holds them.
/// </summary>
internal sealed class FurnitureList : IReadOnlyList<FurniturePiece>
{
    private const int BlockSize = 1024;

    private readonly FurniturePiece[][] _blocks;
    private readonly Journal _journal;
    // How many of the journal's changes lead to this list.
    private readonly long _position;
    // Id → first index, shared along lists that keep every id in place; added ids are entered as they come.
    private Dictionary<int, int>? _indexById;

    public static FurnitureList Empty { get; } = new([], 0, new Journal(), 0, null);

    private FurnitureList(FurniturePiece[][] blocks, int count, Journal journal, long position, Dictionary<int, int>? indexById)
    {
        _blocks = blocks;
        Count = count;
        _journal = journal;
        _position = position;
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
        return new(blocks, all.Length, new Journal(), 0, null);
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
        var (journal, position) = Record(new FurnitureChange(index, before, piece));
        return new(blocks, Count, journal, position, before.Id == piece.Id ? _indexById : null);
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
        if (_indexById is { } map)
            lock (map)
                map.TryAdd(piece.Id, Count);
        var (journal, position) = Record(new FurnitureChange(Count, null, piece));
        return new(blocks, Count + 1, journal, position, _indexById);
    }

    // Notes a change made to this list in the journal. A list changed again after a later one was made from it (which the
    // Simulation never does) starts a journal of its own, and indexes behind it start over.
    private (Journal Journal, long Position) Record(FurnitureChange change)
    {
        if (_journal.TryAppend(_position, change))
            return (_journal, _position + 1);
        var fork = new Journal();
        fork.TryAppend(0, change);
        return (fork, 1);
    }

    /// <summary>The index of the first piece with this id, or -1.</summary>
    public int IndexOf(int id)
    {
        var map = _indexById;
        if (map is null)
        {
            // Filled before it is shared: readers on other threads never see half a map.
            map = new Dictionary<int, int>(Count);
            for (var i = 0; i < Count; i++)
                map.TryAdd(this[i].Id, i);
            _indexById = map;
        }
        int index;
        bool found;
        // A later list may be adding to a shared map on the simulation thread.
        lock (map)
            found = map.TryGetValue(id, out index);
        // A shared map may hold ids added to a later list; then look the slow way.
        if (found && index < Count && this[index].Id == id)
            return index;
        for (var i = 0; i < Count; i++)
            if (this[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>
    /// The changes, oldest first, that lead from <paramref name="older"/> to this list; false when this list does not
    /// remember coming from it (another line of lists, or more changes ago than the journal keeps).
    /// </summary>
    public bool ChangesSince(IReadOnlyList<FurniturePiece>? older, List<FurnitureChange> changes)
    {
        changes.Clear();
        return older is FurnitureList list && ReferenceEquals(list._journal, _journal) && list._position <= _position &&
            _journal.CopyTo(list._position, _position, changes);
    }

    public IEnumerator<FurniturePiece> GetEnumerator()
    {
        foreach (var block in _blocks)
            foreach (var piece in block)
                yield return piece;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// The changes along one line of lists, the latest <see cref="Capacity"/> of them kept in a ring: appended on the
    /// simulation thread, read on any (the presentation follows the Furniture from snapshot to snapshot).
    /// </summary>
    private sealed class Journal
    {
        // How many changes back a list remembers; an index further behind starts over.
        private const int Capacity = 1 << 16;
        private FurnitureChange[] _ring = [];
        private long _count;

        /// <summary>Appends a change made to the list at <paramref name="position"/>, if that is the latest list.</summary>
        public bool TryAppend(long position, FurnitureChange change)
        {
            lock (this)
            {
                if (position != _count)
                    return false;
                if (_count == _ring.Length && _ring.Length < Capacity)
                {
                    // Grows while young, so the many short-lived worlds of tests stay small.
                    var grown = new FurnitureChange[Math.Max(64, _ring.Length * 2)];
                    Array.Copy(_ring, grown, _ring.Length);
                    _ring = grown;
                }
                _ring[_count % _ring.Length] = change;
                _count++;
                return true;
            }
        }

        /// <summary>Adds the changes from <paramref name="from"/> up to <paramref name="to"/>, if they are still kept.</summary>
        public bool CopyTo(long from, long to, List<FurnitureChange> changes)
        {
            lock (this)
            {
                if (from < _count - _ring.Length)
                    return false;
                for (var i = from; i < to; i++)
                    changes.Add(_ring[i % _ring.Length]);
                return true;
            }
        }
    }
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
