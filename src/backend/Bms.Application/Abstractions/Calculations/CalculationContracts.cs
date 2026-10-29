using Bms.Application.Abstractions.Telemetry;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Calculations;

/// <summary>Time bucket a calculation's results are summed into.</summary>
public enum CalculationGranularity
{
    /// <summary>One value per accepted reading — no aggregation.</summary>
    Reading,
    FiveMinutes,
    Hourly,
    Daily,
    Monthly
}

public sealed record CalculatedPoint(DateTimeOffset Timestamp, double Value);

/// <summary>
/// One report period of a derived value. A level (a formula — kW, %) has
/// Min/Mean/Max; a quantity (a Totalizer Delta — kWh) has a Total, since
/// the min or mean of individual reading-to-reading deltas means nothing.
/// </summary>
public sealed record CalculatedRollupPoint(DateTimeOffset PeriodStart, double? Min, double? Mean, double? Max, double? Total);

/// <summary>A derived value produced by a live reading — what is pushed over the realtime channel.</summary>
public sealed record CalculationUpdate(
    Guid TenantId,
    Guid SiteId,
    Guid DeviceId,
    Guid RuleId,
    string RuleKey,
    string OutputKey,
    string OutputName,
    string? OutputUnit,
    double Value,
    DateTimeOffset Timestamp);

public sealed record LatestCalculatedValue(Guid RuleId, string OutputKey, string OutputName, string? OutputUnit, double Value, DateTimeOffset Timestamp);

public sealed record CalculationResults(IReadOnlyList<CalculatedPoint> Points, double Total, LatestCalculatedValue? Latest);

/// <summary>
/// Read side of the Calculations module, for the API. Every method takes a
/// device the caller has already authorized — this does no tenant checks of
/// its own beyond scoping rules to that device's tenant.
/// </summary>
public interface ICalculationReader
{
    /// <summary>Enabled rules whose scope covers this device and whose source parameter is eligible for their calculation type.</summary>
    Task<IReadOnlyList<CalculationRule>> GetEffectiveRulesAsync(Device device, CancellationToken ct);

    /// <summary>Whether the rule's scope covers the device and its source is eligible — regardless of IsEnabled, so a disabled rule's history can still be viewed.</summary>
    Task<bool> AppliesToAsync(CalculationRule rule, Device device, CancellationToken ct);

    /// <summary>
    /// Derived series keyed by output key, over the same range and windows the
    /// raw telemetry history uses, so they line up point-for-point with it.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>>> GetHistoryAsync(Device device, TelemetryRange range, CancellationToken ct);
    Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>>> GetHistoryAsync(Device device, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Latest live value per enabled rule, for each device.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<LatestCalculatedValue>>> GetLatestAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    /// <summary>
    /// Report periods per enabled rule's output key, windows labelled by their
    /// start and aligned like the raw telemetry rollup (Flux aggregateWindow).
    /// </summary>
    /// <param name="every">Flux duration of one period ("1d", "1w").</param>
    Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedRollupPoint>>> GetRollupAsync(Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct);

    Task<CalculationResults> GetResultsAsync(CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, CalculationGranularity granularity, CancellationToken ct);
}
