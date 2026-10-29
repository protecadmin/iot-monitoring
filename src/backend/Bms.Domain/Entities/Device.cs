using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Device : TenantScopedEntity
{
    public Guid SiteId { get; set; }
    public Guid DeviceTypeId { get; set; }
    public Guid? GatewayId { get; set; }

    public string DeviceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }

    public TransportType Transport { get; set; }
    public ProtocolType Protocol { get; set; }
    public PayloadFormat PayloadFormat { get; set; }

    public string? FirmwareVersion { get; set; }
    public string? HardwareVersion { get; set; }

    public DeviceStatus Status { get; set; } = DeviceStatus.Registered;

    /// <summary>
    /// Off mutes the device: no new alerts from any rule, offline and no
    /// telemetry included — e.g. during maintenance. Its readings are still
    /// stored, and alerts already open stay open.
    /// </summary>
    public bool AlertsEnabled { get; set; } = true;

    public DeviceConnection Connection { get; set; } = DeviceConnection.MqttDirect;

    /// <summary>For <see cref="DeviceConnection.Connector"/>: which connector it sends to, and the ID it puts in its messages.</summary>
    public Guid? ConnectorId { get; set; }
    public string? SensorId { get; set; }

    public DateOnly? InstalledOn { get; set; }
    /// <summary>Where on the site, in words — "B2, grid C4, east face".</summary>
    public string? MountingLocation { get; set; }
    /// <summary>Unit corrections applied before storing, for an MQTT-direct device that sends a wrong unit.</summary>
    public List<DeviceValueConversion> ValueConversions { get; set; } = [];

    /// <summary>Whether its readings carry the device's own time or their arrival time; updated with each reading.</summary>
    public TimeSource TimeSource { get; set; } = TimeSource.Device;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// How the dashboard draws this device's digital twin: which picture
    /// (template id, e.g. "pump"), where on it the sensor sits (mount point
    /// id, e.g. "pump-bearing"), and an optional equipment name. The ids are
    /// defined by the dashboard's twin templates; nulls mean "the default for
    /// this device type". Display only — nothing in ingestion reads them.
    /// </summary>
    public string? TwinTemplate { get; set; }
    public string? TwinMountPoint { get; set; }
    public string? TwinAssetName { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastTelemetryAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    public Site? Site { get; set; }
    public DeviceType? DeviceType { get; set; }
    public Gateway? Gateway { get; set; }
    public DeviceConfiguration? Configuration { get; set; }

    public ICollection<DeviceCommand> Commands { get; set; } = new List<DeviceCommand>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    public ICollection<FirmwareDeployment> FirmwareDeployments { get; set; } = new List<FirmwareDeployment>();
}
