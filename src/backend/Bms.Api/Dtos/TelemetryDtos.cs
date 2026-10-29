namespace Bms.Api.Dtos;

/// <param name="Fields">Measured values, exactly as the device reported them.</param>
/// <param name="Derived">Values calculated from them by the Calculations module — kept in their own map so a consumer can always tell measured from calculated.</param>
public sealed record TelemetryPointDto(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Fields, IReadOnlyDictionary<string, double>? Derived = null);

public sealed record LatestTelemetryDto(Guid DeviceId, TelemetryPointDto? Point, IReadOnlyList<LatestCalculatedValueDto>? Calculated = null);

public sealed record TelemetryFieldStatsDto(double Min, double Mean, double Max);
/// <param name="Total">Set for a quantity (a Totalizer Delta: kWh in the period); a level (a formula) has Min/Mean/Max instead.</param>
public sealed record CalculatedStatsDto(double? Min, double? Mean, double? Max, double? Total);

/// <param name="Fields">Measured values' min/mean/max in the period.</param>
/// <param name="Derived">Calculated values in the period, kept apart from measured ones as in <see cref="TelemetryPointDto"/>.</param>
public sealed record TelemetryRollupPointDto(
    DateTimeOffset PeriodStart, IReadOnlyDictionary<string, TelemetryFieldStatsDto> Fields, IReadOnlyDictionary<string, CalculatedStatsDto>? Derived = null);
