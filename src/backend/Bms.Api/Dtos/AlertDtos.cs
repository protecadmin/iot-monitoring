namespace Bms.Api.Dtos;

public sealed record AlertDto(
    Guid Id, Guid TenantId, Guid DeviceId, Guid AlertRuleId,
    string Severity, string Status, string Parameter,
    double? CurrentValue, double? Threshold, string Message,
    DateTimeOffset TriggeredAt, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt);

public sealed record CreateAlertRuleRequest(
    Guid? SiteId, Guid? DeviceId, Guid? DeviceTypeId,
    string Name, string Metric, Bms.Domain.AlertRuleOperator Operator,
    double? Threshold, Bms.Domain.AlertSeverity Severity, int CooldownSeconds);

public sealed record UpdateAlertRuleRequest(
    string Name, string Metric, Bms.Domain.AlertRuleOperator Operator,
    double? Threshold, Bms.Domain.AlertSeverity Severity, int CooldownSeconds, bool IsEnabled);

/// <param name="DeviceTypeId">Null: every device type — the lifecycle conditions.</param>
/// <param name="AppliesByDefault">On for every company unless switched off; off: only where switched on.</param>
/// <param name="OverrideCount">Companies, sites and devices that changed this rule.</param>
public sealed record GlobalAlertRuleDto(
    Guid Id, string Name, Guid? DeviceTypeId, string Metric, string Operator, double? Threshold,
    string Severity, int CooldownSeconds, bool IsEnabled, bool AppliesByDefault, int OverrideCount);

public sealed record SaveGlobalAlertRuleRequest(
    string Name, Guid? DeviceTypeId, string Metric, Bms.Domain.AlertRuleOperator Operator, double? Threshold,
    Bms.Domain.AlertSeverity Severity, int CooldownSeconds, bool IsEnabled = true, bool AppliesByDefault = true);

/// <summary>
/// A global rule as it applies at the selected level (company, site or
/// device). Each *From says where that value was set — Global, Company, Site
/// or Device — and Here is the change stored at exactly this level, if any.
/// </summary>
public sealed record EffectiveAlertRuleDto(
    Guid RuleId, string Name, Guid? DeviceTypeId, string Metric, string Operator,
    bool IsEnabled, string EnabledFrom,
    double? Threshold, string ThresholdFrom,
    string Severity, string SeverityFrom,
    int CooldownSeconds, string CooldownFrom,
    AlertRuleOverrideDto? Here);

public sealed record AlertRuleOverrideDto(Guid? SiteId, Guid? DeviceId, bool? IsEnabled, double? Threshold, string? Severity, int? CooldownSeconds);

/// <summary>The change at one level. Null fields inherit from the level above; all null removes the change.</summary>
public sealed record SaveAlertRuleOverrideRequest(
    Guid? SiteId, Guid? DeviceId, bool? IsEnabled, double? Threshold, Bms.Domain.AlertSeverity? Severity, int? CooldownSeconds);
