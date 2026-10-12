using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// The fixed grid of Pillars of a <see cref="PillarLayout"/> and the Sectors they divide the Store
/// into. The grid comes from the layout and the Store geometry alone, never from the seed: Sector
/// boundaries start at the Seam and repeat every Store length / Pillars per side (448 m in 3 × 3,
/// on Chunk boundaries; 672 m in 2 × 2, on the 16 m aisle grid but mid-Chunk; 640 m on the large
/// Store, on Chunk boundaries), and each Pillar stands at the centre of a Sector; on the four-Pillar
/// large layouts the other Sectors are empty.
/// </summary>
internal static class PillarGenerator
{
    public const float Width = 32f;
    // Far above any camera: the Pillars have no visible top and the Store no ceiling.
    public const float Height = 1200f;

    public static IReadOnlyList<Sector> Sectors(StoreSpace space, PillarLayout layout)
    {
        var perSide = layout.SectorsPerSide();
        var size = space.Size / perSide;
        var sectors = new List<Sector>();
        for (var row = 0; row < perSide; row++)
            for (var column = 0; column < perSide; column++)
                sectors.Add(new Sector(sectors.Count, Coordinate(column, row), new Vector2(column, row) * size, size));
        return sectors.AsReadOnly();
    }

    /// <summary>The Pillars at the centres of the layout's Sectors that have one, numbered from 0 in Sector order.</summary>
    public static IReadOnlyList<Pillar> Pillars(IReadOnlyList<Sector> sectors, PillarLayout layout)
    {
        var perSide = layout.SectorsPerSide();
        return sectors
            .Where(s => layout.HasPillar(s.Id % perSide, s.Id / perSide))
            .Select((s, id) => new Pillar(id, s.Name, s.Centre, Width, Height))
            .ToList().AsReadOnly();
    }

    /// <summary>Index of the Sector containing a position, which may be unwrapped.</summary>
    public static int SectorIndexAt(StoreSpace space, PillarLayout layout, Vector2 position)
    {
        var perSide = layout.SectorsPerSide();
        var wrapped = space.Wrap(position);
        var size = space.Size / perSide;
        var column = Math.Min((int)(wrapped.X / size), perSide - 1);
        var row = Math.Min((int)(wrapped.Y / size), perSide - 1);
        return row * perSide + column;
    }

    private static string Coordinate(int column, int row) => $"{(char)('A' + column)}-{row + 1}";
}
