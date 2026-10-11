using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// A static concrete Landmark, separate from Furniture: one of nine identical Pillars on a regular
/// 3 × 3 grid, each at the centre of its <see cref="Sector"/>. It is marked with its grid
/// <paramref name="Coordinate"/>: column letter A–C along X, then row number 1–3 along Y (e.g.
/// <c>B-2</c>). Its square footprint is centred on Position; Width and Height are in metres, and
/// the Height reaches far beyond any camera, so the top is never seen. A one-aisle walkway ring
/// around the footprint is kept free of Furniture. Pillars never give light or power.
/// </summary>
public sealed record Pillar(int Id, string Coordinate, Vector2 Position, float Width, float Height)
{
    /// <summary>Width of the Furniture-free walkway ring around every Pillar: one aisle.</summary>
    public const float WalkwayWidth = 3f;

    /// <summary>Half the side of the square kept free of Furniture: the footprint plus its walkway ring.</summary>
    public float ClearHalfWidth => Width / 2f + WalkwayWidth;
}
