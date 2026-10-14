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

    /// <summary>How long one display Lamp takes to come on or go out: a short ramp, not a pop.</summary>
    public static readonly TimeSpan LampRamp = TimeSpan.FromMinutes(4);

    /// <summary>
    /// How far display Lamp <paramref name="lampId"/> has come on at <paramref name="timeOfDay"/>, from 0 (off) to 1
    /// (fully lit). Off while the Store is open, fully lit during Closing time. In the <see cref="LightFade"/> before
    /// Closing time each Lamp ramps up at its own scattered moment, and in the fade before reopening each ramps down
    /// at its own moment. The moment depends only on the Lamp's id, so it is the same every Store day and every run.
    /// Continuous in time, so the presentation can sample it at interpolated Store times. The dead zone (where no
    /// Lamp is ever lit) is applied by whoever knows the Landmarks, not here.
    /// </summary>
    public static float LampLevelAt(int lampId, TimeSpan timeOfDay)
    {
        var t = Normalize(timeOfDay);
        if (t >= ClosingTimeStarts - LightFade && t < ClosingTimeStarts)
            return Ramp(lampId, t - (ClosingTimeStarts - LightFade));
        if (t >= Reopens - LightFade && t < Reopens)
            return 1f - Ramp(lampId, t - (Reopens - LightFade));
        return PhaseAt(t) == DayPhase.Open ? 0f : 1f;
    }

    /// <summary>Whether <paramref name="timeOfDay"/> lies inside one of the two fades, when Lamps are switching.</summary>
    public static bool LampsSwitchingAt(TimeSpan timeOfDay)
    {
        var t = Normalize(timeOfDay);
        return t >= ClosingTimeStarts - LightFade && t < ClosingTimeStarts || t >= Reopens - LightFade && t < Reopens;
    }

    // 0 until the Lamp's own moment within the fade, then a smoothstep over LampRamp; the latest moment still ends with the fade.
    private static float Ramp(int lampId, TimeSpan sinceFadeStarted)
    {
        var start = (LightFade - LampRamp) * ScatterOf(lampId);
        return (float)SmoothStep(Math.Clamp((sinceFadeStarted - start) / LampRamp, 0.0, 1.0));
    }

    // A fixed per-id fraction in [0, 1): an integer hash, so no seed or run-to-run state is involved.
    private static double ScatterOf(int lampId)
    {
        var x = (uint)lampId * 0x9E3779B9u;
        x ^= x >> 16; x *= 0x85EBCA6Bu;
        x ^= x >> 13; x *= 0xC2B2AE35u;
        x ^= x >> 16;
        return x / 4294967296.0;
    }

    private static TimeSpan Normalize(TimeSpan timeOfDay)
    {
        var ticks = timeOfDay.Ticks % TimeSpan.TicksPerDay;
        return TimeSpan.FromTicks(ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks);
    }

    private static double SmoothStep(double x) => x * x * (3.0 - 2.0 * x);
}
