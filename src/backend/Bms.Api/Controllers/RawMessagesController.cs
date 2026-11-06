using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Application.Ingestion;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Authorize]
public sealed class RawMessagesController(IRawMessageRepository rawMessages, IDeviceRepository devices, IIngestionPipeline pipeline) : BmsControllerBase
{
    [HttpGet("api/devices/{deviceId:guid}/raw-messages")]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<PagedResult<RawMessageDto>>> ListForDevice(
        Guid deviceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var device = await devices.GetByIdAsync(deviceId, ct) ?? throw ApiException.NotFound("Device not found", "DEVICE_NOT_FOUND");
        RequireTenantAccess(device.TenantId);

        var result = await rawMessages.ListForDeviceAsync(deviceId, new PageRequest { Page = page, PageSize = pageSize }, ct);
        return Ok(new PagedResult<RawMessageDto>
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    /// <summary>
    /// Re-runs a stored raw message through decode -> normalize -> validate ->
    /// persist — most useful right after adding a missing parameter to the
    /// device type, for messages whose fields were previously dropped. See
    /// docs/troubleshooting/common-issues.md and core.txt section 51.
    /// Restricted to Engineer+ since it can (re-)write telemetry.
    /// </summary>
    [HttpPost("api/raw-messages/{id:guid}/replay")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<RawMessageDto>> Replay(Guid id, CancellationToken ct)
    {
        var original = await rawMessages.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Raw message not found", "RAW_MESSAGE_NOT_FOUND");
        RequireTenantAccess(original.TenantId);

        var replay = new RawDeviceMessage
        {
            TenantId = original.TenantId,
            SiteId = original.SiteId,
            DeviceId = original.DeviceId,
            Transport = original.Transport,
            Protocol = original.Protocol,
            Topic = original.Topic,
            Endpoint = original.Endpoint,
            PayloadFormat = original.PayloadFormat,
            RawPayload = original.RawPayload,
            ReceivedAt = DateTimeOffset.UtcNow,
            DeviceTimestamp = original.DeviceTimestamp,
            CorrelationId = Guid.NewGuid()
        };

        await pipeline.ProcessAsync(replay, ct);
        return Ok(Map(replay));
    }

    private static RawMessageDto Map(RawDeviceMessage m) => new(
        m.Id, m.DeviceId, m.Transport.ToString(), m.Protocol.ToString(), m.Topic, m.Endpoint,
        m.PayloadFormat.ToString(), Convert.ToBase64String(m.RawPayload), m.ReceivedAt, m.DeviceTimestamp,
        m.CorrelationId, m.ProcessingStatus.ToString(), m.ProcessingError);
}
