using Bms.Domain.Entities;

namespace Bms.Domain.Alerting;

/// <summary>Where an effective alert-rule value comes from.</summary>
public enum AlertRuleLevel
{
    Global,
    Company,
    Site,
    Device
}

/// <summary>
/// A <see cref="GlobalAlertRule"/> as it applies at one company / site /
/// device, with the level each value was set at.
/// </summary>
public sealed record EffectiveAlertRule(
    GlobalAlertRule Rule,
    bool IsEnabled, AlertRuleLevel EnabledFrom,
    double? Threshold, AlertRuleLevel ThresholdFrom,
    AlertSeverity Severity, AlertRuleLevel SeverityFrom,
    int CooldownSeconds, AlertRuleLevel CooldownFrom);

/// <summary>
/// Resolves a global rule through a company's overrides: the device's change
/// wins over its site's, the site's over the company's, the company's over the
/// global rule. Each value resolves on its own, so a site can change the
/// threshold while the severity still follows the company.
/// </summary>
public static class AlertRuleResolution
{
    /// <param name="overrides">This company's overrides of <paramref name="rule"/>; any others are ignored.</param>
    /// <param name="siteId">Resolve at a site (or a device at it); null for the company level.</param>
    /// <param name="deviceId">Resolve for one device; null for the company or site level.</param>
    public static EffectiveAlertRule Resolve(
        GlobalAlertRule rule, IEnumerable<AlertRuleOverride> overrides, Guid tenantId, Guid? siteId, Guid? deviceId)
    {
        // Least to most specific: later entries win.
        var chain = new List<(AlertRuleOverride Override, AlertRuleLevel Level)>();
        foreach (var o in overrides.Where(o => o.GlobalAlertRuleId == rule.Id && o.TenantId == tenantId))
        {
            if (o.DeviceId is null && o.SiteId is null)
                chain.Add((o, AlertRuleLevel.Company));
            else if (o.DeviceId is null && siteId is not null && o.SiteId == siteId)
                chain.Add((o, AlertRuleLevel.Site));
            else if (deviceId is not null && o.DeviceId == deviceId)
                chain.Add((o, AlertRuleLevel.Device));
        }
        chain.Sort((a, b) => a.Level.CompareTo(b.Level));

        (T Value, AlertRuleLevel From) Pick<T>(T globalValue, Func<AlertRuleOverride, T?> read) where T : struct
        {
            var result = (Value: globalValue, From: AlertRuleLevel.Global);
            foreach (var (o, level) in chain)
                if (read(o) is { } v)
                    result = (v, level);
            return result;
        }

        var enabled = Pick(rule.AppliesByDefault, o => o.IsEnabled);
        var threshold = PickThreshold(chain, rule.Threshold);
        var severity = Pick(rule.Severity, o => o.Severity);
        var cooldown = Pick(rule.CooldownSeconds, o => o.CooldownSeconds);

        return new EffectiveAlertRule(
            rule,
            rule.IsEnabled && enabled.Value, rule.IsEnabled ? enabled.From : AlertRuleLevel.Global,
            threshold.Value, threshold.From,
            severity.Value, severity.From,
            cooldown.Value, cooldown.From);
    }

    // Nullable, unlike the other values: a rule without a global threshold can still take one from an override.
    private static (double? Value, AlertRuleLevel From) PickThreshold(
        List<(AlertRuleOverride Override, AlertRuleLevel Level)> chain, double? globalValue)
    {
        var result = (Value: globalValue, From: AlertRuleLevel.Global);
        foreach (var (o, level) in chain)
            if (o.Threshold is { } v)
                result = (v, level);
        return result;
    }

    /// <summary>The rule as the alert evaluators consume it — same shape as a company's own rule.</summary>
    public static AlertRule ToAlertRule(EffectiveAlertRule effective, Guid tenantId) => new()
    {
        Id = effective.Rule.Id,
        TenantId = tenantId,
        DeviceTypeId = effective.Rule.DeviceTypeId,
        Name = effective.Rule.Name,
        Metric = effective.Rule.Metric,
        Operator = effective.Rule.Operator,
        Threshold = effective.Threshold,
        Severity = effective.Severity,
        CooldownSeconds = effective.CooldownSeconds,
        IsEnabled = effective.IsEnabled,
        CreatedAt = effective.Rule.CreatedAt,
        UpdatedAt = effective.Rule.UpdatedAt
    };
}
