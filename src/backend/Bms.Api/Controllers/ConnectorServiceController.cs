using Bms.Api.Connectors;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Connectors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// What the Connector Service reads and reports. It polls the config (connectors
/// + the devices they deliver for) and reports what it saw: heartbeats, sensor
/// IDs no device matches, message parts no mapping uses, and captured samples.
/// </summary>
[ApiController]
[Route("api/connector-service")]
[AllowAnonymous]
[ConnectorServiceKey]
public sealed class ConnectorServiceController(IConnectorRepository connectors) : ControllerBase
{
    [HttpGet("config")]
    public async Task<ActionResult<ConnectorServiceConfigDto>> Config(CancellationToken ct)
    {
        var list = (await connectors.ListAsync(ct)).Where(c => c.IsEnabled).ToList();
        var devices = await connectors.ListDeviceEntriesAsync(ct);
        return Ok(new ConnectorServiceConfigDto(
            list.Select(c => ConnectorMapper.ToDto(c)).ToList(),
            devices.Select(d => new ConnectorServiceDeviceDto(d.ConnectorId, d.SensorId, d.DeviceCode, d.TenantSlug, d.SiteCode)).ToList(),
            DateTimeOffset.UtcNow));
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(ConnectorHeartbeatRequest request, CancellationToken ct)
    {
        var status = await connectors.GetStatusAsync(ct) ?? new ConnectorServiceStatus();
        status.LastHeartbeatAt = DateTimeOffset.UtcNow;
        status.Version = request.Version;
        status.MessagesAccepted = request.MessagesAccepted;
        status.MessagesRejected = request.MessagesRejected;
        status.MessagesUnknown = request.MessagesUnknown;
        status.MessagesPublished = request.MessagesPublished;
        status.LastError = request.LastError is { Length: > 1024 } e ? e[..1024] : request.LastError;
        status.ConnectorsJson = request.ConnectorsJson;
        await connectors.SaveStatusAsync(status, ct);
        return NoContent();
    }

    [HttpPost("unknown-devices")]
    public async Task<IActionResult> UnknownDevices(IReadOnlyList<ConnectorSightingDto> sightings, CancellationToken ct)
    {
        await connectors.RecordUnknownDevicesAsync(Valid(sightings, 64), ct);
        return NoContent();
    }

    [HttpPost("unmapped-keys")]
    public async Task<IActionResult> UnmappedKeys(IReadOnlyList<ConnectorSightingDto> sightings, CancellationToken ct)
    {
        await connectors.RecordUnmappedKeysAsync(Valid(sightings, 128), ct);
        return NoContent();
    }

    [HttpGet("captures/pending")]
    public async Task<ActionResult<IReadOnlyList<ConnectorCaptureDto>>> PendingCaptures(CancellationToken ct) =>
        Ok((await connectors.ListPendingCapturesAsync(DateTimeOffset.UtcNow, ct))
            .Select(c => new ConnectorCaptureDto(c.Id, c.ConnectorId, c.RequestedAt, c.ExpiresAt, c.CapturedAt, c.Sample, c.SampleIsHex)).ToList());

    [HttpPost("captures/{id:guid}")]
    public async Task<IActionResult> CompleteCapture(Guid id, ConnectorCaptureResultRequest request, CancellationToken ct)
    {
        await connectors.CompleteCaptureAsync(id, request.Sample ?? "", request.SampleIsHex, DateTimeOffset.UtcNow, ct);
        return NoContent();
    }

    /// <summary>Drops malformed entries rather than failing the batch; caps the batch so a runaway sender can't flood the table.</summary>
    private static List<ConnectorSighting> Valid(IReadOnlyList<ConnectorSightingDto>? sightings, int maxKeyLength) =>
        (sightings ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Key) && s.Key.Length <= maxKeyLength && s.Count > 0)
            .Take(500)
            .Select(s => new ConnectorSighting(s.ConnectorId, s.Key.Trim(), s.Count, s.LastSeenAt, s.Sample))
            .ToList();
}
