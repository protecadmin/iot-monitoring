using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Calculations;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/device-types")]
[Authorize]
public sealed class DeviceTypesController(IDeviceTypeRepository deviceTypes, DerivedParameterCalculations calculations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceTypeDto>>> List(CancellationToken ct)
    {
        var types = await deviceTypes.ListAllAsync(ct);
        return Ok(types.Select(Map).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceTypeDto>> Create(CreateDeviceTypeRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw ApiException.BadRequest("Name is required", "DEVICE_TYPE_NAME_REQUIRED");
        if (await deviceTypes.NameTakenAsync(name, null, ct))
            throw ApiException.Conflict("A device type with this name already exists", "DEVICE_TYPE_NAME_TAKEN");

        var type = new DeviceType
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ExpectedIntervalSeconds = ValidInterval(request.ExpectedIntervalSeconds),
            TiltAxes = ValidAxes(request.TiltAxes),
            SupportsCommands = request.SupportsCommands
        };
        await deviceTypes.AddAsync(type, ct);
        return Ok(Map(type));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceTypeDto>> Update(Guid id, UpdateDeviceTypeRequest request, CancellationToken ct)
    {
        var type = await deviceTypes.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        // Renaming is safe: devices, firmware and rules reference the type by
        // id, and the ingestion allow-list comes from its parameter keys, not
        // its name.
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw ApiException.BadRequest("Name is required", "DEVICE_TYPE_NAME_REQUIRED");
        if (await deviceTypes.NameTakenAsync(name, id, ct))
            throw ApiException.Conflict("A device type with this name already exists", "DEVICE_TYPE_NAME_TAKEN");

        type.Name = name;
        type.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        type.ExpectedIntervalSeconds = ValidInterval(request.ExpectedIntervalSeconds);
        type.SupportsCommands = request.SupportsCommands;
        if (request.TiltAxes is not null)
        {
            var axes = ValidAxes(request.TiltAxes)!;
            if (type.TiltAxes is {} existing && (existing.X != axes.X || existing.Y != axes.Y || existing.Z != axes.Z))
                throw ApiException.Conflict("Tilt axis keys are fixed to preserve daily history. Create a new device type for a different mapping.", "TILT_AXES_FIXED");
            type.TiltAxes = axes;
        }
        await deviceTypes.UpdateAsync(type, ct);
        return Ok(Map(type));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var type = await deviceTypes.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        // Refused rather than cascaded: a device can't exist without a type,
        // and a rule scoped to a deleted type would silently stop matching
        // anything. Say exactly what is in the way so it can be moved first.
        var usage = await deviceTypes.GetUsageAsync(id, ct);
        if (usage.InUse)
        {
            var parts = new List<string>();
            if (usage.Devices > 0) parts.Add($"{usage.Devices} device{(usage.Devices == 1 ? "" : "s")}");
            if (usage.Firmwares > 0) parts.Add($"{usage.Firmwares} firmware release{(usage.Firmwares == 1 ? "" : "s")}");
            if (usage.AlertRules > 0) parts.Add($"{usage.AlertRules} alert rule{(usage.AlertRules == 1 ? "" : "s")}");
            if (usage.CalculationRules > 0) parts.Add($"{usage.CalculationRules} calculation rule{(usage.CalculationRules == 1 ? "" : "s")}");
            throw ApiException.Conflict(
                $"\"{type.Name}\" is still used by {string.Join(", ", parts)}. Reassign or remove them before deleting this type.",
                "DEVICE_TYPE_IN_USE");
        }

        await deviceTypes.DeleteAsync(type, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/parameters")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceTypeParameterDto>> AddParameter(Guid id, CreateDeviceTypeParameterRequest request, CancellationToken ct)
    {
        var type = await deviceTypes.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");

        if (type.Parameters.Any(p => string.Equals(p.Key, request.Key, StringComparison.OrdinalIgnoreCase)))
            throw ApiException.Conflict("A parameter with this key already exists on this device type", "DEVICE_TYPE_PARAMETER_KEY_TAKEN");

        var parameter = new DeviceTypeParameter
        {
            DeviceTypeId = id,
            Key = request.Key,
            Label = request.Label,
            Unit = request.Unit,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Kind = request.Kind ?? ParameterKind.Instantaneous,
            DashboardWidget = request.DashboardWidget
        };
        ApplyCalculation(parameter, request.Calculation);
        Validate(type, parameter);

        await deviceTypes.AddParameterAsync(parameter, ct);
        var warnings = await calculations.ApplyAsync(type, parameter, ct);
        return Ok(Map(parameter) with { Warnings = warnings.Count > 0 ? warnings : null });
    }

    // Key deliberately isn't editable here — it's what ties a device's raw
    // telemetry field name to this parameter (see TelemetryNormalizer);
    // changing it after devices are already reporting would silently orphan
    // their existing data under the old key instead of remapping it.
    [HttpPut("{id:guid}/parameters/{parameterId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceTypeParameterDto>> UpdateParameter(Guid id, Guid parameterId, UpdateDeviceTypeParameterRequest request, CancellationToken ct)
    {
        var type = await deviceTypes.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");
        var current = type.Parameters.FirstOrDefault(p => p.Id == parameterId)
            ?? throw ApiException.NotFound("Parameter not found", "DEVICE_TYPE_PARAMETER_NOT_FOUND");

        // Validated on a copy first, so an invalid calculation saves nothing.
        var proposed = new DeviceTypeParameter { Id = current.Id, DeviceTypeId = id, Key = current.Key };
        void Apply(DeviceTypeParameter p)
        {
            p.Label = request.Label;
            p.Unit = request.Unit;
            p.MinValue = request.MinValue;
            p.MaxValue = request.MaxValue;
            p.Kind = request.Kind ?? current.Kind;
            p.DashboardWidget = request.DashboardWidget;
            ApplyCalculation(p, request.Calculation);
        }
        Apply(proposed);
        if (IsTiltAxis(type, current.Key) && proposed.Kind == ParameterKind.Derived)
            throw ApiException.Conflict("A tilt axis must remain a measured parameter to preserve daily history.", "TILT_AXIS_IN_USE");
        Validate(type, proposed);

        var parameter = await deviceTypes.UpdateParameterAsync(id, parameterId, Apply, ct)
            ?? throw ApiException.NotFound("Parameter not found", "DEVICE_TYPE_PARAMETER_NOT_FOUND");
        var warnings = await calculations.ApplyAsync(type, parameter, ct);
        return Ok(Map(parameter) with { Warnings = warnings.Count > 0 ? warnings : null });
    }

    [HttpDelete("{id:guid}/parameters/{parameterId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RemoveParameter(Guid id, Guid parameterId, CancellationToken ct)
    {
        var type = await deviceTypes.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Device type not found", "DEVICE_TYPE_NOT_FOUND");
        var parameter = type.Parameters.FirstOrDefault(p => p.Id == parameterId)
            ?? throw ApiException.NotFound("Parameter not found", "DEVICE_TYPE_PARAMETER_NOT_FOUND");

        if (IsTiltAxis(type, parameter.Key))
            throw ApiException.Conflict("This parameter defines a tilt axis and cannot be removed.", "TILT_AXIS_IN_USE");
        await deviceTypes.RemoveParameterAsync(id, parameterId, ct);
        // Its calculation rules would publish a value nothing defines any more.
        if (parameter.CalculationType is not null)
            await calculations.RemoveAsync(type, parameter.Key, ct);
        return NoContent();
    }

    private static bool IsTiltAxis(DeviceType type, string key) => type.TiltAxes is {} axes
        && (axes.X == key || axes.Y == key || axes.Z == key);

    private static TiltAxes? ValidAxes(TiltAxes? axes)
    {
        if (axes is null) return null;
        var keys = new[] { axes.X, axes.Y }.Concat(axes.Z is null ? [] : new[] { axes.Z }).ToArray();
        if(keys.Any(k=>string.IsNullOrWhiteSpace(k)||k.Length>64) || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=keys.Length)
            throw ApiException.BadRequest("Tilt axis parameter keys must be nonempty, distinct, and at most 64 characters.", "TILT_AXES_INVALID");
        return axes;
    }

    private static DeviceTypeDto Map(DeviceType t) => new(
        t.Id, t.Name, t.Description,
        t.Parameters.Select(Map).ToList(),
        t.ExpectedIntervalSeconds, t.SupportsCommands, t.TiltAxes);

    /// <summary>A reporting interval of 10 s to 7 days; null or 0 clears it.</summary>
    private static int? ValidInterval(int? seconds) => seconds switch
    {
        null or 0 => null,
        < 10 or > 7 * 86400 => throw ApiException.BadRequest("Reporting interval must be between 10 seconds and 7 days", "DEVICE_TYPE_INTERVAL_INVALID"),
        _ => seconds
    };

    /// <summary>A calculation is only kept on a Derived parameter; any other Kind clears it.</summary>
    private static void ApplyCalculation(DeviceTypeParameter p, ParameterCalculationDto? calculation)
    {
        if (p.Kind != ParameterKind.Derived || calculation is null)
        {
            p.CalculationType = null;
            p.CalculationExpression = null;
            p.CalculationSourceKey = null;
            p.CalculationMaxDelta = null;
            p.CalculationWindowSeconds = null;
            return;
        }

        if (!CalculationTypeNames.TryParse(calculation.Type, out var type))
            throw ApiException.BadRequest($"Unsupported calculation type '{calculation.Type}'", "CALCULATION_TYPE_UNSUPPORTED");
        var isFormula = type == CalculationType.Formula;
        p.CalculationType = type;
        p.CalculationExpression = isFormula ? calculation.Expression?.Trim() : null;
        p.CalculationSourceKey = isFormula ? null : calculation.SourceParameterKey?.Trim();
        p.CalculationMaxDelta = isFormula ? null : calculation.MaxDelta;
        var isWindowed = type is CalculationType.TimeWeightedAverage or CalculationType.EquivalentContinuousLevel;
        p.CalculationWindowSeconds = isWindowed ? (calculation.WindowSeconds ?? LevelWindow.DefaultSeconds) : null;
    }

    /// <summary>The calculation is checked against the type's parameters as they will be after this save.</summary>
    private void Validate(DeviceType type, DeviceTypeParameter parameter)
    {
        var after = new DeviceType
        {
            Id = type.Id,
            Name = type.Name,
            Parameters = type.Parameters.Where(p => p.Id != parameter.Id).Append(parameter).ToList()
        };
        if (calculations.Validate(after, parameter) is { } problem)
            throw ApiException.BadRequest(problem, "DEVICE_TYPE_PARAMETER_CALCULATION_INVALID");
    }

    private static DeviceTypeParameterDto Map(DeviceTypeParameter p) =>
        new(p.Id, p.Key, p.Label, p.Unit, p.MinValue, p.MaxValue, p.Kind.ToString(),
            p.CalculationType is { } t
                ? new ParameterCalculationDto(CalculationTypeNames.ToApi(t), p.CalculationExpression, p.CalculationSourceKey, p.CalculationMaxDelta, p.CalculationWindowSeconds)
                : null,
            DashboardWidget: p.DashboardWidget?.ToString());
}
