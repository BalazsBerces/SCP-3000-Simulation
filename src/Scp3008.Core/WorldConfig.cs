namespace Scp3008.Core;

/// <summary>
/// Settings for creating a world.
/// </summary>
/// <param name="Seed">Drives Store generation only (ADR 0003); the same seed gives the same Store.</param>
/// <param name="RuntimeRngSeed">
/// Optional seed for runtime randomness. Leave null in normal play so every run unfolds differently;
/// tests set it to get reproducible outcomes.
/// </param>
public sealed record WorldConfig(int Seed, int? RuntimeRngSeed = null);
