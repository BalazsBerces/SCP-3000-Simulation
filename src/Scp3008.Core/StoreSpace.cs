using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// The geometry of the wrapping Store floor: a square that wraps in all directions (a torus),
/// divided into square chunks. Positions are in metres, X running east and Y running south.
/// Positions may be given unwrapped; distance and direction always use the shortest wrapped
/// delta, so the Store has no edges, only seams where it meets itself.
/// </summary>
public sealed record StoreSpace
{
    /// <summary>The standard Store: 21 × 21 chunks of 64 m, 1344 × 1344 m (about 1.3 × 1.3 km).</summary>
    public static StoreSpace Standard { get; } = new(chunkSize: 64f, chunksPerSide: 21);

    public StoreSpace(float chunkSize, int chunksPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunksPerSide);
        ChunkSize = chunkSize;
        ChunksPerSide = chunksPerSide;
    }

    /// <summary>Edge length of a chunk, in metres.</summary>
    public float ChunkSize { get; }

    /// <summary>Number of chunks along each edge of the Store.</summary>
    public int ChunksPerSide { get; }

    /// <summary>Edge length of the Store floor, in metres.</summary>
    public float Size => ChunkSize * ChunksPerSide;

    /// <summary>Total number of chunks in the Store.</summary>
    public int ChunkCount => ChunksPerSide * ChunksPerSide;

    /// <summary>The same place expressed on the floor, with both coordinates in [0, <see cref="Size"/>).</summary>
    public Vector2 Wrap(Vector2 position) => new(WrapAxis(position.X), WrapAxis(position.Y));

    /// <summary>The shortest wrapped vector from one position to another; each axis lies in [-Size/2, Size/2).</summary>
    public Vector2 Delta(Vector2 from, Vector2 to) => new(DeltaAxis(from.X, to.X), DeltaAxis(from.Y, to.Y));

    /// <summary>The shortest wrapped distance between two positions.</summary>
    public float Distance(Vector2 a, Vector2 b) => Delta(a, b).Length();

    /// <summary>The unit vector along the shortest wrapped path, or zero when both are the same place.</summary>
    public Vector2 Direction(Vector2 from, Vector2 to)
    {
        var delta = Delta(from, to);
        var length = delta.Length();
        return length == 0f ? Vector2.Zero : delta / length;
    }

    /// <summary>The chunk containing a position.</summary>
    public ChunkCoord ChunkOf(Vector2 position)
    {
        var wrapped = Wrap(position);
        return new ChunkCoord(ChunkIndex(wrapped.X), ChunkIndex(wrapped.Y));
    }

    /// <summary>The north-west corner of a chunk on the floor. Chunk coordinates wrap.</summary>
    public Vector2 ChunkOrigin(ChunkCoord chunk) =>
        new(Mod(chunk.X, ChunksPerSide) * ChunkSize, Mod(chunk.Y, ChunksPerSide) * ChunkSize);

    /// <summary>
    /// Every chunk overlapping the square of half-width <paramref name="reach"/> around
    /// <paramref name="center"/>, each with the offset that places it next to the center.
    /// Around a seam this yields chunks from the far side of the Store; with a reach wider
    /// than the Store the same chunk appears more than once (ghost copies).
    /// </summary>
    public IEnumerable<ChunkImage> ChunkImagesAround(Vector2 center, float reach)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reach);
        var minX = (long)Math.Floor((center.X - reach) / ChunkSize);
        var maxX = (long)Math.Floor((center.X + reach) / ChunkSize);
        var minY = (long)Math.Floor((center.Y - reach) / ChunkSize);
        var maxY = (long)Math.Floor((center.Y + reach) / ChunkSize);
        for (var y = minY; y <= maxY; y++)
        {
            var chunkY = Mod(y, ChunksPerSide);
            var offsetY = (y - chunkY) / ChunksPerSide * Size;
            for (var x = minX; x <= maxX; x++)
            {
                var chunkX = Mod(x, ChunksPerSide);
                var offsetX = (x - chunkX) / ChunksPerSide * Size;
                yield return new ChunkImage(new ChunkCoord(chunkX, chunkY), new Vector2(offsetX, offsetY));
            }
        }
    }

    private float WrapAxis(float value)
    {
        double size = Size;
        var wrapped = (float)(value - size * Math.Floor(value / size));
        // A value a hair below zero wraps to just under Size, which can round up to Size in float.
        return wrapped < Size ? wrapped : MathF.BitDecrement(Size);
    }

    private float DeltaAxis(float from, float to)
    {
        double size = Size;
        var delta = (double)to - from;
        return (float)(delta - size * Math.Floor(delta / size + 0.5));
    }

    private int ChunkIndex(float wrapped) => Math.Min((int)(wrapped / ChunkSize), ChunksPerSide - 1);

    private static int Mod(long value, int modulus) => (int)(((value % modulus) + modulus) % modulus);
}

/// <summary>Grid coordinates of a chunk, from (0, 0) in the north-west corner of the Store.</summary>
public readonly record struct ChunkCoord(int X, int Y);

/// <summary>
/// A chunk placed at one of its wrapped images: add <paramref name="Offset"/> (a whole number of
/// Store lengths per axis) to positions in the chunk to draw them next to a viewpoint.
/// </summary>
public readonly record struct ChunkImage(ChunkCoord Chunk, Vector2 Offset);
