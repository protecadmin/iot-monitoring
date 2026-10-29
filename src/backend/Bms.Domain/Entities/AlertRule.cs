using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class AlertRule : TenantScopedEntity
{
    public Guid? SiteId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? DeviceTypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A Device Type parameter key (e.g. "current", "co2") or one of
    /// <see cref="LifecycleMetrics"/> ("DeviceOffline"/"NoTelemetry").
    /// Free-text rather than an enum so new parameters never require a
    /// code change to become alertable.
    /// </summary>
    public string Metric { get; set; } = string.Empty;
    public AlertRuleOperator Operator { get; set; }
    public double? Threshold { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    /// <summary>Minimum time between re-triggers for the same device+rule.</summary>
    public int CooldownSeconds { get; set; } = 300;

    public bool IsEnabled { get; set; } = true;
}
