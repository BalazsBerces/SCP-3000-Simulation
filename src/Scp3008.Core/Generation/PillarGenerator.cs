using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// The fixed 3 × 3 grid of Pillars and the Sectors they divide the Store into. The layout comes
/// from the Store geometry alone, never from the seed: Sector boundaries run on Chunk boundaries
/// (0 m, 448 m and 896 m in the standard Store) and each Pillar stands at its Sector's centre.
/// </summary>
internal static class PillarGenerator
{
    public const int PerSide = 3;
    public const float Width = 32f;
    // Far above any camera: the Pillars have no visible top and the Store no ceiling.
    public const float Height = 1200f;

    public static IReadOnlyList<Sector> Sectors(StoreSpace space)
    {
        var size = SectorSize(space);
        var sectors = new List<Sector>();
        for (var row = 0; row < PerSide; row++)
            for (var column = 0; column < PerSide; column++)
                sectors.Add(new Sector(sectors.Count, Coordinate(column, row), new Vector2(column, row) * size, size));
        return sectors.AsReadOnly();
    }

    public static IReadOnlyList<Pillar> Pillars(IReadOnlyList<Sector> sectors) =>
        sectors.Select(s => new Pillar(s.Id, s.Name, s.Centre, Width, Height)).ToList().AsReadOnly();

    /// <summary>Index of the Sector containing a position, which may be unwrapped.</summary>
    public static int SectorIndexAt(StoreSpace space, Vector2 position)
    {
        var wrapped = space.Wrap(position);
        var size = SectorSize(space);
        var column = Math.Min((int)(wrapped.X / size), PerSide - 1);
        var row = Math.Min((int)(wrapped.Y / size), PerSide - 1);
        return row * PerSide + column;
    }

    private static float SectorSize(StoreSpace space)
    {
        if (space.ChunksPerSide % PerSide != 0)
            throw new ArgumentException($"Sectors need a Store a multiple of {PerSide} Chunks across.", nameof(space));
        return space.Size / PerSide;
    }

    private static string Coordinate(int column, int row) => $"{(char)('A' + column)}-{row + 1}";
}
