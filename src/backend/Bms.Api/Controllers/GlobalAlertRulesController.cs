using System.Text.Json;
using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Alerting;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// Alert rules defined once for every company, by device type, and each
/// company's changes to them at company, site or device level.
///
/// A rule is never copied: a device matches it by type, so a new device or a
/// new company is covered at once, and a change to the rule reaches every
/// level that didn't change that value itself.
/// </summary>
[ApiController]
[Route("api/alert-rules/global")]
[Authorize]
public sealed class GlobalAlertRulesController(
    IGlobalAlertRuleRepository globalRules,
    IDeviceTypeRepository deviceTypes,
    ISiteRepository sites,
    IDeviceRepository devices,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    private static readonly string[] Lifecycle = LifecycleMetrics.All;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GlobalAlertRuleDto>>> List(CancellationToken ct)
    {
        var counts = await globalRules.CountOverridesAsync(ct);
        return Ok((await globalRules.ListAsync(ct)).Select(r => Map(r, counts.GetValueOrDefault(r.Id))).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<GlobalAlertRuleDto>> Create(SaveGlobalAlertRuleRequest request, CancellationToken ct)
    {
        var rule = new GlobalAlertRule();
        await ApplyAsync(rule, request, ct);
        await globalRules.AddAsync(rule, ct);
        await AuditAsync("GlobalAlertRule.Create", rule, ct);
        return Ok(Map(rule, 0));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<GlobalAlertRuleDto>> Update(Guid id, SaveGlobalAlertRuleRequest request, CancellationToken ct)
    {
        var rule = await globalRules.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Global alert rule not found", "GLOBAL_ALERT_RULE_NOT_FOUND");
        var appliedByDefault = rule.AppliesByDefault;
        await ApplyAsync(rule, request, ct);
        await globalRules.UpdateAsync(rule, ct);
        // A company that switched the rule on (or off) to differ from the old
        // default now matches the new one — the switch, and its tag, go.
        if (rule.AppliesByDefault != appliedByDefault)
            await globalRules.ClearRedundantCompanySwitchesAsync(rule.Id, rule.AppliesByDefault, ct);
        await AuditAsync("GlobalAlertRule.Update", rule, ct);
        var counts = await globalRules.CountOverridesAsync(ct);
        return Ok(Map(rule, counts.GetValueOrDefault(rule.Id)));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var rule = await globalRules.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Global alert rule not found", "GLOBAL_ALERT_RULE_NOT_FOUND");
        await globalRules.DeleteAsync(id, ct);
        await AuditAsync("GlobalAlertRule.Delete", rule, ct);
        return NoContent();
    }

    /// <summary>
    /// The global rules as they apply in one company — optionally at a site,
    /// for a device type, or for one device — with where each value comes from.
    /// </summary>
    [HttpGet("effective")]
    public async Task<ActionResult<IReadOnlyList<EffectiveAlertRuleDto>>> Effective(
        [FromQuery] Guid tenantId, [FromQuery] Guid? siteId, [FromQuery] Guid? deviceTypeId, [FromQuery] Guid? deviceId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        (siteId, deviceTypeId) = await ResolveScopeAsync(tenantId, siteId, deviceTypeId, deviceId, ct);

        var rules = (await globalRules.ListAsync(ct))
            .Where(r => deviceTypeId is null || r.DeviceTypeId is null || r.DeviceTypeId == deviceTypeId)
            .ToList();
        var overrides = await globalRules.ListOverridesAsync(tenantId, null, ct);

        return Ok(rules.Select(r =>
        {
            var e = AlertRuleResolution.Resolve(r, overrides, tenantId, siteId, deviceId);
            var here = overrides.FirstOrDefault(o => o.GlobalAlertRuleId == r.Id && o.SiteId == (deviceId is null ? siteId : null) && o.DeviceId == deviceId);
            return new EffectiveAlertRuleDto(
                r.Id, r.Name, r.DeviceTypeId, r.Metric, r.Operator.ToString(),
                e.IsEnabled, e.EnabledFrom.ToString(),
                e.Threshold, e.ThresholdFrom.ToString(),
                e.Severity.ToString(), e.SeverityFrom.ToString(),
                e.CooldownSeconds, e.CooldownFrom.ToString(),
                here is null ? null : new AlertRuleOverrideDto(here.SiteId, here.DeviceId, here.IsEnabled, here.Threshold, here.Severity?.ToString(), here.CooldownSeconds));
        }).ToList());
    }

    /// <summary>Sets a company's change at one level — the company, a site, or a device. Null fields inherit; all null resets.</summary>
    [HttpPut("{id:guid}/override")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<IActionResult> SaveOverride(Guid id, [FromQuery] Guid tenantId, SaveAlertRuleOverrideRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await globalRules.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Global alert rule not found", "GLOBAL_ALERT_RULE_NOT_FOUND");
        var (siteId, deviceId) = await ValidateLevelAsync(tenantId, rule, request.SiteId, request.DeviceId, ct);
        if (request.CooldownSeconds is < 0)
            throw ApiException.BadRequest("Cooldown can't be negative", "ALERT_RULE_COOLDOWN_INVALID");

        if (request is { IsEnabled: null, Threshold: null, Severity: null, CooldownSeconds: null })
        {
            await globalRules.DeleteOverrideAsync(tenantId, id, siteId, deviceId, ct);
            await AuditOverrideAsync(tenantId, "AlertRuleOverride.Reset", rule, siteId, deviceId, null, ct);
            return NoContent();
        }

        var o = await globalRules.GetOverrideAsync(tenantId, id, siteId, deviceId, ct)
                ?? new AlertRuleOverride { TenantId = tenantId, GlobalAlertRuleId = id, SiteId = siteId, DeviceId = deviceId };
        o.IsEnabled = request.IsEnabled;
        o.Threshold = request.Threshold;
        o.Severity = request.Severity;
        o.CooldownSeconds = request.CooldownSeconds;
        await globalRules.SaveOverrideAsync(o, ct);
        await AuditOverrideAsync(tenantId, "AlertRuleOverride.Save", rule, siteId, deviceId, request, ct);
        return NoContent();
    }

    /// <summary>Removes the change at one level, so it follows the level above again.</summary>
    [HttpDelete("{id:guid}/override")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<IActionResult> ResetOverride(Guid id, [FromQuery] Guid tenantId, [FromQuery] Guid? siteId, [FromQuery] Guid? deviceId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await globalRules.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Global alert rule not found", "GLOBAL_ALERT_RULE_NOT_FOUND");
        (siteId, deviceId) = await ValidateLevelAsync(tenantId, rule, siteId, deviceId, ct);
        await globalRules.DeleteOverrideAsync(tenantId, id, siteId, deviceId, ct);
        await AuditOverrideAsync(tenantId, "AlertRuleOverride.Reset", rule, siteId, deviceId, null, ct);
        return NoContent();
    }

    private async Task ApplyAsync(GlobalAlertRule rule, SaveGlobalAlertRuleRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 128)
            throw ApiException.BadRequest("Name is required (up to 128 characters)", "ALERT_RULE_NAME_INVALID");
        if (request.CooldownSeconds < 0)
            throw ApiException.BadRequest("Cooldown can't be negative", "ALERT_RULE_COOLDOWN_INVALID");

        var metric = request.Metric?.Trim() ?? string.Empty;
        var lifecycle = Lifecycle.FirstOrDefault(m => string.Equals(m, metric, StringComparison.OrdinalIgnoreCase));
        if (lifecycle is not null)
        {
            metric = lifecycle;
        }
        else if (request.DeviceTypeId is { } typeId)
        {
            // The metric is one of the type's parameters (calculated ones too),
            // spelled as the type spells it — a typo would never fire.
            var type = await deviceTypes.GetByIdAsync(typeId, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");
            metric = type.Parameters.FirstOrDefault(p => string.Equals(p.Key, metric, StringComparison.OrdinalIgnoreCase))?.Key
                     ?? throw ApiException.BadRequest($"'{metric}' isn't a parameter of {type.Name}", "ALERT_RULE_METRIC_INVALID");
        }
        else
        {
            throw ApiException.BadRequest(
                "A rule for every device type can only watch Device Offline or No Telemetry — pick a device type for a measured value",
                "ALERT_RULE_METRIC_INVALID");
        }

        if (lifecycle is null && request.Threshold is null)
            throw ApiException.BadRequest("A threshold is required", "ALERT_RULE_THRESHOLD_REQUIRED");

        rule.Name = name;
        rule.DeviceTypeId = lifecycle is null ? request.DeviceTypeId : null;
        rule.Metric = metric;
        rule.Operator = request.Operator;
        rule.Threshold = request.Threshold;
        rule.Severity = request.Severity;
        rule.CooldownSeconds = request.CooldownSeconds;
        rule.IsEnabled = request.IsEnabled;
        rule.AppliesByDefault = request.AppliesByDefault;
    }

    /// <summary>A device fixes its site and type; a site must belong to the company.</summary>
    private async Task<(Guid? SiteId, Guid? DeviceTypeId)> ResolveScopeAsync(Guid tenantId, Guid? siteId, Guid? deviceTypeId, Guid? deviceId, CancellationToken ct)
    {
        if (deviceId is { } d)
        {
            var device = await devices.GetByIdAsync(tenantId, d, ct) ?? throw ApiException.NotFound("Device not found in this company", "DEVICE_NOT_FOUND");
            return (device.SiteId, device.DeviceTypeId);
        }
        if (siteId is { } s && await sites.GetByIdAsync(tenantId, s, ct) is null)
            throw ApiException.NotFound("Site not found in this company", "SITE_NOT_FOUND");
        return (siteId, deviceTypeId);
    }

    /// <summary>
    /// The level a change is stored at: the device (its own row, no site), a
    /// site, or the company (both null). A device must be one the rule can
    /// apply to.
    /// </summary>
    private async Task<(Guid? SiteId, Guid? DeviceId)> ValidateLevelAsync(Guid tenantId, GlobalAlertRule rule, Guid? siteId, Guid? deviceId, CancellationToken ct)
    {
        if (deviceId is { } d)
        {
            var device = await devices.GetByIdAsync(tenantId, d, ct) ?? throw ApiException.NotFound("Device not found in this company", "DEVICE_NOT_FOUND");
            if (rule.DeviceTypeId is { } t && device.DeviceTypeId != t)
                throw ApiException.Unprocessable($"'{rule.Name}' is for another device type, so it never applies to {device.Name}", "ALERT_RULE_NOT_APPLICABLE");
            return (null, d);
        }
        if (siteId is { } s && await sites.GetByIdAsync(tenantId, s, ct) is null)
            throw ApiException.NotFound("Site not found in this company", "SITE_NOT_FOUND");
        return (siteId, null);
    }

    private static GlobalAlertRuleDto Map(GlobalAlertRule r, int overrideCount) => new(
        r.Id, r.Name, r.DeviceTypeId, r.Metric, r.Operator.ToString(), r.Threshold,
        r.Severity.ToString(), r.CooldownSeconds, r.IsEnabled, r.AppliesByDefault, overrideCount);

    private Task AuditAsync(string action, GlobalAlertRule rule, CancellationToken ct) =>
        auditLog.AddAsync(new AuditLog
        {
            TenantId = null,
            UserId = CurrentUserId,
            Action = action,
            EntityType = nameof(GlobalAlertRule),
            EntityId = rule.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new
            {
                rule.Name, rule.DeviceTypeId, rule.Metric, Operator = rule.Operator.ToString(), rule.Threshold,
                Severity = rule.Severity.ToString(), rule.IsEnabled, rule.AppliesByDefault
            })
        }, ct);

    private Task AuditOverrideAsync(Guid tenantId, string action, GlobalAlertRule rule, Guid? siteId, Guid? deviceId, SaveAlertRuleOverrideRequest? change, CancellationToken ct) =>
        auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = action,
            EntityType = nameof(AlertRuleOverride),
            EntityId = rule.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new
            {
                Rule = rule.Name, siteId, deviceId,
                change?.IsEnabled, change?.Threshold, Severity = change?.Severity?.ToString(), change?.CooldownSeconds
            })
        }, ct);
}
