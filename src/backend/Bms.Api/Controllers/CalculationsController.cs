using System.Text.Json;
using System.Text.RegularExpressions;
using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Calculations;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// The Calculations module: configuring derived parameters and reading their results.
///
/// Every endpoint goes through RequireTenantAccess, and every rule, site and
/// device a request names is looked up within that tenant — so a rule or
/// result belonging to another tenant resolves to not-found, even for a
/// guessed or copied id.
/// </summary>
[ApiController]
[Route("api/calculations")]
[Authorize]
public sealed partial class CalculationsController(
    ICalculationRuleRepository rules,
    ICalculationStateRepository states,
    IDeviceCalculationAssignmentRepository assignments,
    ICalculationReader reader,
    IEnumerable<ICalculationHandler> handlers,
    IDeviceRepository devices,
    ISiteRepository sites,
    IDeviceTypeRepository deviceTypes,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex RuleKeyPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex OutputKeyPattern();

    [HttpGet("types")]
    public ActionResult<IReadOnlyList<CalculationTypeDto>> Types() =>
        Ok(handlers.Select(h => new CalculationTypeDto(
            CalculationTypeNames.ToApi(h.Type), CalculationTypeNames.DisplayName(h.Type),
            CalculationTypeNames.Description(h.Type), h.SourceRequirement)).ToList());

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<CalculationRuleDto>>> ListRules([FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var types = await deviceTypes.ListAllAsync(ct);
        return Ok((await rules.ListAsync(tenantId, ct)).Select(r => Map(r, IsDefinedOnDeviceType(types, r))).ToList());
    }

    [HttpGet("rules/{id:guid}")]
    public async Task<ActionResult<CalculationRuleDto>> GetRule(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await FindAsync(tenantId, id, ct);
        return Ok(Map(rule, await IsDefinedOnDeviceTypeAsync(rule, ct)));
    }

    [HttpPost("rules")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<CalculationRuleDto>> CreateRule([FromQuery] Guid tenantId, CreateCalculationRuleRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);

        if (!CalculationTypeNames.TryParse(request.Type, out var type))
            throw ApiException.BadRequest($"Unsupported calculation type '{request.Type}'", "CALCULATION_TYPE_UNSUPPORTED");

        var key = (request.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (!RuleKeyPattern().IsMatch(key))
            throw ApiException.BadRequest("Key must be 2–63 lowercase letters, digits or hyphens", "CALCULATION_RULE_KEY_INVALID");
        if (await rules.GetByKeyAsync(tenantId, key, ct) is not null)
            throw ApiException.Conflict("A calculation rule with this key already exists", "CALCULATION_RULE_KEY_TAKEN");

        var rule = new CalculationRule
        {
            TenantId = tenantId, Key = key, Type = type, IsEnabled = true,
            AssignmentMode = ParseMode(request.AssignmentMode) ?? CalculationAssignmentMode.Automatic
        };
        Apply(rule, request.Name, request.SourceParameterKey, request.Expression, request.OutputKey, request.OutputName, request.OutputUnit,
            request.MaxDelta, request.SiteId, request.DeviceId, request.DeviceTypeId, request.WindowSeconds);
        await ValidateAsync(rule, ct);

        await rules.AddAsync(rule, ct);
        await AuditAsync("CalculationRule.Create", rule, new { rule.Name, rule.Key, Type = CalculationTypeNames.ToApi(rule.Type), rule.SourceParameterKey, rule.Expression, rule.OutputKey }, ct);
        return Ok(Map(rule));
    }

    [HttpPut("rules/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<CalculationRuleDto>> UpdateRule(Guid id, [FromQuery] Guid tenantId, UpdateCalculationRuleRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await FindAsync(tenantId, id, ct);

        // A Derived parameter's calculation is edited on its device type, for
        // every tenant at once; an edit here would be overwritten the next time
        // the parameter is saved. Only switching it on or off is the tenant's.
        if (await IsDefinedOnDeviceTypeAsync(rule, ct))
        {
            var requested = new CalculationRule { Type = rule.Type };
            Apply(requested, request.Name, request.SourceParameterKey, request.Expression, request.OutputKey, request.OutputName, request.OutputUnit,
                request.MaxDelta, request.SiteId, request.DeviceId, request.DeviceTypeId, request.WindowSeconds ?? rule.WindowSeconds);
            if (!Definition(requested).Equals(Definition(rule)))
                throw ApiException.BadRequest(
                    "This calculation is defined on the Device Types page — edit the Derived parameter there (it applies to every company). Here it can only be enabled or disabled.",
                    "CALCULATION_RULE_DEFINED_ON_DEVICE_TYPE");

            rule.IsEnabled = request.IsEnabled;
            await rules.UpdateAsync(rule, ct);
            await AuditAsync("CalculationRule.Update", rule, new { rule.Name, rule.IsEnabled }, ct);
            return Ok(Map(rule, definedOnDeviceType: true));
        }

        var before = (Source: rule.SourceParameterKey.ToLowerInvariant(), rule.Expression, rule.MaxDelta, rule.SiteId, rule.DeviceId, rule.DeviceTypeId, rule.AssignmentMode);

        Apply(rule, request.Name, request.SourceParameterKey, request.Expression, request.OutputKey, request.OutputName, request.OutputUnit,
            request.MaxDelta, request.SiteId, request.DeviceId, request.DeviceTypeId, request.WindowSeconds);
        rule.IsEnabled = request.IsEnabled;
        if (ParseMode(request.AssignmentMode) is { } mode)
            rule.AssignmentMode = mode;
        await ValidateAsync(rule, ct);
        await rules.UpdateAsync(rule, ct);

        // A different source, scope or delta limit makes every stored baseline
        // wrong — the next reading must start a fresh accumulation period, not
        // be compared against a value produced under the old definition.
        var after = (Source: rule.SourceParameterKey.ToLowerInvariant(), rule.Expression, rule.MaxDelta, rule.SiteId, rule.DeviceId, rule.DeviceTypeId, rule.AssignmentMode);
        var baselineReset = before != after;
        if (baselineReset)
            await states.DeleteForRuleAsync(rule.Id, ct);

        await AuditAsync("CalculationRule.Update", rule, new { rule.Name, rule.SourceParameterKey, rule.Expression, rule.OutputKey, rule.IsEnabled, BaselineReset = baselineReset }, ct);
        return Ok(Map(rule));
    }

    [HttpDelete("rules/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteRule(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await FindAsync(tenantId, id, ct);
        if (await IsDefinedOnDeviceTypeAsync(rule, ct))
            throw ApiException.BadRequest(
                "This calculation is defined on the Device Types page, so it would come back the next time that parameter is saved. Disable it here to stop it for this company, or delete the Derived parameter on Device Types to remove it everywhere.",
                "CALCULATION_RULE_DEFINED_ON_DEVICE_TYPE");

        // Live state cascades with the rule. Raw telemetry is untouched, so no
        // measured data is lost — only the derived view of it.
        await rules.DeleteAsync(tenantId, id, ct);
        await AuditAsync("CalculationRule.Delete", rule, new { rule.Name, rule.Key }, ct);
        return NoContent();
    }

    /// <summary>Devices this rule currently calculates for, with each one's latest value.</summary>
    [HttpGet("rules/{id:guid}/devices")]
    public async Task<ActionResult<IReadOnlyList<CalculationRuleDeviceDto>>> RuleDevices(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var rule = await FindAsync(tenantId, id, ct);

        var candidates = await devices.ListAsync(new DeviceFilter { TenantId = tenantId, SiteId = rule.SiteId, DeviceTypeId = rule.DeviceTypeId, PageSize = 200 }, ct);

        // Source eligibility depends only on the device type, so it is checked
        // once per type rather than once per device.
        // A Selected rule only reaches devices it was picked for in the device form.
        HashSet<Guid>? selected = rule.AssignmentMode == CalculationAssignmentMode.Selected
            ? (await assignments.GetDeviceIdsForRuleAsync(rule.Id, ct)).ToHashSet()
            : null;

        var eligibleByType = new Dictionary<Guid, bool>();
        var applicable = new List<Device>();
        foreach (var device in candidates.Items)
        {
            if (!CalculationScope.Matches(rule, device))
                continue;
            if (selected is not null && !selected.Contains(device.Id))
                continue;
            if (!eligibleByType.TryGetValue(device.DeviceTypeId, out var eligible))
                eligibleByType[device.DeviceTypeId] = eligible = await reader.AppliesToAsync(rule, device, ct);
            if (eligible)
                applicable.Add(device);
        }

        var latest = await reader.GetLatestAsync(applicable.Select(d => d.Id).ToList(), ct);
        return Ok(applicable.Select(d => new CalculationRuleDeviceDto(
            d.Id, d.Name, d.DeviceCode, d.SiteId,
            latest.TryGetValue(d.Id, out var values) ? values.Where(v => v.RuleId == rule.Id).Select(MapLatest).FirstOrDefault() : null)).ToList());
    }

    [HttpGet("rules/{id:guid}/results")]
    public async Task<ActionResult<CalculationResultsDto>> Results(
        Guid id, [FromQuery] Guid tenantId, [FromQuery] Guid deviceId,
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] CalculationGranularity granularity = CalculationGranularity.Hourly, CancellationToken ct = default)
    {
        RequireTenantAccess(tenantId);
        var rule = await FindAsync(tenantId, id, ct);
        var device = await devices.GetByIdAsync(tenantId, deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");

        if (to <= from)
            throw ApiException.BadRequest("'to' must be after 'from'", "CALCULATION_RANGE_INVALID");

        var limit = MaxRange(granularity);
        if (to - from > limit)
            throw ApiException.Unprocessable($"A {granularity} query can span at most {limit.TotalDays:0} days — choose a coarser granularity", "CALCULATION_RANGE_TOO_LARGE");

        if (!await reader.AppliesToAsync(rule, device, ct))
            throw ApiException.Unprocessable("This calculation rule does not apply to that device", "CALCULATION_RULE_NOT_APPLICABLE");

        var results = await reader.GetResultsAsync(rule, device, from, to, granularity, ct);
        return Ok(new CalculationResultsDto(
            rule.Id, device.Id, rule.OutputKey, rule.OutputName, rule.OutputUnit,
            granularity.ToString(), from, to,
            results.Points.Select(p => new CalculatedPointDto(p.Timestamp, p.Value)).ToList(),
            results.Total,
            results.Latest is { } l ? MapLatest(l) : null));
    }

    /// <summary>
    /// The rules a device of this type (at this site) can use — what the device form offers.
    /// Automatic ones are included so the form can show they already apply; Selected ones are
    /// what the user picks. Eligibility is decided here, not re-implemented in the browser.
    /// </summary>
    [HttpGet("eligible")]
    public async Task<ActionResult<IReadOnlyList<EligibleCalculationRuleDto>>> Eligible(
        [FromQuery] Guid tenantId, [FromQuery] Guid deviceTypeId, [FromQuery] Guid? siteId, [FromQuery] Guid? deviceId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var type = await deviceTypes.GetByIdAsync(deviceTypeId, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        var eligible = (await rules.ListAsync(tenantId, ct))
            .Where(r =>
                (r.DeviceTypeId is null || r.DeviceTypeId == deviceTypeId) &&
                (r.SiteId is null || r.SiteId == siteId) &&
                (r.DeviceId is null || r.DeviceId == deviceId) &&
                IsSourceEligible(r, type))
            .Select(r => new EligibleCalculationRuleDto(Map(r, DerivedParameterCalculations.IsDefinedBy(type, r)), r.AssignmentMode == CalculationAssignmentMode.Automatic))
            .ToList();

        return Ok(eligible);
    }

    [HttpGet("/api/devices/{deviceId:guid}/calculations")]
    public async Task<ActionResult<DeviceCalculationsDto>> DeviceCalculations(Guid deviceId, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        _ = await devices.GetByIdAsync(tenantId, deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        return Ok(new DeviceCalculationsDto(deviceId, await assignments.GetRuleIdsForDeviceAsync(deviceId, ct)));
    }

    /// <summary>Sets exactly which Selected-mode rules a device uses. Called by the device form on create and edit.</summary>
    [HttpPut("/api/devices/{deviceId:guid}/calculations")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<DeviceCalculationsDto>> SetDeviceCalculations(
        Guid deviceId, [FromQuery] Guid tenantId, SetDeviceCalculationsRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var device = await devices.GetByIdAsync(tenantId, deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        var type = await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        var requested = (request.RuleIds ?? []).Distinct().ToList();
        foreach (var ruleId in requested)
        {
            // Tenant-scoped lookup: another tenant's rule id resolves to not-found.
            var rule = await rules.GetByIdAsync(tenantId, ruleId, ct)
                ?? throw ApiException.NotFound("Calculation rule not found", "CALCULATION_RULE_NOT_FOUND");
            if (rule.AssignmentMode == CalculationAssignmentMode.Automatic)
                throw ApiException.Unprocessable($"'{rule.Name}' already applies automatically to every device in its scope, so it isn't selected per device", "CALCULATION_RULE_AUTOMATIC");
            if (!CalculationScope.Matches(rule, device) || !IsSourceEligible(rule, type))
                throw ApiException.Unprocessable($"'{rule.Name}' can't be used on this device — check its scope, and that {type.Name} defines the parameters it reads", "CALCULATION_RULE_NOT_APPLICABLE");
        }

        await assignments.ReplaceForDeviceAsync(tenantId, deviceId, requested, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "Device.CalculationsSelected",
            EntityType = nameof(Device),
            EntityId = deviceId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { RuleIds = requested })
        }, ct);

        return Ok(new DeviceCalculationsDto(deviceId, requested));
    }

    private bool IsSourceEligible(CalculationRule rule, DeviceType type)
    {
        var handler = handlers.FirstOrDefault(h => h.Type == rule.Type);
        return handler is not null && CalculationScope.IsEligible(handler, rule, type.Parameters);
    }

    /// <summary>Whether this type averages over a period, and so carries a window.</summary>
    private static bool IsWindowed(CalculationType type) =>
        type is CalculationType.TimeWeightedAverage or CalculationType.EquivalentContinuousLevel;

    private static CalculationAssignmentMode? ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<CalculationAssignmentMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
            return mode;
        throw ApiException.BadRequest($"Unknown assignment mode '{value}' — use Automatic or Selected", "CALCULATION_ASSIGNMENT_MODE_INVALID");
    }

    /// <summary>Keeps a single response to a sensible size: finer granularity, shorter window.</summary>
    private static TimeSpan MaxRange(CalculationGranularity granularity) => granularity switch
    {
        CalculationGranularity.Reading => TimeSpan.FromDays(7),
        CalculationGranularity.FiveMinutes => TimeSpan.FromDays(31),
        CalculationGranularity.Hourly => TimeSpan.FromDays(366),
        _ => TimeSpan.FromDays(3660)
    };

    private async Task ValidateAsync(CalculationRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 128)
            throw ApiException.BadRequest("Name is required (up to 128 characters)", "CALCULATION_RULE_NAME_INVALID");
        if (rule.Type == CalculationType.Formula)
        {
            if (!FormulaExpression.TryParse(rule.Expression, out var formula, out var formulaError))
                throw ApiException.BadRequest($"Formula: {formulaError}", "CALCULATION_EXPRESSION_INVALID");
            if (formula!.Variables.Any(v => CalculationScope.KeyEquals(v, rule.OutputKey)))
                throw ApiException.Conflict("A formula can't use its own output", "CALCULATION_OUTPUT_KEY_CLASH");
        }
        else if (string.IsNullOrWhiteSpace(rule.SourceParameterKey))
        {
            throw ApiException.BadRequest("A source parameter is required", "CALCULATION_SOURCE_REQUIRED");
        }
        if (!OutputKeyPattern().IsMatch(rule.OutputKey))
            throw ApiException.BadRequest("Output key must start with a letter and contain only letters, digits or underscores", "CALCULATION_OUTPUT_KEY_INVALID");
        if (string.IsNullOrWhiteSpace(rule.OutputName) || rule.OutputName.Length > 128)
            throw ApiException.BadRequest("Output name is required (up to 128 characters)", "CALCULATION_OUTPUT_NAME_INVALID");
        if (rule.OutputUnit is { Length: > 32 })
            throw ApiException.BadRequest("Output unit can be at most 32 characters", "CALCULATION_OUTPUT_UNIT_INVALID");
        if (rule.MaxDelta is <= 0)
            throw ApiException.BadRequest("Max delta must be greater than zero", "CALCULATION_MAX_DELTA_INVALID");
        if (IsWindowed(rule.Type) && rule.WindowSeconds is not (>= LevelWindow.MinSeconds and <= LevelWindow.MaxSeconds))
            throw ApiException.BadRequest(
                $"The averaging window must be between {LevelWindow.MinSeconds} seconds and {LevelWindow.MaxSeconds / 3600} hours",
                "CALCULATION_WINDOW_INVALID");
        if (rule.Type != CalculationType.Formula && CalculationScope.KeyEquals(rule.OutputKey, rule.SourceParameterKey))
            throw ApiException.Conflict("The output key must differ from the source parameter key", "CALCULATION_OUTPUT_KEY_CLASH");

        var handler = handlers.FirstOrDefault(h => h.Type == rule.Type)
            ?? throw ApiException.BadRequest($"Unsupported calculation type {rule.Type}", "CALCULATION_TYPE_UNSUPPORTED");

        // Scope ownership: site and device must belong to this rule's tenant.
        if (rule.SiteId is { } siteId && await sites.GetByIdAsync(rule.TenantId, siteId, ct) is null)
            throw ApiException.NotFound("Site not found in this tenant", "SITE_NOT_FOUND");

        DeviceType? pinnedType = null;
        if (rule.DeviceId is { } deviceId)
        {
            var device = await devices.GetByIdAsync(rule.TenantId, deviceId, ct)
                ?? throw ApiException.NotFound("Device not found in this tenant", "DEVICE_NOT_FOUND");
            if (rule.SiteId is { } s && device.SiteId != s)
                throw ApiException.Unprocessable("That device is not at the selected site, so this rule would never apply", "CALCULATION_SCOPE_EMPTY");
            if (rule.DeviceTypeId is { } t && device.DeviceTypeId != t)
                throw ApiException.Unprocessable("That device is not of the selected device type, so this rule would never apply", "CALCULATION_SCOPE_EMPTY");
            pinnedType = await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct);
        }

        if (rule.DeviceTypeId is { } typeId && pinnedType is null)
            pinnedType = await deviceTypes.GetByIdAsync(typeId, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        IReadOnlyList<DeviceType> candidateTypes = pinnedType is not null ? [pinnedType] : await deviceTypes.ListAllAsync(ct);

        // Eligibility comes from the parameter's declared Kind, not its unit — a
        // kWh parameter that isn't marked Counter is never treated as a totalizer.
        if (!candidateTypes.Any(t => CalculationScope.IsEligible(handler, rule, t.Parameters)))
        {
            var inputs = string.Join(", ", handler.InputKeys(rule).Select(k => $"'{k}'"));
            throw ApiException.Unprocessable(
                pinnedType is not null
                    ? $"{inputs} on {pinnedType.Name} must each be {handler.SourceRequirement}. Add or fix them on the Device Types page."
                    : $"No device type has {inputs} as {handler.SourceRequirement}.",
                "CALCULATION_SOURCE_INELIGIBLE");
        }

        // Derived values are shown beside measured ones; an output sharing a key
        // with a measured parameter, or with another rule, would make the two
        // indistinguishable in the live table and charts.
        if (candidateTypes.SelectMany(t => t.Parameters).Any(p => p.Kind != ParameterKind.Derived && CalculationScope.KeyEquals(p.Key, rule.OutputKey)))
            throw ApiException.Conflict($"Output key '{rule.OutputKey}' is already used by a measured parameter", "CALCULATION_OUTPUT_KEY_CLASH");

        var siblings = await rules.ListAsync(rule.TenantId, ct);
        if (siblings.Any(r => r.Id != rule.Id && CalculationScope.KeyEquals(r.OutputKey, rule.OutputKey)))
            throw ApiException.Conflict($"Another calculation rule already outputs '{rule.OutputKey}'", "CALCULATION_OUTPUT_KEY_CLASH");
    }

    private static void Apply(
        CalculationRule rule, string name, string? sourceParameterKey, string? expression, string outputKey, string outputName,
        string? outputUnit, double? maxDelta, Guid? siteId, Guid? deviceId, Guid? deviceTypeId, int? windowSeconds)
    {
        // A Formula reads the parameters its expression names; every other
        // type reads one source parameter. Each keeps only its own field, so
        // a rule never carries a stale value of the other.
        var isFormula = rule.Type == CalculationType.Formula;
        rule.Name = (name ?? string.Empty).Trim();
        rule.SourceParameterKey = isFormula ? string.Empty : (sourceParameterKey ?? string.Empty).Trim();
        rule.Expression = isFormula ? expression?.Trim() : null;
        rule.OutputKey = (outputKey ?? string.Empty).Trim();
        rule.OutputName = (outputName ?? string.Empty).Trim();
        rule.OutputUnit = string.IsNullOrWhiteSpace(outputUnit) ? null : outputUnit.Trim();
        rule.MaxDelta = isFormula ? null : maxDelta;
        // Only a windowed type has an averaging period; the default is applied
        // here rather than at evaluation, so the period a rule used is always
        // visible on the rule itself.
        rule.WindowSeconds = IsWindowed(rule.Type) ? (windowSeconds ?? LevelWindow.DefaultSeconds) : null;
        rule.SiteId = siteId;
        rule.DeviceId = deviceId;
        rule.DeviceTypeId = deviceTypeId;
    }

    private async Task<CalculationRule> FindAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        await rules.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Calculation rule not found", "CALCULATION_RULE_NOT_FOUND");

    private Task AuditAsync(string action, CalculationRule rule, object details, CancellationToken ct) =>
        auditLog.AddAsync(new AuditLog
        {
            TenantId = rule.TenantId,
            UserId = CurrentUserId,
            Action = action,
            EntityType = nameof(CalculationRule),
            EntityId = rule.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(details)
        }, ct);

    private async Task<bool> IsDefinedOnDeviceTypeAsync(CalculationRule rule, CancellationToken ct) =>
        rule.DeviceTypeId is { } typeId && await deviceTypes.GetByIdAsync(typeId, ct) is { } type && DerivedParameterCalculations.IsDefinedBy(type, rule);

    private static bool IsDefinedOnDeviceType(IEnumerable<DeviceType> types, CalculationRule rule) =>
        types.Any(t => DerivedParameterCalculations.IsDefinedBy(t, rule));

    /// <summary>What a rule calculates and for which devices — everything but whether it is on.</summary>
    private static object Definition(CalculationRule r) =>
        (r.Name, Source: r.SourceParameterKey.ToLowerInvariant(), r.Expression, r.OutputKey, r.OutputName, r.OutputUnit, r.MaxDelta,
         r.WindowSeconds, r.SiteId, r.DeviceId, r.DeviceTypeId);

    private static CalculationRuleDto Map(CalculationRule r) => Map(r, definedOnDeviceType: false);

    private static CalculationRuleDto Map(CalculationRule r, bool definedOnDeviceType) => new(
        r.Id, r.TenantId, r.SiteId, r.DeviceId, r.DeviceTypeId, r.Name, r.Key, CalculationTypeNames.ToApi(r.Type),
        r.SourceParameterKey, r.OutputKey, r.OutputName, r.OutputUnit, r.MaxDelta, r.IsEnabled, r.CreatedAt, r.UpdatedAt, r.AssignmentMode.ToString(), r.Expression,
        r.WindowSeconds, definedOnDeviceType);

    private static LatestCalculatedValueDto MapLatest(LatestCalculatedValue v) =>
        new(v.RuleId, v.OutputKey, v.OutputName, v.OutputUnit, v.Value, v.Timestamp);
}
