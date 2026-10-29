using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// An alert rule defined once for every company — "High Voltage: voltage > 250
/// on Energy Meters". Devices are matched by type, so a new device or a new
/// company is covered without creating anything. A company, site or single
/// device changes only what differs from it through an
/// <see cref="AlertRuleOverride"/>; see Bms.Domain.Alerting.AlertRuleResolution.
/// </summary>
public class GlobalAlertRule : Entity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Which devices it applies to. Null for every device type — the lifecycle conditions (Device Offline, No Telemetry).</summary>
    public Guid? DeviceTypeId { get; set; }

    /// <summary>A parameter key of the device type, or a <see cref="LifecycleMetrics"/> condition.</summary>
    public string Metric { get; set; } = string.Empty;
    public AlertRuleOperator Operator { get; set; }
    public double? Threshold { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>Off switches the rule off everywhere, whatever any override says.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// On: every company has the rule unless it switches it off. Off: only
    /// where a company, site or device switches it on — a rule for selected
    /// companies or sites.
    /// </summary>
    public bool AppliesByDefault { get; set; } = true;
}

/// <summary>
/// One company's change to a <see cref="GlobalAlertRule"/> — for the whole
/// company, one site (SiteId), or one device (DeviceId). Null fields inherit
/// from the level above, so only what actually differs is stored and a later
/// change to the global rule still reaches everything that wasn't changed.
/// </summary>
public class AlertRuleOverride : TenantScopedEntity
{
    public Guid GlobalAlertRuleId { get; set; }

    /// <summary>Set for a site's change; null for the whole company.</summary>
    public Guid? SiteId { get; set; }

    /// <summary>Set for one device's change; it wins over its site and company.</summary>
    public Guid? DeviceId { get; set; }

    public bool? IsEnabled { get; set; }
    public double? Threshold { get; set; }
    public AlertSeverity? Severity { get; set; }
    public int? CooldownSeconds { get; set; }

    public GlobalAlertRule? GlobalAlertRule { get; set; }
}
