using Bms.Application.Abstractions.Calculations;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Calculations;

/// <summary>
/// One calculation type. The engine and reader are type-agnostic and dispatch
/// to a handler by <see cref="CalculationRule.Type"/>, so supporting a new
/// calculation — Sum, Ratio, Efficiency — means writing a handler and
/// registering it, with no change to ingestion, the engine or the API.
/// </summary>
public interface ICalculationHandler
{
    CalculationType Type { get; }

    /// <summary>Whether a parameter can be this calculation's source. Decided by its declared Kind, never its unit.</summary>
    bool IsEligibleSource(DeviceTypeParameter parameter);

    /// <summary>What an eligible source looks like, for rule validation messages.</summary>
    string SourceRequirement { get; }

    /// <summary>
    /// The parameter keys a rule of this type reads from each reading — its
    /// one source parameter, or every parameter a formula names. Empty when
    /// the rule is malformed, which makes it apply to no device.
    /// </summary>
    IReadOnlyList<string> InputKeys(CalculationRule rule);

    /// <summary>Processes one live reading. Returns the value to publish, or null when this reading produces none.</summary>
    Task<CalculationUpdate?> EvaluateAsync(CalculationRule rule, Device device, NormalizedTelemetry telemetry, CancellationToken ct);

    /// <summary>Report periods (min/mean/max for a level, total for a quantity) over a time range, computed from raw history.</summary>
    Task<IReadOnlyList<CalculatedRollupPoint>> RollupAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct);

    /// <summary>Derived points over a time range, computed from raw history.</summary>
    Task<IReadOnlyList<CalculatedPoint>> QueryAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to,
        string? aggregateEvery, bool timestampAtWindowStart, CancellationToken ct);
}
