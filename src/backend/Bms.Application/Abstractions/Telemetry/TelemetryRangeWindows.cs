namespace Bms.Application.Abstractions.Telemetry;

public sealed record TelemetryRangeWindow(TimeSpan Duration, string? AggregateEvery);

/// <summary>
/// The span and aggregation window behind each <see cref="TelemetryRange"/>.
/// Shared by raw telemetry history and derived history so both bucket
/// identically — a derived series merged into the telemetry chart has to land
/// on exactly the same timestamps as the raw one.
/// </summary>
public static class TelemetryRangeWindows
{
    public static TelemetryRangeWindow Resolve(TelemetryRange range) => range switch
    {
        TelemetryRange.Live => new(TimeSpan.FromMinutes(5), null),
        TelemetryRange.OneHour => new(TimeSpan.FromHours(1), null),
        TelemetryRange.SixHours => new(TimeSpan.FromHours(6), "1m"),
        TelemetryRange.TwentyFourHours => new(TimeSpan.FromHours(24), "5m"),
        TelemetryRange.SevenDays => new(TimeSpan.FromDays(7), "30m"),
        _ => new(TimeSpan.FromHours(1), null)
    };
}
