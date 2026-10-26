namespace Scp3008.Core;

/// <summary>
/// Settings for creating a world.
/// </summary>
/// <param name="Seed">Drives Store generation only (ADR 0003); the same seed gives the same Store.</param>
/// <param name="RuntimeRngSeed">
/// Optional seed for runtime randomness. Leave null in normal play so every run unfolds differently;
/// tests set it to get reproducible outcomes.
/// </param>
/// <param name="PillarLayout">
/// The Pillar grid and its Sectors, 3 Ă— 3 unless chosen otherwise. Part of the world state
/// (<see cref="WorldSnapshot.PillarLayout"/>), so a world is saved and restored with it.
/// </param>
/// <param name="LandmarkShowcase">
/// Builds the Landmark showcase instead of a normal Store: every Landmark type once, side by side
/// in one row on empty Store floor, with no Displays
/// and no Points of interest. The seed still varies how each Landmark is built.
/// </param>
/// <param name="PointOfInterestShowcase">
/// Builds the Point of interest showcase instead of a normal Store: every Point of interest type once, side by side
/// in one row on empty Store floor, with no Displays and no Landmarks.
/// The seed still varies how each one is built. Not combined with <paramref name="LandmarkShowcase"/>.
/// </param>
/// <param name="Stock">How much Goods the Store generator puts on the Stock Furniture, and the left-behind share; the defaults if null.</param>
/// <param name="Water">How many showroom taps run; the defaults if null.</param>
/// <param name="Needs">Store hours of starvation and thirst before death; the defaults if null.</param>
/// <param name="Restock">Goods refill rate and Furniture return delays; the defaults if null.</param>
/// <param name="StartingBands">Populates a normal Store with Bands; false for isolated scenarios. Sandboxes and showcases stay hand-placed.</param>
public sealed record WorldConfig(int Seed, int? RuntimeRngSeed = null, PillarLayout PillarLayout = PillarLayout.ThreeByThree,
    bool LandmarkShowcase = false, bool PointOfInterestShowcase = false, StockConfig? Stock = null,
    WaterConfig? Water = null, NeedsConfig? Needs = null, RestockConfig? Restock = null,
    bool StartingBands = true);
