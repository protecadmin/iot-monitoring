using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Api.Realtime;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Common;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public sealed class AlertsController(
    IAlertRepository alerts,
    IAlertRuleRepository alertRules,
    IDeviceRepository devices,
    IRealtimeNotifier notifier,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlertDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] Guid? deviceId, [FromQuery] AlertStatus? status,
        [FromQuery] AlertSeverity? severity, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var filter = new AlertFilter { TenantId = effectiveTenantId, DeviceId = deviceId, Status = status, Severity = severity, Page = page, PageSize = pageSize };
        var result = await alerts.ListAsync(filter, ct);

        return Ok(new PagedResult<AlertDto>
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<AlertDto>> Acknowledge(Guid id, CancellationToken ct)
    {
        var alert = await alerts.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Alert not found", "ALERT_NOT_FOUND");
        RequireTenantAccess(alert.TenantId);

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTimeOffset.UtcNow;
        alert.AcknowledgedByUserId = CurrentUserId;
        await alerts.UpdateAsync(alert, ct);
        return Ok(Map(alert));
    }

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<AlertDto>> Resolve(Guid id, CancellationToken ct)
    {
        var alert = await alerts.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Alert not found", "ALERT_NOT_FOUND");
        RequireTenantAccess(alert.TenantId);

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = DateTimeOffset.UtcNow;
        alert.ResolvedByUserId = CurrentUserId;
        await alerts.UpdateAsync(alert, ct);
        await notifier.AlertResolvedAsync(alert, ct);
        return Ok(Map(alert));
    }

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<AlertRuleDto>>> ListRules([FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rules = await alertRules.ListAsync(tenantId, ct);
        return Ok(rules.Select(r => new AlertRuleDto(
            r.Id, r.SiteId, r.DeviceId, r.DeviceTypeId, r.Name, r.Metric, r.Operator.ToString(),
            r.Threshold, r.Severity.ToString(), r.CooldownSeconds, r.IsEnabled)).ToList());
    }

    /// <summary>
    /// Every rule each of the company's devices is evaluated against — its
    /// type's standard rules with the company's and device's changes applied,
    /// plus any company rules — one entry per device (DeviceId set), exactly
    /// what alert evaluation uses. For views that show a device's limits.
    /// </summary>
    [HttpGet("rules/effective")]
    public async Task<ActionResult<IReadOnlyList<AlertRuleDto>>> EffectiveRules([FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var tenantDevices = await devices.ListAsync(new DeviceFilter { TenantId = tenantId, PageSize = 200 }, ct);
        var result = new List<AlertRuleDto>();
        foreach (var d in tenantDevices.Items)
        {
            foreach (var r in await alertRules.GetApplicableRulesAsync(tenantId, d.SiteId, d.Id, d.DeviceTypeId, ct))
            {
                result.Add(new AlertRuleDto(
                    r.Id, d.SiteId, d.Id, d.DeviceTypeId, r.Name, r.Metric, r.Operator.ToString(),
                    r.Threshold, r.Severity.ToString(), r.CooldownSeconds, r.IsEnabled));
            }
        }
        return Ok(result);
    }

    [HttpPost("rules")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<AlertRuleDto>> CreateRule([FromQuery] Guid tenantId, CreateAlertRuleRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = new AlertRule
        {
            TenantId = tenantId,
            SiteId = request.SiteId,
            DeviceId = request.DeviceId,
            DeviceTypeId = request.DeviceTypeId,
            Name = request.Name,
            Metric = request.Metric,
            Operator = request.Operator,
            Threshold = request.Threshold,
            Severity = request.Severity,
            CooldownSeconds = request.CooldownSeconds,
            IsEnabled = true
        };
        await alertRules.AddAsync(rule, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "AlertRule.Create",
            EntityType = nameof(AlertRule),
            EntityId = rule.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { rule.Name, rule.Metric, Operator = rule.Operator.ToString(), rule.Threshold })
        }, ct);
        return Ok(new AlertRuleDto(rule.Id, rule.SiteId, rule.DeviceId, rule.DeviceTypeId, rule.Name,
            rule.Metric, rule.Operator.ToString(), rule.Threshold, rule.Severity.ToString(), rule.CooldownSeconds, rule.IsEnabled));
    }

    [HttpPut("rules/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<AlertRuleDto>> UpdateRule(Guid id, [FromQuery] Guid tenantId, UpdateAlertRuleRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await alertRules.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Alert rule not found", "ALERT_RULE_NOT_FOUND");

        rule.Name = request.Name;
        rule.Metric = request.Metric;
        rule.Operator = request.Operator;
        rule.Threshold = request.Threshold;
        rule.Severity = request.Severity;
        rule.CooldownSeconds = request.CooldownSeconds;
        rule.IsEnabled = request.IsEnabled;
        await alertRules.UpdateAsync(rule, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "AlertRule.Update",
            EntityType = nameof(AlertRule),
            EntityId = rule.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { rule.Name, rule.Metric, Operator = rule.Operator.ToString(), rule.Threshold, rule.IsEnabled })
        }, ct);

        return Ok(new AlertRuleDto(rule.Id, rule.SiteId, rule.DeviceId, rule.DeviceTypeId, rule.Name,
            rule.Metric, rule.Operator.ToString(), rule.Threshold, rule.Severity.ToString(), rule.CooldownSeconds, rule.IsEnabled));
    }

    [HttpDelete("rules/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteRule(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await alertRules.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Alert rule not found", "ALERT_RULE_NOT_FOUND");
        await alertRules.DeleteAsync(tenantId, id, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "AlertRule.Delete",
            EntityType = nameof(AlertRule),
            EntityId = rule.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { rule.Name, rule.Metric })
        }, ct);

        return NoContent();
    }

    private static AlertDto Map(Alert a) => new(
        a.Id, a.TenantId, a.DeviceId, a.AlertRuleId, a.Severity.ToString(), a.Status.ToString(),
        a.Parameter, a.CurrentValue, a.Threshold, a.Message, a.TriggeredAt, a.AcknowledgedAt, a.ResolvedAt);
}

public sealed record AlertRuleDto(
    Guid Id, Guid? SiteId, Guid? DeviceId, Guid? DeviceTypeId, string Name,
    string Metric, string Operator, double? Threshold, string Severity, int CooldownSeconds, bool IsEnabled);
