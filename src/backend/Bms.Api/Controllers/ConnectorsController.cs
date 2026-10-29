using System.Globalization;
using System.Text;
using Bms.Api.Common;
using Bms.Api.Connectors;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Connectors.Core;
using Bms.Domain.Connectors;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bms.Api.Controllers;

/// <summary>
/// Connectors: how devices that don't speak BMS's MQTT format are read.
/// Managed by a SuperAdmin; Admins and Engineers can list them to pick one
/// when adding a device.
/// </summary>
[ApiController]
[Route("api/connectors")]
[Authorize]
public sealed class ConnectorsController(
    IConnectorRepository connectors,
    IAuditLogRepository auditLog,
    IOptions<ConnectorOptions> options) : BmsControllerBase
{
    private ConnectorOptions Options => options.Value;

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<IReadOnlyList<ConnectorDto>>> List(CancellationToken ct)
    {
        var list = await connectors.ListAsync(ct);
        var devices = await connectors.CountDevicesAsync(ct);
        var unmapped = (await connectors.ListUnmappedKeysAsync(null, ct)).GroupBy(u => u.ConnectorId).ToDictionary(g => g.Key, g => g.Count());
        return Ok(list.Select(c => ConnectorMapper.ToDto(c, devices.GetValueOrDefault(c.Id), unmapped.GetValueOrDefault(c.Id))).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<ConnectorDto>> Get(Guid id, CancellationToken ct) =>
        Ok(ConnectorMapper.ToDto(await FindAsync(id, ct)));

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ConnectorDto>> Create(SaveConnectorRequest request, CancellationToken ct)
    {
        var connector = new Connector();
        ConnectorMapper.Apply(request, connector);
        await ValidateAsync(connector, null, ct);
        await connectors.AddAsync(connector, ct);
        await AuditAsync("Connector.Create", connector, ct);
        return Ok(ConnectorMapper.ToDto(connector));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ConnectorDto>> Update(Guid id, SaveConnectorRequest request, CancellationToken ct)
    {
        var connector = await FindAsync(id, ct);
        var oldPrefix = connector.DeviceCodePrefix;
        ConnectorMapper.Apply(request, connector);
        // Device codes are prefix + sensor ID and are used in MQTT topics, so
        // the prefix is fixed once devices use it.
        var devices = (await connectors.CountDevicesAsync(ct)).GetValueOrDefault(id);
        if (devices > 0 && !string.Equals(oldPrefix, connector.DeviceCodePrefix, StringComparison.Ordinal))
            throw ApiException.BadRequest($"The device code prefix can't change while {devices} device(s) use this connector", "CONNECTOR_PREFIX_IN_USE");

        await ValidateAsync(connector, id, ct);
        await connectors.UpdateAsync(connector, ct);
        // Keys now mapped are no longer "unmapped".
        await connectors.DeleteUnmappedKeysAsync(id, connector.Mappings.Select(m => m.Source).ToList(), ct);
        await AuditAsync("Connector.Update", connector, ct);
        return Ok(ConnectorMapper.ToDto(connector, devices));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var connector = await FindAsync(id, ct);
        var devices = (await connectors.CountDevicesAsync(ct)).GetValueOrDefault(id);
        if (devices > 0)
            throw ApiException.Conflict($"{devices} device(s) still use this connector. Move or delete them first, or disable the connector instead.", "CONNECTOR_IN_USE");
        await connectors.DeleteAsync(connector, ct);
        await AuditAsync("Connector.Delete", connector, ct);
        return NoContent();
    }

    /// <summary>
    /// Decodes a sample with a connector as drafted in the form — nothing is
    /// saved. Returns every value found (the rows to map) and the reading
    /// BMS would receive, so a mapping is checked before it goes live.
    /// </summary>
    [HttpPost("test")]
    [Authorize(Roles = "SuperAdmin")]
    public ActionResult<ConnectorTestResult> Test(TestConnectorRequest request)
    {
        var connector = new Connector();
        ConnectorMapper.Apply(request.Connector, connector);
        var validation = ConnectorValidator.Validate(connector, Options.PortRangeStart, Options.PortRangeEnd);

        byte[] bytes;
        try
        {
            bytes = request.SampleIsHex
                ? Convert.FromHexString(new string((request.Sample ?? "").Where(Uri.IsHexDigit).ToArray()))
                : Encoding.UTF8.GetBytes(request.Sample ?? "");
        }
        catch (FormatException)
        {
            throw ApiException.BadRequest("The sample isn't valid hex", "CONNECTOR_SAMPLE_INVALID");
        }

        // The first message in the sample, cut exactly as the live service would.
        var framer = new MessageFramer(connector);
        var message = framer.Feed(bytes).FirstOrDefault() ?? framer.Flush();
        if (message is null)
            return Ok(new ConnectorTestResult([], null, null, new Dictionary<string, double>(), null, [], "The sample is empty", validation));

        var decoded = MessageDecoder.Decode(connector, message.Bytes);
        var values = decoded.Values
            .OrderBy(v => int.TryParse(v.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .Select(v => new ConnectorSampleValue(v.Key, v.Value))
            .ToList();
        var error = message.Oversize ? $"Message is longer than {connector.MaxMessageBytes} bytes" : message.Incomplete ? "The sample is shorter than one message" : decoded.Error;
        var deviceCode = decoded.SensorId is { Length: > 0 } id ? connector.DeviceCodePrefix + id : null;
        return Ok(new ConnectorTestResult(values, decoded.SensorId, deviceCode, decoded.Fields, decoded.Time, decoded.UnmappedSources, error, validation));
    }

    /// <summary>Asks the Connector Service for the next real message on this connector's port.</summary>
    [HttpPost("{id:guid}/capture")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ConnectorCaptureDto>> Capture(Guid id, CancellationToken ct)
    {
        var connector = await FindAsync(id, ct);
        var now = DateTimeOffset.UtcNow;
        var capture = new ConnectorCapture { ConnectorId = connector.Id, RequestedAt = now, ExpiresAt = now.AddSeconds(Options.CaptureWaitSeconds) };
        await connectors.AddCaptureAsync(capture, ct);
        return Ok(Map(capture));
    }

    [HttpGet("captures/{captureId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ConnectorCaptureDto>> GetCapture(Guid captureId, CancellationToken ct) =>
        Ok(Map(await connectors.GetCaptureAsync(captureId, ct) ?? throw ApiException.NotFound("Capture not found", "CONNECTOR_CAPTURE_NOT_FOUND")));

    [HttpGet("unknown-devices")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<IReadOnlyList<ConnectorUnknownDeviceDto>>> UnknownDevices(CancellationToken ct)
    {
        var byId = (await connectors.ListAsync(ct)).ToDictionary(c => c.Id);
        var list = await connectors.ListUnknownDevicesAsync(ct);
        return Ok(list.Where(u => byId.ContainsKey(u.ConnectorId)).Select(u => new ConnectorUnknownDeviceDto(
            u.ConnectorId, byId[u.ConnectorId].Name, u.SensorId, byId[u.ConnectorId].DeviceCodePrefix + u.SensorId,
            u.FirstSeenAt, u.LastSeenAt, u.MessageCount, u.LastSample)).ToList());
    }

    /// <summary>Dismisses an unknown sensor ID (it comes back if it keeps sending).</summary>
    [HttpDelete("{id:guid}/unknown-devices/{sensorId}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DismissUnknown(Guid id, string sensorId, CancellationToken ct)
    {
        await connectors.DeleteUnknownDeviceAsync(id, sensorId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/unmapped-keys")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<ConnectorUnmappedKeyDto>>> UnmappedKeys(Guid id, CancellationToken ct) =>
        Ok((await connectors.ListUnmappedKeysAsync(id, ct)).Select(u => new ConnectorUnmappedKeyDto(u.ConnectorId, u.Source, u.MessageCount, u.LastSeenAt, u.LastValue)).ToList());

    [HttpGet("service-status")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ConnectorServiceStatusDto>> ServiceStatus(CancellationToken ct)
    {
        var s = await connectors.GetStatusAsync(ct);
        var online = s is not null && DateTimeOffset.UtcNow - s.LastHeartbeatAt < TimeSpan.FromSeconds(Options.OfflineAfterSeconds);
        return Ok(new ConnectorServiceStatusDto(
            online, s?.LastHeartbeatAt, s?.Version, s?.MessagesAccepted ?? 0, s?.MessagesRejected ?? 0, s?.MessagesUnknown ?? 0,
            s?.MessagesPublished ?? 0, s?.LastError, s?.ConnectorsJson, Options.PortRangeStart, Options.PortRangeEnd));
    }

    private async Task ValidateAsync(Connector connector, Guid? id, CancellationToken ct)
    {
        var errors = ConnectorValidator.Validate(connector, Options.PortRangeStart, Options.PortRangeEnd).ToList();
        if (await connectors.PortTakenAsync(connector.Protocol, connector.Port, id, ct))
            errors.Add($"Another connector already listens on {connector.Protocol} port {connector.Port}");
        if (await connectors.PrefixTakenAsync(connector.DeviceCodePrefix, id, ct))
            errors.Add($"Another connector already uses the prefix \"{connector.DeviceCodePrefix}\"");
        if (errors.Count > 0)
            throw ApiException.BadRequest(string.Join(". ", errors), "CONNECTOR_INVALID");
    }

    private async Task<Connector> FindAsync(Guid id, CancellationToken ct) =>
        await connectors.GetAsync(id, ct) ?? throw ApiException.NotFound("Connector not found", "CONNECTOR_NOT_FOUND");

    private Task AuditAsync(string action, Connector c, CancellationToken ct) =>
        auditLog.AddAsync(new AuditLog
        {
            UserId = CurrentUserId,
            Action = action,
            EntityType = nameof(Connector),
            EntityId = c.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { c.Name, c.Protocol, c.Port, c.Format, c.DeviceCodePrefix, c.IsEnabled })
        }, ct);

    private static ConnectorCaptureDto Map(ConnectorCapture c) => new(c.Id, c.ConnectorId, c.RequestedAt, c.ExpiresAt, c.CapturedAt, c.Sample, c.SampleIsHex);
}
