namespace Scp3008.Core;

/// <summary>
/// A small, fully specified pseudo-random generator (SplitMix64) for Store generation, so the
/// same seed gives the same Store on every runtime. <see cref="Random"/> is avoided because its
/// seeded sequence is not guaranteed across .NET versions.
/// </summary>
internal sealed class SeededRandom
{
    private ulong _state;

    internal ulong State { get => _state; set => _state = value; }

    public SeededRandom(ulong seed) => _state = seed;

    /// <summary>An independent stream for one part of the Store, derived from the seed and a few keys.</summary>
    public static SeededRandom For(int seed, int stream, int a = 0, int b = 0)
    {
        var mixer = new SeededRandom((ulong)(uint)seed);
        var state = mixer.NextULong() ^ (ulong)(uint)stream * 0xD1B54A32D192ED03UL;
        state = Mix(state ^ (ulong)(uint)a * 0x9E3779B97F4A7C15UL);
        state = Mix(state ^ (ulong)(uint)b * 0xC2B2AE3D27D4EB4FUL);
        return new SeededRandom(state);
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>A float in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

    /// <summary>A float in [min, max).</summary>
    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>An integer in [0, count).</summary>
    public int Next(int count) => (int)((NextULong() >> 33) % (ulong)count);

    /// <summary>True with the given probability.</summary>
    public bool Chance(float probability) => NextFloat() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
