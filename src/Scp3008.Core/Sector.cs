using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// One of the equal squares the Pillar layout divides the Store into (nine 448 m squares on
/// 3 × 3, four 672 m squares on 2 × 2, nine 640 m squares on the large layouts), named after its
/// grid coordinate (e.g. <c>B-2</c>), like the Pillar at its centre; on the four-Pillar large
/// layouts some Sectors are empty. It covers [Origin, Origin + Size) on each axis; the boundaries
/// include the Seams, and every wrapped position lies in exactly one Sector.
/// </summary>
public sealed record Sector(int Id, string Name, Vector2 Origin, float Size)
{
    /// <summary>The middle of the Sector, where its Pillar stands if it has one.</summary>
    public Vector2 Centre => Origin + new Vector2(Size / 2f);
}
