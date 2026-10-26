namespace Scp3008.Core;

/// <summary>Restock tuning in Store hours. Per-kind rates and balance tuning belong to ticket 85.</summary>
public sealed record RestockConfig
{
    /// <summary>Units added per Stock Furniture Goods kind per Store hour, capped at its stock target; zero disables refill.</summary>
    public double GoodsPerStoreHour { get; init; } = 0.125;
    /// <summary>Store hours after a spot is vacated before ordinary Furniture can return; zero disables return.</summary>
    public double FurnitureHours { get; init; } = 24;
    /// <summary>Store hours after a spot is vacated before Landmark Furniture can return; zero disables return.</summary>
    public double LandmarkFurnitureHours { get; init; } = 72;

    internal void Validate()
    {
        if (!double.IsFinite(GoodsPerStoreHour) || GoodsPerStoreHour < 0 ||
            !double.IsFinite(FurnitureHours) || FurnitureHours < 0 ||
            !double.IsFinite(LandmarkFurnitureHours) || LandmarkFurnitureHours < 0)
            throw new ArgumentOutOfRangeException(nameof(RestockConfig), "Restock rates and delays must be finite and nonnegative.");
    }
}
