using System.Text.RegularExpressions;
using Bms.Domain;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Notifications;

/// <summary>
/// The contract an external maintenance system receives when BMS raises an
/// alert.
///
/// One alert, at the moment it fires. Not a telemetry feed: the value and
/// threshold that triggered it are here because maintenance needs the reason,
/// but no series, no history and no subsequent readings ever travel this way.
///
/// It lives in the application layer rather than beside the HTTP client
/// because the shape is the integration contract; posting it is a delivery
/// detail.
/// </summary>
public sealed record BmsAlertEvent(
    string SourceSystem,
    string SourceEventId,
    string EventType,
    string BmsTenantId,
    string BmsSiteId,
    string BmsDeviceId,
    string BmsDeviceCode,
    string Severity,
    string Status,
    string Parameter,
    double? Value,
    double? Threshold,
    string Message,
    DateTimeOffset TriggeredAt)
{
    public const string System = "BMS";

    public static BmsAlertEvent From(Alert alert, Device device, AlertRule? rule) => new(
        SourceSystem: System,
        SourceEventId: alert.Id.ToString(),
        EventType: DeriveEventType(alert, rule),
        BmsTenantId: alert.TenantId.ToString(),
        BmsSiteId: device.SiteId.ToString(),
        BmsDeviceId: device.Id.ToString(),
        BmsDeviceCode: device.DeviceCode,
        Severity: alert.Severity.ToString(),
        Status: alert.Status.ToString(),
        Parameter: alert.Parameter,
        Value: alert.CurrentValue,
        Threshold: alert.Threshold,
        Message: alert.Message,
        TriggeredAt: alert.TriggeredAt);

    /// <summary>
    /// Turns a rule breach into a stable event name the receiving system can
    /// write policy against: temperature over a ceiling becomes
    /// HIGH_TEMPERATURE, pressure under a floor becomes LOW_PRESSURE.
    ///
    /// The rule's operator decides the direction, not a comparison of value to
    /// threshold, so a breach exactly at the boundary of a "&lt;=" rule is
    /// still LOW. If the rule has since been deleted, the recorded value and
    /// threshold are the fallback.
    /// </summary>
    public static string DeriveEventType(Alert alert, AlertRule? rule)
    {
        var parameter = NonAlphanumeric().Replace(alert.Parameter ?? string.Empty, "_").Trim('_').ToUpperInvariant();
        if (string.IsNullOrEmpty(parameter))
            parameter = "UNKNOWN";

        var direction = rule?.Operator switch
        {
            AlertRuleOperator.GreaterThan or AlertRuleOperator.GreaterThanOrEqual => "HIGH",
            AlertRuleOperator.LessThan or AlertRuleOperator.LessThanOrEqual => "LOW",
            AlertRuleOperator.Equal => "EXACT",
            _ => alert.CurrentValue is { } v && alert.Threshold is { } t
                ? (v >= t ? "HIGH" : "LOW")
                : "ALERT"
        };

        return $"{direction}_{parameter}";
    }

    private static readonly Regex NonAlphanumericRegex = new("[^A-Za-z0-9]+", RegexOptions.Compiled);
    private static Regex NonAlphanumeric() => NonAlphanumericRegex;
}
