using Bms.Domain.Telemetry;

namespace Bms.Application.Abstractions.Telemetry;

public sealed record TelemetryPoint(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Fields);

public sealed record TelemetryFieldStats(double Min, double Mean, double Max);
public sealed record TelemetryRollupPoint(DateTimeOffset PeriodStart, IReadOnlyDictionary<string, TelemetryFieldStats> Fields);

public enum RollupGranularity
{
    Daily,
    Weekly
}

public enum TelemetryRange
{
    Live,
    OneHour,
    SixHours,
    TwentyFourHours,
    SevenDays
}

/// <summary>
/// Abstraction over InfluxDB — the only place the rest of the system talks
/// to time-series storage. See docs/database/influxdb.md.
/// </summary>
public interface ITelemetryStore
{
    Task WriteAsync(NormalizedTelemetry telemetry, CancellationToken ct);
    Task<TelemetryPoint?> GetLatestAsync(Guid deviceId, CancellationToken ct);
    Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(Guid deviceId, TelemetryRange range, CancellationToken ct);
    Task<IReadOnlyList<TelemetryPoint>> GetHistoryAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Per-period min/mean/max per field — daily rollup covers the last 30 days, weekly the last 12 weeks.</summary>
    Task<IReadOnlyList<TelemetryRollupPoint>> GetRollupAsync(Guid deviceId, RollupGranularity granularity, CancellationToken ct);
}
