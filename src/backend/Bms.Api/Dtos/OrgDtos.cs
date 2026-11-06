namespace Bms.Api.Dtos;

public sealed record TenantDto(Guid Id, string Name, string Slug, bool IsActive, DateTimeOffset CreatedAt, bool DigitalTwinEnabled = false);
public sealed record CreateTenantRequest(string Name, string Slug);
public sealed record UpdateTenantRequest(string Name, bool IsActive, bool DigitalTwinEnabled = false);

public sealed record SiteDto(Guid Id, Guid TenantId, string Code, string Name, string? Address, double? Latitude, double? Longitude);
public sealed record CreateSiteRequest(string Code, string Name, string? Address, double? Latitude, double? Longitude);
public sealed record UpdateSiteRequest(string Name, string? Address, double? Latitude, double? Longitude);

public sealed record GatewayDto(Guid Id, Guid? TenantId, Guid? SiteId, string SiteName, string Name, string? IpAddress, DateTimeOffset? LastSeenAt,
    string GatewayCode = "", string Status = "Standalone", DateTimeOffset? LastMeasurementAt = null, string? FirmwareVersion = null,
    int? Rssi = null, double? BatteryPct = null, IReadOnlyList<string>? DeviceNames = null, string? TenantName = null);
public sealed record CreateGatewayRequest(Guid? SiteId, string? Name, string? IpAddress, string? GatewayCode = null,
    string? FirmwareVersion = null, int? Rssi = null, double? BatteryPct = null);
public sealed record UpdateGatewayRequest(string Name, string? IpAddress, string? FirmwareVersion = null, int? Rssi = null, double? BatteryPct = null);
public sealed record GatewayAvailableDto(Guid Id, string GatewayCode, string Name, string Status, Guid? TenantId, Guid? SiteId);

/// <summary>How a Derived parameter is calculated, for every tenant. Type is the API name: FORMULA or TOTALIZER_DELTA.</summary>
/// <param name="WindowSeconds">The averaging period, for a windowed type. Null takes the default (see Bms.Domain.Calculations.LevelWindow).</param>
public sealed record ParameterCalculationDto(string Type, string? Expression, string? SourceParameterKey, double? MaxDelta, int? WindowSeconds = null);

/// <param name="Warnings">Only on a save: tenants whose rule couldn't be brought in line (e.g. another rule already outputs this key).</param>
public sealed record DeviceTypeParameterDto(
    Guid Id, string Key, string Label, string? Unit, double? MinValue, double? MaxValue, string Kind,
    ParameterCalculationDto? Calculation = null, IReadOnlyList<string>? Warnings = null, string? DashboardWidget = null);

/// <param name="Calculation">For a Derived parameter: how it is calculated. Null leaves it without one.</param>
/// <param name="DashboardWidget">How the dashboard shows it, for every company; null keeps it off the dashboard.</param>
public sealed record CreateDeviceTypeParameterRequest(
    string Key, string Label, string? Unit, double? MinValue, double? MaxValue, Bms.Domain.ParameterKind? Kind = null,
    ParameterCalculationDto? Calculation = null, Bms.Domain.DashboardWidget? DashboardWidget = null);
public sealed record UpdateDeviceTypeParameterRequest(
    string Label, string? Unit, double? MinValue, double? MaxValue, Bms.Domain.ParameterKind? Kind = null,
    ParameterCalculationDto? Calculation = null, Bms.Domain.DashboardWidget? DashboardWidget = null);

/// <param name="ExpectedIntervalSeconds">How often its devices report, when slower than the platform's offline window; null for the default.</param>
/// <param name="SupportsCommands">False for devices with no downlink — the command API refuses them.</param>
public sealed record DeviceTypeDto(
    Guid Id, string Name, string? Description, IReadOnlyList<DeviceTypeParameterDto> Parameters,
    int? ExpectedIntervalSeconds = null, bool SupportsCommands = true, Bms.Domain.Entities.TiltAxes? TiltAxes = null);
public sealed record CreateDeviceTypeRequest(string Name, string? Description, int? ExpectedIntervalSeconds = null, bool SupportsCommands = true, Bms.Domain.Entities.TiltAxes? TiltAxes = null);
public sealed record UpdateDeviceTypeRequest(string Name, string? Description, int? ExpectedIntervalSeconds = null, bool SupportsCommands = true, Bms.Domain.Entities.TiltAxes? TiltAxes = null);
