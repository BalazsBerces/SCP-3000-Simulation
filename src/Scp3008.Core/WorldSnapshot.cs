using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// An immutable, read-only view of the world at one moment. Safe to read from any thread.
/// </summary>
/// <param name="Seed">The seed the Store was generated from.</param>
/// <param name="PillarLayout">The world's Pillar layout, which fixes its Pillars and Sectors.</param>
/// <param name="Clock">The Store clock.</param>
/// <param name="Space">The geometry of the wrapping Store floor and its chunks.</param>
/// <param name="Speed">The speed the simulation clock is running at.</param>
/// <param name="Departments">Which Department covers each part of the Store.</param>
/// <param name="Furniture">Every piece of Furniture in the Store, each an individual object.</param>
/// <param name="FogBanks">The Fog banks drifting through the Store.</param>
/// <param name="Wind">The wind the Fog banks drift with, in metres per Store hour (X east, Y south).</param>
/// <param name="Pillars">The identical concrete Pillars on the layout's grid (9 or 4), marked by grid coordinate; never light or power sources.</param>
/// <param name="Sectors">The equal Sectors the Pillars divide the Store into, in the same order as the Pillars.</param>
public sealed record WorldSnapshot(
    int Seed,
    PillarLayout PillarLayout,
    StoreClock Clock,
    StoreSpace Space,
    SimulationSpeed Speed,
    DepartmentMap Departments,
    IReadOnlyList<FurniturePiece> Furniture,
    IReadOnlyList<FogBank> FogBanks,
    Vector2 Wind,
    IReadOnlyList<Pillar> Pillars,
    IReadOnlyList<Sector> Sectors)
{
    /// <summary>Whether the Store is open or in Closing time.</summary>
    public DayPhase Phase => Clock.Phase;

    /// <summary>How bright the Store lights are, from 0 (out) to 1 (fully on).</summary>
    public double StoreLightLevel => Clock.StoreLightLevel;

    /// <summary>
    /// The fog density at a Store position, from 0 (only the thin haze) to 1 (deep inside a thick
    /// Fog bank), using wrapped distances to the Fog banks.
    /// </summary>
    public float FogDensityAt(Vector2 position) => FogBank.CombinedDensityAt(Space, FogBanks, position);

    /// <summary>
    /// The Sector a Store position is in. Positions may be unwrapped; a position on a Sector
    /// boundary belongs to the Sector east or south of it.
    /// </summary>
    public Sector SectorAt(Vector2 position) => Sectors[Generation.PillarGenerator.SectorIndexAt(Space, PillarLayout, position)];

    /// <summary>
    /// The Pillar nearest to a Store position by the shortest wrapped distance. Positions may be
    /// unwrapped. In 2 × 2 the Pillar to the east and the one to the west are the same Pillar.
    /// </summary>
    public Pillar NearestPillar(Vector2 position)
    {
        var nearest = Pillars[0];
        var nearestDistance = float.MaxValue;
        foreach (var pillar in Pillars)
        {
            var distance = Space.Delta(position, pillar.Position).LengthSquared();
            if (distance < nearestDistance)
                (nearest, nearestDistance) = (pillar, distance);
        }
        return nearest;
    }

    /// <summary>
    /// The position itself, or, if it lies inside a Pillar's footprint, the point straight out
    /// through the nearest face onto the middle of the walkway ring. Positions may be unwrapped and
    /// keep their image of the Store.
    /// </summary>
    public Vector2 OutsidePillars(Vector2 position)
    {
        var pillar = NearestPillar(position);
        var delta = Space.Delta(pillar.Position, position);
        var half = pillar.Width / 2f;
        if (MathF.Abs(delta.X) >= half || MathF.Abs(delta.Y) >= half)
            return position;
        var outside = half + Pillar.WalkwayWidth / 2f;
        var moved = MathF.Abs(delta.X) > MathF.Abs(delta.Y)
            ? delta with { X = delta.X < 0f ? -outside : outside }
            : delta with { Y = delta.Y < 0f ? -outside : outside };
        return position + moved - delta;
    }
}
