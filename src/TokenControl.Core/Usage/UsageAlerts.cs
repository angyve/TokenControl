namespace TokenControl.Core.Usage;

public enum AlertKind { RunningLow, Exhausted, Reset }

public sealed record UsageAlert(AlertKind Kind, UsageWindow Window);

/// <summary>Decides which notifications to raise by comparing consecutive snapshots.</summary>
public static class UsageAlerts
{
    public static IReadOnlyList<UsageAlert> Evaluate(UsageSnapshot? previous, UsageSnapshot current)
    {
        // First snapshot after launch: nothing to compare, don't spam on startup.
        if (previous is null) return [];

        var alerts = new List<UsageAlert>();
        foreach (var now in current.Windows)
        {
            if (previous.Find(now.Id) is not { } before) continue;

            if (now.IsExhausted && !before.IsExhausted)
                alerts.Add(new(AlertKind.Exhausted, now));
            else if (now.Fraction >= UsageWindow.CriticalThreshold && before.Fraction < UsageWindow.CriticalThreshold)
                alerts.Add(new(AlertKind.RunningLow, now));
            // Only a reset that frees you from a tight spot is worth interrupting for.
            else if (before.Fraction >= UsageWindow.CriticalThreshold && now.Fraction < UsageWindow.WarningThreshold)
                alerts.Add(new(AlertKind.Reset, now));
        }
        return alerts;
    }
}

public static class PollPolicy
{
    public static readonly TimeSpan Normal = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Fast = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SignedOut = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResetSoon = TimeSpan.FromMinutes(10);

    /// <summary>Poll faster when usage is tight or a reset is imminent, so alerts land promptly.</summary>
    public static TimeSpan NextDelay(UsageSnapshot snapshot, DateTimeOffset now) =>
        snapshot.Windows.Any(w =>
            w.Fraction >= UsageWindow.CriticalThreshold
            || (w.Used > 0 && w.ResetsAt is { } r && r - now <= ResetSoon))
            ? Fast
            : Normal;
}
