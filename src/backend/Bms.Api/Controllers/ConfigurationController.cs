using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/devices/{deviceId:guid}/configuration")]
[Authorize]
public sealed class ConfigurationController(
    IDeviceConfigurationRepository configurations,
    IDeviceRepository devices,
    IDeviceCommandDispatcher dispatcher) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer,Viewer")] // not Customer: integration detail
    public async Task<ActionResult<DeviceConfigurationDto>> Get(Guid deviceId, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var config = await configurations.GetForDeviceAsync(deviceId, ct)
                     ?? new DeviceConfiguration { TenantId = device.TenantId, DeviceId = deviceId };

        return Ok(Map(config));
    }

    [HttpPut]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<DeviceConfigurationDto>> Update(Guid deviceId, UpdateDeviceConfigurationRequest request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var config = new DeviceConfiguration
        {
            TenantId = device.TenantId,
            DeviceId = deviceId,
            ReportingIntervalSeconds = request.ReportingIntervalSeconds,
            HeartbeatIntervalSeconds = request.HeartbeatIntervalSeconds,
            TemperatureThreshold = request.TemperatureThreshold
        };

        var saved = await configurations.UpsertAsync(config, ct);
        await dispatcher.SendConfigurationAsync(device, saved, ct);

        return Ok(Map(saved));
    }

    private static DeviceConfigurationDto Map(DeviceConfiguration c) =>
        new(c.DeviceId, c.Version, c.ReportingIntervalSeconds, c.HeartbeatIntervalSeconds, c.TemperatureThreshold, c.AcknowledgedAt);
}
