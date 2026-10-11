using System.Globalization;
using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>Places one Pillar per Chunk at an aisle intersection, including on the Seams.</summary>
internal static class PillarGenerator
{
    public static IReadOnlyList<Pillar> Generate(int seed, StoreSpace space, DepartmentMap departments)
    {
        var pillars = new List<Pillar>();
        var intersections = (int)(space.ChunkSize / departments.CellSize);
        for (var y = 0; y < space.ChunksPerSide; y++)
            for (var x = 0; x < space.ChunksPerSide; x++)
            {
                var random = SeededRandom.For(seed, stream: 4, x, y);
                // Furniture remains inside a 1.5 m cell margin. A 2.4 m Pillar fits the
                // 3 m aisle, leaving all existing displays untouched, even at a Seam.
                var position = space.ChunkOrigin(new ChunkCoord(x, y)) +
                    new Vector2(random.Next(intersections), random.Next(intersections)) * departments.CellSize;
                var id = pillars.Count;
                pillars.Add(new Pillar(id, position, 2.4f, random.Range(36f, 48f),
                    new PillarMarking((id + 1).ToString("D3", CultureInfo.InvariantCulture),
                        (PillarBand)random.Next(5), (PillarDamage)random.Next(3))));
            }
        return pillars.AsReadOnly();
    }
}
