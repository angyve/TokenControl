using System.Text.Json;
using TokenControl.Core.Claude;

namespace TokenControl.Core.Tests;

public sealed class ClaudeUsageParserTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void ParsesSessionWeeklyAndExtraUsage()
    {
        var windows = ClaudeUsageParser.ParseUsage(Json("""
            {
              "seven_day": { "utilization": 41.0, "resets_at": "2026-11-12T09:00:00.000000+00:00" },
              "five_hour": { "utilization": 12.5, "resets_at": "2026-11-08T18:00:00+00:00" },
              "seven_day_oauth_apps": null,
              "seven_day_opus": { "utilization": 0.0, "resets_at": null },
              "seven_day_sonnet": { "utilization": 7.0, "resets_at": "2026-11-12T09:00:00+00:00" },
              "extra_usage": { "is_enabled": true, "monthly_limit": 5000, "used_credits": 1234, "utilization": 24.68 }
            }
            """));

        Assert.Equal(["five_hour", "seven_day", "seven_day_sonnet", "extra"], windows.Select(w => w.Id));

        var session = windows[0];
        Assert.Equal(0.125, session.Fraction, 3);
        Assert.Equal(TimeSpan.FromHours(5), session.Period);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero), session.ResetsAt);
        Assert.True(session.ShowInIcon);

        Assert.Equal(TimeSpan.FromDays(7), windows[1].Period);
        Assert.False(windows[2].ShowInIcon);

        var extra = windows[3];
        Assert.Equal(12.34, extra.Used, 2);
        Assert.Equal(50, extra.Limit);
        Assert.False(extra.ShowInIcon);
    }

    [Fact]
    public void DisabledExtraUsageAndGarbageAreIgnored()
    {
        var windows = ClaudeUsageParser.ParseUsage(Json("""
            { "five_hour": { "utilization": "30" }, "weird": 5, "extra_usage": { "is_enabled": false, "monthly_limit": 100 } }
            """));
        var only = Assert.Single(windows);
        Assert.Equal(0.30, only.Fraction, 3);
        Assert.Null(only.ResetsAt);

        Assert.Empty(ClaudeUsageParser.ParseUsage(Json("[]")));
    }

    [Fact]
    public void UnknownLimitsStillShow()
    {
        var w = Assert.Single(ClaudeUsageParser.ParseUsage(Json("""{ "seven_day_haiku": { "utilization": 3 } }""")));
        Assert.Equal("Semanal · Haiku", w.Title);
    }

    [Fact]
    public void PicksPaidChatOrganizationAndLabelsPlan()
    {
        var orgs = ClaudeUsageParser.ParseOrganizations(Json("""
            [
              { "uuid": "api-org", "name": "API", "capabilities": ["api"] },
              { "uuid": "max-org", "name": "Personal", "capabilities": ["chat", "claude_max"], "rate_limit_tier": "default_claude_max_20x" },
              { "name": "no id" }
            ]
            """));
        Assert.Equal(2, orgs.Count);

        var picked = ClaudeUsageParser.PickOrganization(orgs);
        Assert.Equal("max-org", picked?.Id);
        Assert.Equal("Max 20x", picked?.PlanLabel);

        Assert.Equal("Pro", new ClaudeOrganization("x", "x", ["chat", "claude_pro"], "default_claude_ai").PlanLabel);
        Assert.Equal("Free", new ClaudeOrganization("x", "x", ["chat"], null).PlanLabel);
    }

    [Fact]
    public void ReadsAccountEmail()
    {
        Assert.Equal("a@b.c", ClaudeUsageParser.ParseAccountEmail(Json("""{ "email_address": "a@b.c" }""")));
        Assert.Null(ClaudeUsageParser.ParseAccountEmail(Json("[]")));
    }
}
