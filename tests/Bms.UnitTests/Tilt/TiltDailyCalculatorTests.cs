using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Tilt;
using Bms.Domain.Entities;

namespace Bms.UnitTests.Tilt;

public class TiltDailyCalculatorTests
{
    private static readonly TiltAxes Axes = new() { X = "xDeg", Y = "yDeg" };
    private static TelemetryPoint Point(string at, double x, double y) => new(DateTimeOffset.Parse(at), new Dictionary<string, double> { ["xDeg"] = x, ["yDeg"] = y });

    [Fact]
    public void SelectsLastCompleteActualReadingAtOrBeforeCutoff()
    {
        var cutoff = DateTimeOffset.Parse("2026-10-31T15:55:00Z");
        var rows = new[] { Point("2026-10-31T15:54:00Z", 3, 4), Point("2026-10-31T15:55:00Z", 2, 4), Point("2026-10-31T15:55:01Z", 9, 9) };
        var selected = TiltDailyCalculator.Select(rows, Axes, cutoff.AddHours(-24), cutoff);
        Assert.NotNull(selected);
        Assert.Equal(2, selected.X);
        Assert.Equal(4, selected.Y);
        Assert.Null(selected.Z);
        Assert.Equal(cutoff, selected.ReadingAt);
    }

    [Fact]
    public void SkipsPartialAndNonFinitePacketsWithoutCombiningDifferentTimestamps()
    {
        var time = DateTimeOffset.Parse("2026-10-31T12:00:00Z");
        var rows = new[] { Point(time.ToString("O"), 3, 4), new TelemetryPoint(time.AddMinutes(1), new Dictionary<string,double>{{"xDeg",5}}), Point(time.AddMinutes(2).ToString("O"), double.NaN, 6) };
        Assert.Equal(3, TiltDailyCalculator.Select(rows, Axes, time.AddHours(-1), time.AddHours(1))!.X);
        Assert.Null(TiltDailyCalculator.Select(rows, new TiltAxes { X="xDeg",Y="yDeg",Z="zDeg" }, time.AddHours(-1), time.AddHours(1)));
    }

    [Fact]
    public void SignedDifferencesUsePreviousDayAndFixedOnboardingIndependently()
    {
        var baseline = new TiltValues(3,4,null);
        var yesterday = new TiltValues(2,4,null);
        var today = new TiltValues(4,5,null);
        Assert.Equal(new TiltValues(2,1,null), TiltDailyCalculator.Difference(today,yesterday));
        Assert.Equal(new TiltValues(1,1,null), TiltDailyCalculator.Difference(today,baseline));
        Assert.Equal(new TiltValues(null,null,null), TiltDailyCalculator.Difference(today,null));
    }

    [Fact]
    public void SingaporeCutoffAndMidnightUseTheConfiguredLocalCalendarDay()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T15:55:00Z"), TiltDailyCalculator.AtLocal(new DateOnly(2026,10,31),new TimeOnly(23,55),"Asia/Singapore"));
        Assert.Equal(DateTimeOffset.Parse("2026-10-30T16:00:00Z"), TiltDailyCalculator.AtLocal(new DateOnly(2026,10,31),TimeOnly.MinValue,"Asia/Singapore"));
    }

    [Fact]
    public void DstUsesLaterAmbiguousInstantAndFirstValidMinuteInSpringGap()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:30:00Z"),TiltDailyCalculator.AtLocal(new DateOnly(2026,11,1),new TimeOnly(1,30),"America/New_York"));
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:00:00Z"),TiltDailyCalculator.AtLocal(new DateOnly(2026,3,8),new TimeOnly(2,30),"America/New_York"));
    }

    [Fact]
    public void EmptyDayStaysMissingRatherThanReusingEarlierDays()
    {
        var start=DateTimeOffset.Parse("2026-10-31T00:00:00Z");
        Assert.Null(TiltDailyCalculator.Select(new[]{Point("2026-10-30T23:59:00Z",1,2)},Axes,start,start.AddHours(23)));
    }
}
