using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// One of the equal squares the grid of Pillars divides the Store into (nine 448 m squares on
/// 3 × 3, four 672 m squares on 2 × 2), centred on its Pillar and named after the Pillar's grid
/// coordinate (e.g. <c>B-2</c>). It covers [Origin, Origin + Size) on each axis; the boundaries
/// include the Seams, and every wrapped position lies in exactly one Sector.
/// </summary>
public sealed record Sector(int Id, string Name, Vector2 Origin, float Size)
{
    /// <summary>The middle of the Sector, where its Pillar stands.</summary>
    public Vector2 Centre => Origin + new Vector2(Size / 2f);
}
