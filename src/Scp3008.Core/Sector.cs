using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// One of the nine equal squares the 3 × 3 grid of Pillars divides the Store into, centred on its
/// Pillar and named after the Pillar's grid coordinate (e.g. <c>B-2</c>). It covers
/// [Origin, Origin + Size) on each axis, so its boundaries coincide with Chunk boundaries and the
/// Seams, and every wrapped position lies in exactly one Sector.
/// </summary>
public sealed record Sector(int Id, string Name, Vector2 Origin, float Size)
{
    /// <summary>The middle of the Sector, where its Pillar stands.</summary>
    public Vector2 Centre => Origin + new Vector2(Size / 2f);
}
