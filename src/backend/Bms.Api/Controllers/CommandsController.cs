using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Security;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/devices/{deviceId:guid}/commands")]
[Authorize]
public sealed class CommandsController(
    IDeviceCommandRepository commands,
    IDeviceRepository devices,
    IDeviceTypeRepository deviceTypes,
    IDeviceCommandDispatcher dispatcher,
    IRealtimeNotifier notifier) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<IReadOnlyList<DeviceCommandDto>>> List(Guid deviceId, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        var list = await commands.ListForDeviceAsync(deviceId, ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<DeviceCommandDto>> Create(Guid deviceId, CreateCommandRequest request, CancellationToken ct)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);
        if (await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct) is { SupportsCommands: false } type)
            throw ApiException.BadRequest($"{type.Name} devices can't receive commands", "DEVICE_COMMANDS_UNSUPPORTED");

        var command = new DeviceCommand
        {
            TenantId = device.TenantId,
            DeviceId = deviceId,
            Type = request.Type,
            PayloadJson = request.PayloadJson,
            Status = CommandStatus.Pending,
            IssuedByUserId = CurrentUserId
        };
        await commands.AddAsync(command, ct);

        try
        {
            await dispatcher.SendCommandAsync(device, command, ct);
            command.Status = CommandStatus.Sent;
            command.SentAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            command.Error = ex.Message;
        }

        await commands.UpdateAsync(command, ct);
        await notifier.CommandStatusChangedAsync(command, ct);

        return Ok(Map(command));
    }

    private static DeviceCommandDto Map(DeviceCommand c) =>
        new(c.Id, c.DeviceId, c.Type.ToString(), c.Status.ToString(), c.CreatedAt, c.SentAt, c.CompletedAt, c.Error);
}
