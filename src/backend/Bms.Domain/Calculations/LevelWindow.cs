namespace Bms.Domain.Calculations;

/// <summary>How a level is averaged over a window. Declared per calculation type, never inferred from the unit.</summary>
public enum LevelAggregation
{
    /// <summary>The arithmetic mean of the readings — a WBGT, dust or temperature average.</summary>
    Arithmetic,

    /// <summary>
    /// The mean sound energy expressed back in decibels (Leq):
    /// 10·log₁₀(mean(10^(L/10))). Decibels are logarithmic, so averaging them
    /// arithmetically understates loud periods — 80 dB for half a window and
    /// 100 dB for the other half is 97 dB, not 90.
    /// </summary>
    EnergyDecibel
}

/// <summary>
/// Where a windowed calculation's periods begin and end.
///
/// Windows are aligned to the Unix epoch, which is how Flux aggregateWindow
/// aligns them. That alignment is the whole reason a live value and the same
/// period read back from history describe the same span of time: both are
/// produced by InfluxTelemetryStore.GetWindowedLevelSeriesAsync over
/// epoch-aligned windows, so there is no second implementation to drift from
/// the first.
/// </summary>
public static class LevelWindow
{
    /// <summary>Averaging periods shorter than this would publish faster than devices report.</summary>
    public const int MinSeconds = 10;

    /// <summary>A day. Longer compliance periods (a week, a month) are a report over windows, not one window.</summary>
    public const int MaxSeconds = 86_400;

    /// <summary>Five minutes — the shortest period NEA boundary noise is assessed over.</summary>
    public const int DefaultSeconds = 300;

    /// <summary>The start of the window <paramref name="instant"/> falls in.</summary>
    public static DateTimeOffset StartOf(DateTimeOffset instant, int windowSeconds)
    {
        var seconds = instant.ToUnixTimeSeconds();
        var start = seconds - Modulo(seconds, windowSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(start);
    }

    /// <summary>
    /// The end of the window <paramref name="instant"/> falls in, or the
    /// instant itself when it already sits on a boundary. Used to widen a
    /// query to whole windows: Flux clips its last window to the range stop
    /// and labels it with that clipped time, which would otherwise present a
    /// window still being filled as a finished one.
    /// </summary>
    public static DateTimeOffset EndOf(DateTimeOffset instant, int windowSeconds)
    {
        var start = StartOf(instant, windowSeconds);
        return start == instant ? instant : start.AddSeconds(windowSeconds);
    }

    /// <summary>Whether <paramref name="later"/> falls in a window after the one holding <paramref name="earlier"/> — i.e. that window is now complete.</summary>
    public static bool CrossesWindow(DateTimeOffset earlier, DateTimeOffset later, int windowSeconds) =>
        StartOf(later, windowSeconds) > StartOf(earlier, windowSeconds);

    /// <summary>The window as a Flux duration ("300s"). Seconds throughout, so no rounding can disagree with StartOf.</summary>
    public static string ToFluxDuration(int windowSeconds) => $"{windowSeconds}s";

    /// <summary>
    /// A Flux duration ("5m", "1h", "1d") in seconds, for comparing a requested
    /// granularity against a window. Months and weeks are taken as 30 and 7
    /// days — close enough to tell which of two periods is the longer, which is
    /// all this is used for.
    /// </summary>
    public static bool TryParseFluxSeconds(string? duration, out int seconds)
    {
        seconds = 0;
        var text = duration?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        var (suffix, unit) = text switch
        {
            _ when text.EndsWith("mo", StringComparison.Ordinal) => (2, 30 * 86_400),
            _ when text.EndsWith("ms", StringComparison.Ordinal) => (2, 0),
            _ when text.EndsWith("w", StringComparison.Ordinal) => (1, 7 * 86_400),
            _ when text.EndsWith("d", StringComparison.Ordinal) => (1, 86_400),
            _ when text.EndsWith("h", StringComparison.Ordinal) => (1, 3_600),
            _ when text.EndsWith("m", StringComparison.Ordinal) => (1, 60),
            _ when text.EndsWith("s", StringComparison.Ordinal) => (1, 1),
            _ => (0, 0)
        };
        if (unit == 0
            || !int.TryParse(text[..^suffix], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
            || count <= 0)
            return false;

        try
        {
            seconds = checked(count * unit);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // Timestamps before 1970 would make the C# % operator step the window
    // forward instead of back, putting the start after the instant itself.
    private static long Modulo(long value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
