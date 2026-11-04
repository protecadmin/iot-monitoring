using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Gateway : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? SiteId { get; set; }
    public string GatewayCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GatewayStatus Status { get; set; } = GatewayStatus.Standalone;
    public string? IpAddress { get; set; }
    public string? FirmwareVersion { get; set; }
    public int? Rssi { get; set; }
    public double? BatteryPct { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastMeasurementAt { get; set; }

    public Site? Site { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}

public enum GatewayStatus
{
    Standalone,
    Online,
    Offline,
    Maintenance
}
