namespace Scp3008.Core;

/// <summary>
/// The Store's clock, measured in fixed simulation ticks. A Store day is 24 Store hours;
/// at 1x speed it lasts 10 real minutes.
/// </summary>
public readonly record struct StoreClock(long Tick)
{
    /// <summary>Simulation ticks in one Store day.</summary>
    public const long TicksPerStoreDay = 12_000;

    /// <summary>Store time that passes in one tick (7.2 Store seconds).</summary>
    public static readonly TimeSpan TickDuration = TimeSpan.FromTicks(TimeSpan.TicksPerDay / TicksPerStoreDay);

    /// <summary>A new world starts on day 1 at Store opening.</summary>
    public static readonly TimeSpan StartTimeOfDay = TimeSpan.FromHours(8);

    private static readonly long StartOffsetTicks = StartTimeOfDay.Ticks / TickDuration.Ticks;

    private long TicksSinceDayOneMidnight => Tick + StartOffsetTicks;

    /// <summary>The Store day number, starting at 1.</summary>
    public int Day => (int)(TicksSinceDayOneMidnight / TicksPerStoreDay) + 1;

    /// <summary>Store time of day, from 00:00 up to (not including) 24:00.</summary>
    public TimeSpan TimeOfDay => TickDuration * (TicksSinceDayOneMidnight % TicksPerStoreDay);

    /// <summary>Total Store time elapsed since day 1 00:00; continuous, useful for interpolation.</summary>
    public TimeSpan TotalStoreTime => TickDuration * TicksSinceDayOneMidnight;

    /// <summary>Whether the Store is open or in Closing time.</summary>
    public DayPhase Phase => StoreHours.PhaseAt(TimeOfDay);

    /// <summary>How bright the Store lights are, from 0 (out) to 1 (fully on).</summary>
    public double StoreLightLevel => StoreHours.LightLevelAt(TimeOfDay);

    /// <summary>Converts a Store-time duration into whole ticks, rounding to the nearest tick.</summary>
    public static long TicksFor(TimeSpan storeTime) =>
        (long)Math.Round((double)storeTime.Ticks / TickDuration.Ticks, MidpointRounding.AwayFromZero);
}
