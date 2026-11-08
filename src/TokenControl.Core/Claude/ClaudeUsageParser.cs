using System.Globalization;
using System.Text.Json;
using TokenControl.Core.Usage;

namespace TokenControl.Core.Claude;

public sealed record ClaudeOrganization(string Id, string Name, IReadOnlyList<string> Capabilities, string? RateLimitTier)
{
    public bool IsChat => Capabilities.Contains("chat") || Capabilities.Any(c => c.StartsWith("claude_", StringComparison.Ordinal));
    public bool IsPaid => Capabilities.Any(c => c is "claude_pro" or "claude_max" or "raven");

    /// <summary>"Max 20x", "Max 5x", "Pro", "Team"… ; empty when unknown.</summary>
    public string PlanLabel
    {
        get
        {
            if (Capabilities.Contains("claude_max"))
            {
                var mult = RateLimitTier is { } t && t.LastIndexOf('_') is var i and >= 0 && t[(i + 1)..].EndsWith('x') ? t[(i + 1)..] : null;
                return mult is null ? "Max" : $"Max {mult}";
            }
            if (Capabilities.Contains("raven")) return "Team";
            if (Capabilities.Contains("claude_pro")) return "Pro";
            if (Capabilities.Contains("chat")) return "Free";
            return "";
        }
    }
}

/// <summary>Tolerant parsing of claude.ai's private API responses; missing fields are skipped, never throws.</summary>
public static class ClaudeUsageParser
{
    public static IReadOnlyList<ClaudeOrganization> ParseOrganizations(JsonElement json)
    {
        var found = new List<ClaudeOrganization>();
        if (json.ValueKind != JsonValueKind.Array) return found;
        foreach (var org in json.EnumerateArray())
        {
            if (org.ValueKind != JsonValueKind.Object || Str(org, "uuid") is not { } id) continue;
            var caps = org.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
            found.Add(new ClaudeOrganization(id, Str(org, "name") ?? "Claude", caps, Str(org, "rate_limit_tier")));
        }
        return found;
    }

    /// <summary>The organization whose limits the Usage page shows: a paid chat plan first, then any chat org.</summary>
    public static ClaudeOrganization? PickOrganization(IReadOnlyList<ClaudeOrganization> orgs) =>
        orgs.FirstOrDefault(o => o.IsPaid) ?? orgs.FirstOrDefault(o => o.IsChat) ?? orgs.FirstOrDefault();

    public static string? ParseAccountEmail(JsonElement account) =>
        account.ValueKind == JsonValueKind.Object ? Str(account, "email_address") ?? Str(account, "email") : null;

    /// <summary>
    /// Shape: { five_hour: { utilization: 0-100, resets_at }, seven_day: {…}, seven_day_opus: {…} | null, …,
    /// extra_usage: { is_enabled, monthly_limit, used_credits } }. Unknown limits are shown too, with a generic title.
    /// </summary>
    public static IReadOnlyList<UsageWindow> ParseUsage(JsonElement usage)
    {
        var windows = new List<UsageWindow>();
        if (usage.ValueKind != JsonValueKind.Object) return windows;

        foreach (var prop in usage.EnumerateObject())
        {
            var v = prop.Value;
            if (v.ValueKind != JsonValueKind.Object || prop.Name == "extra_usage") continue;
            if (Num(v, "utilization") is not { } pct) continue;
            var resetsAt = Date(v, "resets_at");
            var (title, period, showInIcon) = Describe(prop.Name);
            // Model-specific weekly caps with no use and no reset are noise.
            if (!showInIcon && pct <= 0 && resetsAt is null) continue;
            windows.Add(new UsageWindow(prop.Name, title, pct, 100, resetsAt, showInIcon, period));
        }

        if (usage.TryGetProperty("extra_usage", out var extra) && extra.ValueKind == JsonValueKind.Object
            && Bool(extra, "is_enabled") == true
            && Num(extra, "monthly_limit") is { } limitCents and > 0)
        {
            var usedCents = Num(extra, "used_credits") ?? 0;
            windows.Add(new UsageWindow("extra", "Uso extra (mensual)", usedCents / 100, limitCents / 100, ResetsAt: null, ShowInIcon: false));
        }

        return windows
            .OrderBy(w => w.Id switch { "five_hour" => 0, "seven_day" => 1, "extra" => 9, _ => 5 })
            .ToList();
    }

    private static (string Title, TimeSpan? Period, bool ShowInIcon) Describe(string id) => id switch
    {
        "five_hour" => ("Sesión actual (5 h)", TimeSpan.FromHours(5), true),
        "seven_day" => ("Semanal · todos los modelos", TimeSpan.FromDays(7), true),
        "seven_day_opus" => ("Semanal · Opus", TimeSpan.FromDays(7), false),
        "seven_day_sonnet" => ("Semanal · Sonnet", TimeSpan.FromDays(7), false),
        "seven_day_oauth_apps" => ("Semanal · apps conectadas", TimeSpan.FromDays(7), false),
        _ when id.StartsWith("seven_day_", StringComparison.Ordinal) =>
            ("Semanal · " + Humanize(id["seven_day_".Length..]), TimeSpan.FromDays(7), false),
        _ => (Humanize(id), null, false),
    };

    private static string Humanize(string id)
    {
        var words = id.Replace('_', ' ').Trim();
        return words.Length == 0 ? "Límite" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
