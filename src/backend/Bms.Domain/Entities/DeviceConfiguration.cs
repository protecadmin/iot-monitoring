using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class DeviceConfiguration : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public int Version { get; set; } = 1;

    public int ReportingIntervalSeconds { get; set; } = 5;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public double? TemperatureThreshold { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public Device? Device { get; set; }
}
