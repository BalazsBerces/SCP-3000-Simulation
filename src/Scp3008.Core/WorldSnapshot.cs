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
public sealed record WorldSnapshot(
    int Seed,
    StoreClock Clock,
    StoreSpace Space,
    SimulationSpeed Speed,
    DepartmentMap Departments,
    IReadOnlyList<FurniturePiece> Furniture);
