using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// A device's reference readings — "Change from baseline" calculations
/// measure movement from these. Taken from the device's latest reading, so
/// the value is one the device actually reported, not one typed in.
/// </summary>
[ApiController]
[Route("api/devices/{deviceId:guid}/baseline")]
[Authorize]
public sealed class DeviceBaselinesController(
    IDeviceRepository devices,
    IDeviceTypeRepository deviceTypes,
    IDeviceBaselineRepository baselines,
    ITelemetryStore telemetry,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceBaselineDto>>> List(Guid deviceId, CancellationToken ct)
    {
        var device = await FindAsync(deviceId, ct);
        var list = await baselines.ListAsync(device.Id, ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<IReadOnlyList<DeviceBaselineDto>>> Set(Guid deviceId, SetDeviceBaselineRequest request, CancellationToken ct)
    {
        var device = await FindAsync(deviceId, ct);
        var latest = await telemetry.GetLatestAsync(device.Id, ct)
                     ?? throw ApiException.BadRequest("This device hasn't sent a reading yet — set the baseline once it has", "BASELINE_NO_READING");

        // Measured parameters only: a baseline of a calculated value would
        // move with the calculation, not with the structure.
        var type = await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct);
        var measured = (type?.Parameters ?? [])
            .Where(p => p.Kind != ParameterKind.Derived)
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wanted = request.ParameterKeys is { Count: > 0 } keys ? keys : latest.Fields.Keys.ToList();
        var missing = wanted.Where(k => !measured.Contains(k) || !latest.Fields.ContainsKey(k)).ToList();
        if (missing.Count > 0 && request.ParameterKeys is { Count: > 0 })
            throw ApiException.BadRequest($"No measured value in the latest reading for: {string.Join(", ", missing)}", "BASELINE_PARAMETER_UNAVAILABLE");

        var now = DateTimeOffset.UtcNow;
        var rows = wanted.Where(k => measured.Contains(k) && latest.Fields.ContainsKey(k))
            .Select(k => new DeviceBaseline
            {
                TenantId = device.TenantId,
                DeviceId = device.Id,
                ParameterKey = measured.First(m => string.Equals(m, k, StringComparison.OrdinalIgnoreCase)),
                Value = latest.Fields[k],
                ReadingAt = latest.Timestamp,
                SetByUserId = CurrentUserId,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();
        if (rows.Count == 0)
            throw ApiException.BadRequest("The latest reading has no measured values to use as a baseline", "BASELINE_NO_READING");

        await baselines.SaveAsync(device.Id, rows, ct);
        await AuditAsync(device, "Device.SetBaseline", rows.ToDictionary(r => r.ParameterKey, r => (object)r.Value), ct);
        return Ok((await baselines.ListAsync(device.Id, ct)).Select(Map).ToList());
    }

    /// <summary>Clears one parameter's baseline (?parameterKey=xDeg), or all of them.</summary>
    [HttpDelete]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<IActionResult> Reset(Guid deviceId, [FromQuery] string? parameterKey, CancellationToken ct)
    {
        var device = await FindAsync(deviceId, ct);
        await baselines.DeleteAsync(device.Id, string.IsNullOrWhiteSpace(parameterKey) ? null : parameterKey, ct);
        await AuditAsync(device, "Device.ResetBaseline", new Dictionary<string, object> { ["parameter"] = parameterKey ?? "all" }, ct);
        return NoContent();
    }

    private async Task<Device> FindAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        return device;
    }

    private Task AuditAsync(Device device, string action, IDictionary<string, object> details, CancellationToken ct) =>
        auditLog.AddAsync(new AuditLog
        {
            TenantId = device.TenantId,
            UserId = CurrentUserId,
            Action = action,
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { device.DeviceCode, details })
        }, ct);

    private static DeviceBaselineDto Map(DeviceBaseline b) => new(b.ParameterKey, b.Value, b.ReadingAt, b.UpdatedAt);
}
