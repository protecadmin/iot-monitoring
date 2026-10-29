using Bms.Domain.Calculations;
using Xunit;

namespace Bms.UnitTests.Calculations;

/// <summary>
/// Window boundaries decide which readings an average covers, so they are
/// pinned here: they must match Flux aggregateWindow's epoch alignment, or a
/// published value and the history of it would describe different periods.
/// </summary>
public class LevelWindowTests
{
    [Theory]
    // Five-minute windows start on the clock's five-minute marks.
    [InlineData("2026-10-19T08:00:00Z", 300, "2026-10-19T08:00:00Z")]
    [InlineData("2026-10-19T08:04:59Z", 300, "2026-10-19T08:00:00Z")]
    [InlineData("2026-10-19T08:05:00Z", 300, "2026-10-19T08:05:00Z")]
    // An 8-hour window divides the day at 00:00, 08:00 and 16:00.
    [InlineData("2026-10-19T15:59:59Z", 28_800, "2026-10-19T08:00:00Z")]
    [InlineData("2026-10-19T16:00:00Z", 28_800, "2026-10-19T16:00:00Z")]
    public void StartOf_AlignsToTheEpoch(string instant, int window, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), LevelWindow.StartOf(DateTimeOffset.Parse(instant), window));
    }

    [Fact]
    public void StartOf_IsNeverAfterTheInstant_BeforeTheEpoch()
    {
        var instant = new DateTimeOffset(1969, 12, 31, 23, 59, 0, TimeSpan.Zero);
        Assert.True(LevelWindow.StartOf(instant, 300) <= instant);
    }

    [Theory]
    // A range ending mid-window is widened to the window's real end, so Flux
    // cannot label a clipped window with the time the query happened to stop.
    [InlineData("2026-10-19T10:37:52Z", 60, "2026-10-19T10:38:00Z")]
    [InlineData("2026-10-19T10:38:00Z", 60, "2026-10-19T10:38:00Z")] // already a boundary
    [InlineData("2026-10-19T10:37:52Z", 300, "2026-10-19T10:40:00Z")]
    public void EndOf_WidensToTheWindowsRealEnd(string instant, int window, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), LevelWindow.EndOf(DateTimeOffset.Parse(instant), window));
    }

    [Theory]
    [InlineData("2026-10-19T08:00:00Z", "2026-10-19T08:04:59Z", false)] // same window
    [InlineData("2026-10-19T08:04:59Z", "2026-10-19T08:05:00Z", true)]  // the boundary itself closes it
    [InlineData("2026-10-19T08:00:00Z", "2026-10-19T09:30:00Z", true)]  // a gap skips whole windows
    public void CrossesWindow_SaysWhetherTheOpenWindowIsNowComplete(string earlier, string later, bool crosses)
    {
        Assert.Equal(crosses, LevelWindow.CrossesWindow(DateTimeOffset.Parse(earlier), DateTimeOffset.Parse(later), 300));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3_600)]
    [InlineData("1d", 86_400)]
    [InlineData("1w", 604_800)]
    [InlineData("1mo", 2_592_000)]
    public void TryParseFluxSeconds_ReadsTheDurationsTheStoreUses(string duration, int expected)
    {
        Assert.True(LevelWindow.TryParseFluxSeconds(duration, out var seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("0m")]
    [InlineData("-5m")]
    [InlineData("500ms")]
    [InlineData("every")]
    public void TryParseFluxSeconds_RejectsWhatItCannotCompare(string? duration)
    {
        Assert.False(LevelWindow.TryParseFluxSeconds(duration, out _));
    }

    [Fact]
    public void ToFluxDuration_StaysInSeconds()
    {
        Assert.Equal("28800s", LevelWindow.ToFluxDuration(28_800));
    }
}
