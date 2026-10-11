namespace Scp3008.Core;

/// <summary>
/// An immutable, read-only view of the world at one moment. Safe to read from any thread.
/// </summary>
/// <param name="Seed">The seed the Store was generated from.</param>
/// <param name="Clock">The Store clock.</param>
/// <param name="StoreSize">Edge length of the square Store floor, in metres.</param>
/// <param name="Speed">The speed the simulation clock is running at.</param>
public sealed record WorldSnapshot(int Seed, StoreClock Clock, float StoreSize, SimulationSpeed Speed);
