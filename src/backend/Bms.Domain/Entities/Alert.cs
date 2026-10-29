using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Alert : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    /// <summary>The company's own rule, or the <see cref="GlobalAlertRule"/> it came from — ids never collide, so one column serves both.</summary>
    public Guid AlertRuleId { get; set; }

    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;

    public string Parameter { get; set; } = string.Empty;
    public double? CurrentValue { get; set; }
    public double? Threshold { get; set; }
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset TriggeredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public Device? Device { get; set; }
}
