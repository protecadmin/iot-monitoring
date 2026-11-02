using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>Explicit device-type capability; null means ordinary device behaviour.</summary>
public sealed class TiltAxes
{
    public string X { get; set; } = string.Empty;
    public string Y { get; set; } = string.Empty;
    public string? Z { get; set; }
}

public sealed class TiltMonitoringSettings : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public TimeOnly ClosingTime { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public DateTimeOffset OnboardedAt { get; set; }
    public DateTimeOffset? BaselineAt { get; set; }
    public double? BaselineX { get; set; }
    public double? BaselineY { get; set; }
    public double? BaselineZ { get; set; }
}

public sealed class TiltDailyReading : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset CutoffAt { get; set; }
    public DateTimeOffset? ReadingAt { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
}
