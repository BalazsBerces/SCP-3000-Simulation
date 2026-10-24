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
/// The Pillar grid and its Sectors, 3 × 3 unless chosen otherwise. Part of the world state
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
public sealed record WorldConfig(int Seed, int? RuntimeRngSeed = null, PillarLayout PillarLayout = PillarLayout.ThreeByThree,
    bool LandmarkShowcase = false, bool PointOfInterestShowcase = false, StockConfig? Stock = null);
