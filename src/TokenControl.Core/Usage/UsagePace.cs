namespace TokenControl.Core.Usage;

/// <param name="Expected">Share of the window already elapsed (0..1): having used exactly this much is "on pace".</param>
/// <param name="Projected">Usage expected by the reset if the current rate holds (0..1, capped).</param>
/// <param name="EmptiesAt">When the limit would be hit at the current rate, if that happens before the reset.</param>
public sealed record UsagePace(double Expected, double Projected, bool AheadOfPace, DateTimeOffset? EmptiesAt);

public static class UsagePaceCalculator
{
    // Very early in a window the rate is noise (a single request would "project" 100 %).
    private const double MinElapsedShare = 0.02;

    public static UsagePace? For(UsageWindow w, DateTimeOffset now)
    {
        if (w.Period is not { } period || period <= TimeSpan.Zero || w.ResetsAt is not { } resetsAt || w.Limit <= 0)
            return null;

        var elapsed = period - (resetsAt - now);
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed > period) elapsed = period;
        var expected = elapsed / period;

        var fraction = w.Fraction;
        if (fraction <= 0 || expected < MinElapsedShare)
            return new UsagePace(expected, fraction, AheadOfPace: false, EmptiesAt: null);

        var projected = Math.Min(1, fraction / expected);
        DateTimeOffset? emptiesAt = fraction < 1 && fraction / expected > 1
            ? now + elapsed * ((1 - fraction) / fraction)
            : null;
        return new UsagePace(expected, projected, AheadOfPace: fraction > expected, emptiesAt);
    }
}
