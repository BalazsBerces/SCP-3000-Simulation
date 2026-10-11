using System.Numerics;

namespace Scp3008.Core;

/// <summary>Where a Fog bank is in its life.</summary>
public enum FogBankStage
{
    /// <summary>Thickening and spreading out to its full size.</summary>
    Forming,

    /// <summary>At full size and density, drifting with the wind.</summary>
    Lingering,

    /// <summary>Thinning out; it is gone once its density reaches zero.</summary>
    Dissipating,
}

/// <summary>
/// A Fog bank at one moment: a large, soft-edged region of thick fog that drifts with the wind
/// across the wrapping Store. Its fog is full <see cref="Density"/> within the inner
/// <see cref="CoreFraction"/> of its <see cref="Radius"/> and fades smoothly to nothing at the
/// edge; beyond the edge there is only the thin haze, which the core does not model.
/// </summary>
/// <param name="Id">Stable for the bank's whole life; use it to follow a bank between snapshots.</param>
/// <param name="Position">Centre of the bank on the floor, wrapped into the Store.</param>
/// <param name="Radius">Distance from the centre to where the fog ends, in metres.</param>
/// <param name="Density">Fog density at the centre, from 0 (none) to 1 (thickest).</param>
/// <param name="Stage">Where the bank is in its life.</param>
public sealed record FogBank(int Id, Vector2 Position, float Radius, float Density, FogBankStage Stage)
{
    /// <summary>Share of the radius around the centre where the fog is at full density.</summary>
    public const float CoreFraction = 0.35f;

    /// <summary>This bank's fog density at <paramref name="position"/>, using the shortest wrapped distance.</summary>
    public float DensityAt(StoreSpace space, Vector2 position) =>
        Density * Falloff(space.Distance(Position, position) / Radius);

    /// <summary>
    /// How much of a bank's density is left at <paramref name="fractionOfRadius"/> of its radius
    /// from the centre: 1 within <see cref="CoreFraction"/>, easing smoothly to 0 at the edge.
    /// </summary>
    public static float Falloff(float fractionOfRadius)
    {
        if (fractionOfRadius >= 1f)
            return 0f;
        var t = Math.Clamp((fractionOfRadius - CoreFraction) / (1f - CoreFraction), 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The combined fog density of several banks at a position, from 0 to 1. Overlapping banks
    /// thicken each other but never beyond 1.
    /// </summary>
    public static float CombinedDensityAt(StoreSpace space, IEnumerable<FogBank> banks, Vector2 position)
    {
        var clear = 1f;
        foreach (var bank in banks)
            clear *= 1f - bank.DensityAt(space, position);
        return 1f - clear;
    }
}
