using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// An immutable, read-only view of the world at one moment. Safe to read from any thread.
/// </summary>
/// <param name="Seed">The seed the Store was generated from.</param>
/// <param name="Clock">The Store clock.</param>
/// <param name="Space">The geometry of the wrapping Store floor and its chunks.</param>
/// <param name="Speed">The speed the simulation clock is running at.</param>
/// <param name="Departments">Which Department covers each part of the Store.</param>
/// <param name="Furniture">Every piece of Furniture in the Store, each an individual object.</param>
/// <param name="FogBanks">The Fog banks drifting through the Store.</param>
/// <param name="Wind">The wind the Fog banks drift with, in metres per Store hour (X east, Y south).</param>
/// <param name="Pillars">Static concrete Landmarks with unique navigation markings; never light or power sources.</param>
public sealed record WorldSnapshot(
    int Seed,
    StoreClock Clock,
    StoreSpace Space,
    SimulationSpeed Speed,
    DepartmentMap Departments,
    IReadOnlyList<FurniturePiece> Furniture,
    IReadOnlyList<FogBank> FogBanks,
    Vector2 Wind,
    IReadOnlyList<Pillar> Pillars)
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
}
