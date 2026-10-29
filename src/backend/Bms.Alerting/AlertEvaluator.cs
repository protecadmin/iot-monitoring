using Bms.Application.Abstractions.Alerting;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging;

namespace Bms.Alerting;

/// <summary>
/// Evaluates a device's enabled alert rules against one telemetry point.
/// Implements cooldown (don't re-fire the same rule+device within its
/// cooldown window) and duplicate prevention (don't open a second alert
/// while one is already OPEN for the same rule+device) — see
/// core.txt section 26 / docs. A reading back within a rule closes that
/// rule's open alert, so the open list is only what is wrong right now.
/// </summary>
public sealed class AlertEvaluator : IAlertEvaluator
{
    private readonly IAlertRuleRepository _rules;
    private readonly IAlertRepository _alerts;
    private readonly TimeProvider _clock;
    private readonly ILogger<AlertEvaluator> _logger;

    public AlertEvaluator(IAlertRuleRepository rules, IAlertRepository alerts, TimeProvider clock, ILogger<AlertEvaluator> logger)
    {
        _rules = rules;
        _alerts = alerts;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Alert>> EvaluateAsync(Device device, NormalizedTelemetry telemetry, CancellationToken ct)
    {
        if (!device.AlertsEnabled)
            return []; // muted, e.g. under maintenance

        var applicableRules = await _rules.GetApplicableRulesAsync(device.TenantId, device.SiteId, device.Id, device.DeviceTypeId, ct);
        var unresolved = await _alerts.ListUnresolvedAsync(device.Id, ct) ?? [];
        var changed = new List<Alert>();

        foreach (var rule in applicableRules.Where(r => r.IsEnabled))
        {
            var value = ReadMetric(telemetry, rule.Metric);
            if (value is null)
                continue; // this telemetry point doesn't carry the rule's metric

            if (!IsBreached(value.Value, rule.Operator, rule.Threshold))
            {
                // Back within the rule: its open alert has recovered.
                foreach (var open in unresolved.Where(a => a.AlertRuleId == rule.Id))
                {
                    open.Status = AlertStatus.Resolved;
                    open.ResolvedAt = _clock.GetUtcNow();
                    open.CurrentValue = value;
                    await _alerts.UpdateAsync(open, ct);
                    changed.Add(open);
                }
                continue;
            }

            var lastTriggered = await _alerts.GetLastTriggeredAsync(device.Id, rule.Id, ct);
            var now = _clock.GetUtcNow();

            if (lastTriggered is not null)
            {
                var withinCooldown = now - lastTriggered.TriggeredAt < TimeSpan.FromSeconds(rule.CooldownSeconds);
                var stillOpen = lastTriggered.Status != AlertStatus.Resolved;
                if (withinCooldown || stillOpen)
                {
                    _logger.LogDebug("Suppressing alert for rule {RuleId} device {DeviceId}: cooldown={Cooldown} stillOpen={StillOpen}",
                        rule.Id, device.Id, withinCooldown, stillOpen);
                    continue;
                }
            }

            var alert = new Alert
            {
                TenantId = device.TenantId,
                DeviceId = device.Id,
                AlertRuleId = rule.Id,
                Severity = rule.Severity,
                Status = AlertStatus.Open,
                Parameter = rule.Metric.ToString(),
                CurrentValue = value,
                Threshold = rule.Threshold,
                Message = $"{rule.Name}: {rule.Metric} {DescribeOperator(rule.Operator)} {rule.Threshold} (current: {value:0.##})",
                TriggeredAt = now
            };

            await _alerts.AddAsync(alert, ct);
            changed.Add(alert);
        }

        return changed;
    }

    // DeviceOffline / NoTelemetry are evaluated by Bms.DeviceManagement's
    // heartbeat sweep, not per-telemetry-point, so they never match here —
    // any other metric name is looked up directly against the telemetry
    // point's dynamic field bag (built with a case-insensitive comparer in
    // TelemetryNormalizer, since the keys a device sends and its Device Type
    // parameter keys aren't guaranteed to agree on case).
    private static double? ReadMetric(NormalizedTelemetry telemetry, string metric) =>
        telemetry.Fields.TryGetValue(metric, out var value) ? value : null;

    private static bool IsBreached(double value, AlertRuleOperator op, double? threshold)
    {
        if (threshold is not { } t)
            return false;

        return op switch
        {
            AlertRuleOperator.GreaterThan => value > t,
            AlertRuleOperator.LessThan => value < t,
            AlertRuleOperator.GreaterThanOrEqual => value >= t,
            AlertRuleOperator.LessThanOrEqual => value <= t,
            AlertRuleOperator.Equal => Math.Abs(value - t) < 0.0001,
            _ => false
        };
    }

    private static string DescribeOperator(AlertRuleOperator op) => op switch
    {
        AlertRuleOperator.GreaterThan => ">",
        AlertRuleOperator.LessThan => "<",
        AlertRuleOperator.GreaterThanOrEqual => ">=",
        AlertRuleOperator.LessThanOrEqual => "<=",
        AlertRuleOperator.Equal => "==",
        _ => "?"
    };
}
