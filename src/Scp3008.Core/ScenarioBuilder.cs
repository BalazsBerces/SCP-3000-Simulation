using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// Sets up situations in a running world, for tests and the dev debug panel; reached through
/// <see cref="Simulation.Scenario"/>. Each change takes effect at once, is safe while the
/// simulation thread runs, and shows in the next <see cref="Simulation.Snapshot"/>.
/// </summary>
public sealed partial class ScenarioBuilder
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

    /// <summary>
    /// Sets how many units of one Goods kind lie on a piece of Stock Furniture, as if they had been taken or restocked; 0 takes the
    /// kind off it, leaving that shelf bare. The Sector stock follows. Display Furniture cannot be stocked.
    /// </summary>
    /// <exception cref="ArgumentException">The piece is not Stock Furniture.</exception>
    public void SetStock(int furnitureId, GoodsKind kind, int amount) => _simulation.SetStock([(furnitureId, kind)], amount);

    /// <summary>
    /// <see cref="SetStock(int, GoodsKind, int)"/> for many Goods at once (a whole aisle or Store), with one Sector stock update.
    /// </summary>
    /// <exception cref="ArgumentException">A piece is not Stock Furniture; nothing is set.</exception>
    public void SetStock(IEnumerable<(int FurnitureId, GoodsKind Kind)> goods, int amount) => _simulation.SetStock(goods, amount);

    /// <summary>Places a Villager with optional initial Needs, each from 0 to 1, on the nearest free floor.</summary>
    public Villager PlaceVillager(Vector2 position, Needs? needs = null)
    {
        (needs ?? new Needs()).Validate();
        return _simulation.PlaceVillager(position, needs);
    }

    /// <summary>
    /// Sends a Villager walking the shortest way, around Furniture and Pillars and across the seams, to a Store position
    /// (beside it, if Furniture stands there). A Villager with no way there stays where they are.
    /// </summary>
    /// <exception cref="ArgumentException">There is no Villager with that id.</exception>
    public void SendVillager(int villagerId, Vector2 destination) => _simulation.SendVillager(villagerId, destination);

    /// <summary>
    /// Moves the Store clock on to the next moment it shows <paramref name="timeOfDay"/>, at once: no time is simulated
    /// in between and no events are raised. Does nothing if the clock already shows it.
    /// </summary>
    public void SetTimeOfDay(TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeOfDay), "A time of day lies from 00:00 up to 24:00.");
        _simulation.SetTimeOfDay(timeOfDay);
    }

    /// <summary>
    /// Puts a new piece of Furniture (the next free id), standing upright, on the floor or at <paramref name="elevation"/>, on top
    /// of what stands there; walking Villagers find their way around it. A piece that is not held up, or a structure it overloads,
    /// collapses at the next tick (<see cref="StructureCollapsed"/>).
    /// </summary>
    public FurniturePiece PlaceFurniture(FurnitureType type, Vector2 position, float rotation = 0f, int variant = 0, int tint = 0,
        float elevation = 0f)
    {
        if (!Enum.IsDefined(type) || variant < 0 || variant >= FurnitureCatalog.VariantCount(type) || tint is < 0 or > 11 ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(rotation) || !float.IsFinite(elevation) || elevation < 0)
            throw new ArgumentException("Furniture needs a valid type, variant, tint and finite placement.");
        return _simulation.PlaceFurniture(type, position, rotation, variant, tint, elevation);
    }

    /// <summary>Moves a piece of Furniture to a new position and rotation; walking Villagers find their way again.</summary>
    /// <exception cref="ArgumentException">There is no piece with that id.</exception>
    public FurniturePiece MoveFurniture(int furnitureId, Vector2 position, float rotation) =>
        _simulation.MoveFurniture(furnitureId, position, rotation);
}
