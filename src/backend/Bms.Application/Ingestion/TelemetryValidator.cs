using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Application.Ingestion;

public static class TelemetryValidator
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleTolerance = TimeSpan.FromDays(7);

    /// <summary>
    /// Returns null when valid, otherwise a human-readable rejection reason.
    /// Per-field range checks come from the device's own Device Type
    /// parameters (Min/Max) rather than a fixed set of known fields — a
    /// parameter with no configured range is accepted as-is.
    /// </summary>
    public static string? Validate(Device device, NormalizedTelemetry telemetry, IReadOnlyList<DeviceTypeParameter> parameters, DateTimeOffset now)
    {
        if (device.Status == DeviceStatus.Decommissioned)
            return "Device is decommissioned";

        if (telemetry.Timestamp > now.Add(FutureTolerance))
            return $"Timestamp {telemetry.Timestamp:O} is too far in the future";

        if (telemetry.Timestamp < now.Subtract(StaleTolerance))
            return $"Timestamp {telemetry.Timestamp:O} is stale (older than {StaleTolerance.TotalDays} days)";

        if (telemetry.Fields.Count == 0)
            return "No recognized telemetry fields — none of the keys sent is a parameter of the device type";

        var byKey = parameters.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in telemetry.Fields)
        {
            if (!byKey.TryGetValue(key, out var parameter))
                continue;

            if (parameter.MinValue is { } min && value < min)
                return $"{key} {value} is below the configured minimum {min}";
            if (parameter.MaxValue is { } max && value > max)
                return $"{key} {value} is above the configured maximum {max}";
        }

        return null;
    }
}
