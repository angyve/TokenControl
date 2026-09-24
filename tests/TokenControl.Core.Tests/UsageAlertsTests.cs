using TokenControl.Core.Usage;

namespace TokenControl.Core.Tests;

public class UsageAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 13, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snap(double rollingUsed, DateTimeOffset? resetsAt = null) =>
        new("Notion AI", "me", "plan", [new UsageWindow("rolling", "6 horas", rollingUsed, 100, resetsAt)], null, Now);

    [Fact]
    public void NoAlertsOnFirstSnapshot() =>
        Assert.Empty(UsageAlerts.Evaluate(null, Snap(100)));

    [Theory]
    [InlineData(50, 92, AlertKind.RunningLow)]
    [InlineData(92, 100, AlertKind.Exhausted)]
    [InlineData(50, 100, AlertKind.Exhausted)]
    [InlineData(100, 0, AlertKind.Reset)]
    [InlineData(95, 10, AlertKind.Reset)]
    public void RaisesAlertOnTransition(double before, double after, AlertKind expected)
    {
        var alert = Assert.Single(UsageAlerts.Evaluate(Snap(before), Snap(after)));
        Assert.Equal(expected, alert.Kind);
        Assert.Equal("rolling", alert.Window.Id);
    }

    [Theory]
    [InlineData(10, 60)]   // still comfortable
    [InlineData(92, 95)]   // already warned
    [InlineData(100, 100)] // already exhausted
    [InlineData(60, 5)]    // reset while not constrained
    [InlineData(95, 80)]   // partial drop, not a real reset
    public void StaysQuietOtherwise(double before, double after) =>
        Assert.Empty(UsageAlerts.Evaluate(Snap(before), Snap(after)));

    [Theory]
    [InlineData(10, null, 120)]
    [InlineData(91, null, 30)]
    [InlineData(10, 5, 30)]    // reset in 5 min
    [InlineData(10, 60, 120)]  // reset in an hour
    [InlineData(0, 5, 120)]    // nothing used, reset is irrelevant
    public void PollsFasterWhenTightOrResetIsNear(double used, int? resetMinutes, int expectedSeconds)
    {
        var snap = Snap(used, resetMinutes is { } m ? Now.AddMinutes(m) : null);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PollPolicy.NextDelay(snap, Now));
    }

    [Theory]
    [InlineData(0, Severity.Ok)]
    [InlineData(69, Severity.Ok)]
    [InlineData(70, Severity.Warning)]
    [InlineData(90, Severity.Critical)]
    [InlineData(150, Severity.Critical)]
    public void SeverityThresholds(double used, Severity expected) =>
        Assert.Equal(expected, new UsageWindow("x", "x", used, 100, null).Severity);
}
