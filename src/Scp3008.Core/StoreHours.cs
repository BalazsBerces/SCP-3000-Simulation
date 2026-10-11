namespace Scp3008.Core;

/// <summary>Which part of the Store day it is.</summary>
public enum DayPhase
{
    /// <summary>The Store lights are on and the Staff are passive.</summary>
    Open,

    /// <summary>The Store lights are out and the Staff are hostile.</summary>
    ClosingTime,
}

/// <summary>
/// The Store's opening hours and its lights. Closing time runs from 22:00 to 08:00, so about
/// 40% of each Store day is dark. The lights fade over <see cref="LightFade"/> leading up to
/// each change, so they are fully out when Closing time begins and fully on at reopening.
/// </summary>
public static class StoreHours
{
    /// <summary>Store time of day when Closing time begins.</summary>
    public static readonly TimeSpan ClosingTimeStarts = TimeSpan.FromHours(22);

    /// <summary>Store time of day when the Store reopens.</summary>
    public static readonly TimeSpan Reopens = TimeSpan.FromHours(8);

    /// <summary>How long the Store lights take to go out or come back on (12.5 real seconds at 1x).</summary>
    public static readonly TimeSpan LightFade = TimeSpan.FromMinutes(30);

    /// <summary>The phase of the Store day at <paramref name="timeOfDay"/>.</summary>
    public static DayPhase PhaseAt(TimeSpan timeOfDay)
    {
        var t = Normalize(timeOfDay);
        return t >= Reopens && t < ClosingTimeStarts ? DayPhase.Open : DayPhase.ClosingTime;
    }

    /// <summary>
    /// How bright the Store lights are at <paramref name="timeOfDay"/>, from 0 (out) to 1 (fully on).
    /// Continuous, so the presentation can sample it at interpolated Store times.
    /// </summary>
    public static double LightLevelAt(TimeSpan timeOfDay)
    {
        var t = Normalize(timeOfDay);
        if (t >= ClosingTimeStarts - LightFade && t < ClosingTimeStarts)
            return 1.0 - SmoothStep((t - (ClosingTimeStarts - LightFade)) / LightFade);
        if (t >= Reopens - LightFade && t < Reopens)
            return SmoothStep((t - (Reopens - LightFade)) / LightFade);
        return PhaseAt(t) == DayPhase.Open ? 1.0 : 0.0;
    }

    private static TimeSpan Normalize(TimeSpan timeOfDay)
    {
        var ticks = timeOfDay.Ticks % TimeSpan.TicksPerDay;
        return TimeSpan.FromTicks(ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks);
    }

    private static double SmoothStep(double x) => x * x * (3.0 - 2.0 * x);
}
