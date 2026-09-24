namespace TokenControl.Core.Usage;

public enum Severity { Ok, Warning, Critical }

/// <param name="Id">Stable key used to compare snapshots across polls (e.g. "rolling", "monthly").</param>
/// <param name="ShowInIcon">Whether the tray tooltip lists this window.</param>
/// <param name="Period">Full length of the window, used to judge pace; null when unknown.</param>
public sealed record UsageWindow(
    string Id,
    string Title,
    double Used,
    double Limit,
    DateTimeOffset? ResetsAt,
    bool ShowInIcon = true,
    TimeSpan? Period = null)
{
    public const double WarningThreshold = 0.70;
    public const double CriticalThreshold = 0.90;

    public double Fraction => Limit > 0 ? Math.Clamp(Used / Limit, 0, 1) : 0;
    public double Remaining => Math.Max(0, Limit - Used);
    public bool IsExhausted => Limit > 0 && Used >= Limit;

    public Severity Severity => Fraction switch
    {
        >= CriticalThreshold => Severity.Critical,
        >= WarningThreshold => Severity.Warning,
        _ => Severity.Ok,
    };
}

public sealed record UsageSnapshot(
    string Provider,
    string Account,
    string Plan,
    IReadOnlyList<UsageWindow> Windows,
    string? Note,
    DateTimeOffset FetchedAt,
    string? Workspace = null)
{
    public UsageWindow? Find(string id) => Windows.FirstOrDefault(w => w.Id == id);
}

public abstract record FetchResult
{
    public sealed record Ok(UsageSnapshot Snapshot) : FetchResult;
    /// <summary>No session or the session was rejected: the user must sign in again.</summary>
    public sealed record SignedOut(string Message) : FetchResult;
    /// <param name="Transient">Network hiccup etc.; keep showing the last snapshot.</param>
    public sealed record Failed(string Message, bool Transient) : FetchResult;
}

public interface IUsageProvider
{
    string Name { get; }
    Task<FetchResult> FetchAsync(CancellationToken ct = default);
}
