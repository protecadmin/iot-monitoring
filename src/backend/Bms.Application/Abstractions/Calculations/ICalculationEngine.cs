using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Application.Abstractions.Calculations;

/// <summary>
/// Derives calculated values from telemetry as it arrives. The ingestion
/// pipeline calls this only after a reading has been validated, deduplicated
/// and persisted — never for a rejected or duplicate message — so a derived
/// value can never run ahead of the raw data it comes from.
/// Implemented in Bms.Calculations.
/// </summary>
public interface ICalculationEngine
{
    /// <param name="parameters">The device's Device Type parameters, already loaded by the pipeline.</param>
    /// <returns>The values this reading produced, already published — so alert rules can be evaluated on them too.</returns>
    Task<IReadOnlyList<CalculationUpdate>> EvaluateAsync(Device device, IReadOnlyList<DeviceTypeParameter> parameters, NormalizedTelemetry telemetry, CancellationToken ct);
}
