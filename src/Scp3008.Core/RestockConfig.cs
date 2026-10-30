namespace Scp3008.Core;

/// <summary>Restock tuning in Store hours. Per-kind rates and balance tuning belong to ticket 85.</summary>
public sealed record RestockConfig
{
    /// <summary>Units added per restocked (Cafeteria or Camping ground) Goods kind per Store hour, capped at its stock target; zero disables refill.</summary>
    public double GoodsPerStoreHour { get; init; } = 0.125;
    /// <summary>Store hours after a spot is vacated before Cafeteria Furniture can return; zero disables return.</summary>
    public double FurnitureHours { get; init; } = 24;
    /// <summary>Store hours after a spot is vacated before Camping ground Furniture can return; zero disables return.</summary>
    public double LandmarkFurnitureHours { get; init; } = 72;

    /// <summary>Mean wait between Dry spells in Store hours, varied from half to one-and-a-half times this value; zero disables them.</summary>
    public double DrySpellIntervalHours { get; init; } = 72;
    /// <summary>Length of a Dry spell in Store hours (at least one simulation tick).</summary>
    public double DrySpellDurationHours { get; init; } = 72;
    /// <summary>Fraction of Restock removed in one Department and added in another, from 0 to 1 (a complete stop).</summary>
    public double DrySpellStrength { get; init; } = 0.875;

    internal void Validate()
    {
        if (!double.IsFinite(GoodsPerStoreHour) || GoodsPerStoreHour < 0 ||
            !double.IsFinite(FurnitureHours) || FurnitureHours < 0 ||
            !double.IsFinite(LandmarkFurnitureHours) || LandmarkFurnitureHours < 0 ||
            !double.IsFinite(DrySpellIntervalHours) || DrySpellIntervalHours < 0 ||
            !double.IsFinite(DrySpellDurationHours) || DrySpellDurationHours < StoreClock.TickDuration.TotalHours ||
            DrySpellDurationHours > TimeSpan.MaxValue.TotalHours / 2 ||
            !double.IsFinite(DrySpellStrength) || DrySpellStrength < 0 || DrySpellStrength > 1)
            throw new ArgumentOutOfRangeException(nameof(RestockConfig), "Restock rates and delays must be finite and nonnegative; Dry spell duration must span a tick and strength must be from 0 to 1.");
    }
}
