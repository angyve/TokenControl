using TokenControl.Core.Usage;

namespace TokenControl.Core.Tests;

public class UsagePaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 13, 0, 0, TimeSpan.Zero);

    private static UsageWindow Window(double used, TimeSpan? resetsIn, TimeSpan? period) =>
        new("rolling", "6 horas", used, 100, resetsIn is { } r ? Now + r : null, Period: period);

    [Fact]
    public void BelowPace()
    {
        // 3 of 6 hours gone, 20 % used.
        var pace = UsagePaceCalculator.For(Window(20, TimeSpan.FromHours(3), TimeSpan.FromHours(6)), Now)!;

        Assert.Equal(0.5, pace.Expected, 3);
        Assert.False(pace.AheadOfPace);
        Assert.Equal(0.4, pace.Projected, 3);
        Assert.Null(pace.EmptiesAt);
    }

    [Fact]
    public void AheadOfPaceProjectsWhenItEmpties()
    {
        // 1 of 4 hours gone, 50 % used: the other 50 % lasts one more hour.
        var pace = UsagePaceCalculator.For(Window(50, TimeSpan.FromHours(3), TimeSpan.FromHours(4)), Now)!;

        Assert.True(pace.AheadOfPace);
        Assert.Equal(1, pace.Projected);
        Assert.Equal(Now.AddHours(1), pace.EmptiesAt);
    }

    [Fact]
    public void AlreadyExhaustedHasNoEmptiesAt()
    {
        var pace = UsagePaceCalculator.For(Window(100, TimeSpan.FromHours(3), TimeSpan.FromHours(6)), Now)!;
        Assert.Null(pace.EmptiesAt);
        Assert.True(pace.AheadOfPace);
    }

    [Fact]
    public void TooEarlyInWindowDoesNotProject()
    {
        var pace = UsagePaceCalculator.For(Window(10, TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1), TimeSpan.FromHours(6)), Now)!;
        Assert.False(pace.AheadOfPace);
        Assert.Null(pace.EmptiesAt);
    }

    [Fact]
    public void UnknownPeriodOrResetYieldsNull()
    {
        Assert.Null(UsagePaceCalculator.For(Window(10, null, TimeSpan.FromHours(6)), Now));
        Assert.Null(UsagePaceCalculator.For(Window(10, TimeSpan.FromHours(1), null), Now));
    }
}
