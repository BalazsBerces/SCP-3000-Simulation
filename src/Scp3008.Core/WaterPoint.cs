using System.Numerics;

namespace Scp3008.Core;

/// <summary>Where a <see cref="WaterPoint"/> is.</summary>
public enum WaterPointKind
{
    /// <summary>One of the washbasins of a Restroom; always runs.</summary>
    RestroomWashbasin,
    /// <summary>The fountain court's water; always there.</summary>
    FountainCourt,
    /// <summary>The Camping ground's lake; always there.</summary>
    CampingLake,
    /// <summary>A showroom bathroom sink (a Bathroom Display's vanity unit) or kitchen sink (a Kitchen Display's first cabinet); most are dry.</summary>
    ShowroomTap,
}

/// <summary>
/// A place where a Villager can drink. Which showroom taps run is hidden truth the seed fixes once: it never changes, and a
/// running tap looks like a dry one (Villagers learn it only by trying).
/// </summary>
/// <param name="Kind">What kind of place it is.</param>
/// <param name="Position">Where it is on the floor, in metres.</param>
/// <param name="Running">Whether it gives water: always true except for the showroom taps the seed leaves dry.</param>
/// <param name="FurnitureId">The sink or washbasin, by <see cref="FurniturePiece.Id"/>; -1 for the fountain court and the lake.</param>
public readonly record struct WaterPoint(WaterPointKind Kind, Vector2 Position, bool Running, int FurnitureId);

/// <summary>How the Store generator picks the running showroom taps.</summary>
public sealed record WaterConfig
{
    /// <summary>
    /// The share of showroom sinks that run, rounded up so a Store with any sinks has at least one when the share is above zero.
    /// </summary>
    public float RunningTapShare { get; init; } = 0.05f;
}
