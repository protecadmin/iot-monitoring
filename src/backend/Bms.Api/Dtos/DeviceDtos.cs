using Bms.Domain;

namespace Bms.Api.Dtos;

public sealed record DeviceDto(
    Guid Id, Guid TenantId, Guid SiteId, Guid DeviceTypeId,
    string DeviceCode, string Name, string? SerialNumber, string? Manufacturer, string? Model,
    string Transport, string Protocol, string PayloadFormat,
    string? FirmwareVersion, string? HardwareVersion, string Status,
    double? Latitude, double? Longitude,
    DateTimeOffset? LastSeenAt, DateTimeOffset? LastTelemetryAt, DateTimeOffset? LastHeartbeatAt,
    Guid? GatewayId, string? GatewayName, DateTimeOffset CreatedAt,
    DigitalTwinSettingsDto? DigitalTwin = null,
    bool AlertsEnabled = true,
    string TimeSource = "Device",
    string Connection = "MqttDirect",
    Guid? ConnectorId = null,
    string? SensorId = null,
    DateOnly? InstalledOn = null,
    string? MountingLocation = null,
    IReadOnlyList<DeviceValueConversionDto>? ValueConversions = null,
    string? MqttTelemetryTopic = null,
    string? EffectiveMqttTelemetryTopic = null);

/// <summary>value = (raw - Offset) * Scale, applied to an MQTT-direct device's parameter before it is stored.</summary>
public sealed record DeviceValueConversionDto(string ParameterKey, double Offset, double Scale);

/// <summary>Replaces the device's conversions; an empty list removes them all.</summary>
public sealed record SetDeviceValueConversionsRequest(IReadOnlyList<DeviceValueConversionDto> Conversions);

/// <summary>Off mutes the device's alerts (e.g. maintenance); on resumes them.</summary>
public sealed record SetDeviceAlertsRequest(bool Enabled);

/// <summary>Null fields mean "use the default for this device type".</summary>
public sealed record DigitalTwinSettingsDto(string? Template, string? MountPoint, string? AssetName);

public sealed record CreateDeviceRequest(
    Guid SiteId, Guid DeviceTypeId,
    string DeviceCode, string Name, string? SerialNumber, string? Manufacturer, string? Model,
    double? Latitude, double? Longitude, Guid? GatewayId,
    DeviceConnection Connection = DeviceConnection.MqttDirect, Guid? ConnectorId = null, string? SensorId = null,
    DateOnly? InstalledOn = null, string? MountingLocation = null, string? MqttTelemetryTopic = null);

public sealed record UpdateDeviceRequest(
    string Name, string? SerialNumber, string? Manufacturer, string? Model,
    double? Latitude, double? Longitude, Guid? GatewayId,
    DateOnly? InstalledOn = null, string? MountingLocation = null, string? MqttTelemetryTopic = null);

/// <summary>When devices go Online/Offline — shown on the heartbeat guide.</summary>
public sealed record DeviceLifecycleSettingsDto(
    int OfflineAfterSeconds, int SweepIntervalSeconds, int NoTelemetryAlertAfterSeconds, int DeviceOfflineAlertAfterSeconds);
