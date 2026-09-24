using System.Text.Json;
using TokenControl.Core.Notion;

namespace TokenControl.Core.Tests;

public class NotionUsageParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 13, 0, 0, TimeSpan.Zero);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParsesRateLimitAndCredits()
    {
        // Shape captured from a real Business workspace (2026-09-24).
        var rate = Json("""
            {
              "status": "within_limit",
              "resetsInSeconds": 3600,
              "window": { "window": "6h", "used": 25, "limit": 100 },
              "billingPeriodWindow": { "used": 16.61, "limit": 100, "periodEndMs": 1792281600000 }
            }
            """);
        var credits = Json("""
            {
              "premiumCredits": {
                "totalCreditBalance": 300,
                "perSource": { "monthlyAllocated": { "usageTotal": 12, "limit": 300 } }
              }
            }
            """);

        var usage = NotionUsageParser.ParseUsage(rate, credits, Now);

        Assert.Equal("within_limit", usage.Status);
        Assert.Equal("6h", usage.RollingWindowLabel);
        Assert.Equal(new UsageMeter(25, 100, Now.AddHours(1)), usage.Rolling);
        Assert.Equal(16.61, usage.Monthly!.Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792281600000), usage.Monthly.ResetsAt);
        Assert.Equal(new UsageMeter(12, 300, usage.Monthly.ResetsAt), usage.MonthlyCredits);
        Assert.Equal(300, usage.CreditBalance);
    }

    [Fact]
    public void MissingResetAndCreditsYieldNulls()
    {
        // Notion omits resetsInSeconds when nothing has been used in the window.
        var rate = Json("""{ "status": "within_limit", "window": { "used": 0, "limit": 100 } }""");

        var usage = NotionUsageParser.ParseUsage(rate, credits: null, Now);

        Assert.Null(usage.Rolling!.ResetsAt);
        Assert.Null(usage.Monthly);
        Assert.Null(usage.MonthlyCredits);
        Assert.Null(usage.CreditBalance);
    }

    [Fact]
    public void AcceptsNumericStrings()
    {
        var rate = Json("""{ "window": { "used": "40.5", "limit": "100" } }""");
        Assert.Equal(40.5, NotionUsageParser.ParseUsage(rate, null, Now).Rolling!.Used);
    }

    [Fact]
    public void DetectsNotApplicable()
    {
        var rate = Json("""{ "status": "not_applicable" }""");
        var usage = NotionUsageParser.ParseUsage(rate, null, Now);
        Assert.True(usage.NotApplicable);
        Assert.Null(usage.Rolling);
    }

    [Fact]
    public void ParsesNestedWorkspacesAndEmail()
    {
        var spaces = Json("""
            {
              "user-1": {
                "space": {
                  "space-a": { "value": { "role": "editor", "value": { "id": "space-a", "name": "Personal", "plan_type": "personal" } } },
                  "space-b": { "spaceId": "space-b", "value": { "value": { "id": "space-b", "name": "Team", "subscription_tier": "business" } } }
                },
                "notion_user": { "user-1": { "value": { "value": { "id": "user-1", "email": "me@example.com" } } } }
              }
            }
            """);

        var workspaces = NotionUsageParser.ParseWorkspaces(spaces);

        Assert.Equal(2, workspaces.Count);
        Assert.False(workspaces.Single(w => w.Id == "space-a").CarriesAllowance);
        Assert.True(workspaces.Single(w => w.Id == "space-b").CarriesAllowance);
        Assert.Equal("me@example.com", NotionUsageParser.ParseAccountEmail(spaces));
    }

    [Fact]
    public void GarbageInputDoesNotThrow()
    {
        Assert.Empty(NotionUsageParser.ParseWorkspaces(Json("[]")));
        Assert.Null(NotionUsageParser.ParseAccountEmail(Json("\"x\"")));
        Assert.Null(NotionUsageParser.ParseUsage(Json("{}"), Json("[]"), Now).Rolling);
    }
}
