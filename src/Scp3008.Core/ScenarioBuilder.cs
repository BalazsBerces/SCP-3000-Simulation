using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// Sets up situations in a running world, for tests and the dev debug panel; reached through
/// <see cref="Simulation.Scenario"/>. Each change takes effect at once, is safe while the
/// simulation thread runs, and shows in the next <see cref="Simulation.Snapshot"/>.
/// </summary>
public sealed class ScenarioBuilder
{
    private readonly Simulation _simulation;

    internal ScenarioBuilder(Simulation simulation) => _simulation = simulation;

    /// <summary>
    /// Places a fully formed Fog bank that drifts with the wind, lingers for
    /// <paramref name="lingerFor"/> (6 Store hours if not given), then thins out and dissipates
    /// like any other. Raises <see cref="FogBankFormed"/>.
    /// </summary>
    /// <param name="position">Centre of the bank; may be unwrapped.</param>
    /// <param name="radius">Distance from the centre to where the fog ends, in metres.</param>
    /// <param name="density">Fog density at the centre, above 0 and up to 1.</param>
    public FogBank PlaceFogBank(Vector2 position, float radius, float density = 1f, TimeSpan? lingerFor = null) =>
        _simulation.Change((weather, now, emit) =>
            weather.Place(position, radius, density, lingerFor ?? TimeSpan.FromHours(6), now, emit));

    /// <summary>Removes every Fog bank at once, with no events.</summary>
    public void ClearFogBanks() => _simulation.Change((weather, _, _) => weather.Clear());

    /// <summary>Whether new Fog banks form on their own (they do in a new world).</summary>
    public void SetFogBankFormation(bool enabled) =>
        _simulation.Change((weather, _, _) => weather.SetFormation(enabled));

    /// <summary>
    /// Sets the wind, in metres per Store hour (X east, Y south), and holds it steady instead of
    /// letting it change on its own.
    /// </summary>
    public void HoldWind(Vector2 metresPerStoreHour) =>
        _simulation.Change((weather, _, _) => weather.HoldWind(metresPerStoreHour));

    /// <summary>
    /// Takes every piece of Furniture that matches out of the Store, as if it had been carried off; returns how many.
    /// What the ground is like (<see cref="WorldSnapshot.GroundAt"/>) stays. Piece ids stay unique, so after this they
    /// no longer match the pieces' indices in <see cref="WorldSnapshot.Furniture"/>.
    /// </summary>
    public int RemoveFurniture(Func<FurniturePiece, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return _simulation.RemoveFurniture(match);
    }
}
