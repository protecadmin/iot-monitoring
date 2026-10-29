using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Application.Abstractions.Alerting;

/// <summary>Implemented by Bms.Alerting; invoked by the ingestion pipeline after a telemetry point is persisted.</summary>
public interface IAlertEvaluator
{
    /// <summary>
    /// The alerts this reading changed: ones it opened (Status Open), and ones
    /// it closed because the reading is back within their rule (Status Resolved).
    /// </summary>
    Task<IReadOnlyList<Alert>> EvaluateAsync(Device device, NormalizedTelemetry telemetry, CancellationToken ct);
}
