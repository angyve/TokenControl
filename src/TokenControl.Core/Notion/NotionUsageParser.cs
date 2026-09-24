using System.Globalization;
using System.Text.Json;

namespace TokenControl.Core.Notion;

public sealed record NotionWorkspace(string Id, string Name, string? PlanType, string? SubscriptionTier)
{
    public string TierLabel => (SubscriptionTier ?? PlanType ?? "unknown").Replace('_', ' ');

    public bool CarriesAllowance =>
        $"{SubscriptionTier} {PlanType}".Contains("business", StringComparison.OrdinalIgnoreCase)
        || $"{SubscriptionTier} {PlanType}".Contains("enterprise", StringComparison.OrdinalIgnoreCase);
}

public sealed record UsageMeter(double Used, double Limit, DateTimeOffset? ResetsAt)
{
    public double Fraction => Limit > 0 ? Used / Limit : 0;
}

public sealed record NotionUsage(
    string? Status,
    UsageMeter? Rolling,
    string? RollingWindowLabel,
    UsageMeter? Monthly,
    UsageMeter? MonthlyCredits,
    double? CreditBalance)
{
    public bool NotApplicable => string.Equals(Status, "not_applicable", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Tolerant parsing of Notion's private API responses; missing fields yield nulls, never throws.</summary>
public static class NotionUsageParser
{
    public static IReadOnlyList<NotionWorkspace> ParseWorkspaces(JsonElement spaces)
    {
        var found = new List<NotionWorkspace>();
        if (spaces.ValueKind != JsonValueKind.Object) return found;

        // Shape: { <userId>: { space: { <spaceId>: { value: { value: { id, name, … } } } }, notion_user: … } }
        foreach (var user in spaces.EnumerateObject())
        {
            if (!TryGet(user.Value, "space", out var table) || table.ValueKind != JsonValueKind.Object) continue;
            foreach (var entry in table.EnumerateObject())
            {
                var record = UnwrapRecord(entry.Value);
                if (record is not { } r) continue;
                found.Add(new NotionWorkspace(
                    Id: Str(r, "id") ?? entry.Name,
                    Name: Str(r, "name") ?? "Workspace",
                    PlanType: Str(r, "plan_type") ?? Str(r, "planType"),
                    SubscriptionTier: Str(r, "subscription_tier") ?? Str(r, "subscriptionTier")));
            }
        }
        return found.DistinctBy(w => w.Id).ToList();
    }

    public static string? ParseAccountEmail(JsonElement spaces)
    {
        if (spaces.ValueKind != JsonValueKind.Object) return null;
        foreach (var user in spaces.EnumerateObject())
        {
            if (!TryGet(user.Value, "notion_user", out var users) || users.ValueKind != JsonValueKind.Object) continue;
            foreach (var entry in users.EnumerateObject())
                if (UnwrapRecord(entry.Value) is { } r && Str(r, "email") is { Length: > 0 } email)
                    return email;
        }
        return null;
    }

    public static NotionUsage ParseUsage(JsonElement rate, JsonElement? credits, DateTimeOffset now)
    {
        UsageMeter? rolling = null;
        string? rollingLabel = null;
        if (TryGet(rate, "window", out var w) && Num(w, "used") is { } used && Num(w, "limit") is { } limit)
        {
            var resets = Num(rate, "resetsInSeconds") is { } s ? now.AddSeconds(s) : (DateTimeOffset?)null;
            rolling = new UsageMeter(used, limit, resets);
            rollingLabel = Str(w, "window");
        }

        UsageMeter? monthly = null;
        DateTimeOffset? periodEnd = null;
        if (TryGet(rate, "billingPeriodWindow", out var m))
        {
            periodEnd = Num(m, "periodEndMs") is { } end ? DateTimeOffset.FromUnixTimeMilliseconds((long)end) : null;
            if (Num(m, "used") is { } mu && Num(m, "limit") is { } ml)
                monthly = new UsageMeter(mu, ml, periodEnd);
        }

        UsageMeter? monthlyCredits = null;
        double? balance = null;
        if (credits is { } c)
        {
            TryGet(c, "premiumCredits", out var premium);
            balance = Num(premium, "totalCreditBalance")
                ?? (TryGet(c, "usage", out var usage) ? Num(usage, "totalCreditBalance") : null);

            if (TryGet(premium, "perSource", out var perSource)
                && TryGet(perSource, "monthlyAllocated", out var alloc)
                && Num(alloc, "usageTotal") is { } cu && Num(alloc, "limit") is { } cl)
                monthlyCredits = new UsageMeter(cu, cl, periodEnd);
        }

        return new NotionUsage(Str(rate, "status"), rolling, rollingLabel, monthly, monthlyCredits, balance);
    }

    /// <summary>getSpaces nests records as { value: { role, value: { …fields } } }.</summary>
    private static JsonElement? UnwrapRecord(JsonElement entry)
    {
        var current = entry;
        while (current.ValueKind == JsonValueKind.Object)
        {
            if (Str(current, "id") is not null || Str(current, "email") is not null) return current;
            if (!TryGet(current, "value", out var next) || next.ValueKind != JsonValueKind.Object) return current;
            current = next;
        }
        return null;
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static string? Str(JsonElement e, string name) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name)
    {
        if (!TryGet(e, name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
