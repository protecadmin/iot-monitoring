using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Bms.Protocols.Transports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bms.Protocols.Configuration;
using Microsoft.Extensions.Options;

namespace Bms.Api.Controllers;

/// <summary>PRO-TEC HUB hardware fleet management and remote configuration.</summary>
[ApiController]
[Route("api/hubs")]
[Authorize]
public sealed class HubsController(IHubRepository hubs, IAuditLogRepository auditLog, MqttTransportAdapter mqtt, IConfiguration configuration, IOptions<MqttOptions> mqttOptions) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<PagedResult<HubDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] HubStatus? status = null, CancellationToken ct = default)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var result = await hubs.ListAsync(effectiveTenantId, siteId, new PageRequest { Page = page, PageSize = pageSize, Search = search }, ct);
        var items = status is { } selected
            ? result.Items.Where(h => h.Status == selected).ToList()
            : result.Items.ToList();

        return Ok(new PagedResult<HubDto>
        {
            Items = items.Select(Map).ToList(),
            TotalCount = status is null ? result.TotalCount : items.Count,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<HubDto>> GetById(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var hub = await hubs.GetByIdAsync(effectiveTenantId, id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        return Ok(Map(hub));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<HubDto>> Create([FromQuery] Guid? tenantId, CreateHubRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        if (string.IsNullOrWhiteSpace(request.HubId))
            throw ApiException.BadRequest("HubId is required", "HUB_ID_REQUIRED");

        var cleanHubId = request.HubId.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(cleanHubId, "^[a-f0-9]{12}$"))
            throw ApiException.BadRequest("HUB ID must contain 12 hexadecimal characters.", "HUB_ID_INVALID");
        var existing = await hubs.GetByHubIdAsync(cleanHubId, ct);
        if (existing is not null)
            throw ApiException.BadRequest($"HUB with ID '{cleanHubId}' already exists", "HUB_ALREADY_EXISTS");

        var hub = new Hub
        {
            TenantId = tenantId,
            SiteId = request.SiteId,
            HubId = cleanHubId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"HUB-{cleanHubId}" : request.Name.Trim(),
            Status = HubStatus.Standalone,
            IpAddress = request.IpAddress,
            AppVersion = request.AppVersion,
            AgentVersion = request.AgentVersion,
            SecurityMode = request.SecurityMode ?? "tls",
            DisplayMode = "overview",
            RotationSeconds = 20,
            WelcomeSeconds = 7,
            HistoryRange = "1h"
        };
        await hubs.AddAsync(hub, ct);

        if (tenantId is { } auditTenantId)
        {
            await auditLog.AddAsync(new AuditLog
            {
                TenantId = auditTenantId,
                UserId = CurrentUserId,
                Action = "Hub.Create",
                EntityType = nameof(Hub),
                EntityId = hub.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { hub.HubId, hub.Name, hub.SiteId })
            }, ct);
        }

        var created = await hubs.GetByIdAsync(tenantId, hub.Id, ct) ?? hub;
        return CreatedAtAction(nameof(GetById), new { id = hub.Id, tenantId }, Map(created));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<HubDto>> Update(Guid id, [FromQuery] Guid? tenantId, UpdateHubRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");

        hub.Name = request.Name.Trim();
        hub.SiteId = request.SiteId;
        if (request.IpAddress is not null) hub.IpAddress = request.IpAddress;
        if (request.AppVersion is not null) hub.AppVersion = request.AppVersion;
        if (request.AgentVersion is not null) hub.AgentVersion = request.AgentVersion;
        if (request.SecurityMode is not null) hub.SecurityMode = request.SecurityMode;

        await hubs.UpdateAsync(hub, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = hub.TenantId,
            UserId = CurrentUserId,
            Action = "Hub.Update",
            EntityType = nameof(Hub),
            EntityId = hub.Id.ToString()
        }, ct);
        return Ok(Map(hub));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        await hubs.DeleteAsync(hub, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = hub.TenantId,
            UserId = CurrentUserId,
            Action = "Hub.Delete",
            EntityType = nameof(Hub),
            EntityId = hub.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { hub.HubId, hub.Name })
        }, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/commands")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Commands(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        _ = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        return Ok(await hubs.GetCommandsAsync(id, ct));
    }

    [HttpGet("{id:guid}/monitor")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Monitor(Guid id, [FromQuery] Guid? tenantId, [FromQuery] DateTimeOffset? dayStart, CancellationToken ct)
    {
        _ = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        var start = dayStart ?? new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var monitor = await hubs.GetMonitorAsync(id, start, ct);
        return Ok(new { monitor.Messages, monitor.CommandsToday, monitor.SuccessRate,
            broker = new { connected = mqtt.IsConnected, tls = mqttOptions.Value.UseTls, host = mqttOptions.Value.Host, port = mqttOptions.Value.Port } });
    }

    [HttpPost("{id:guid}/commands")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Command(Guid id, [FromQuery] Guid? tenantId, HubCommandRequest request, CancellationToken ct)
    {
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct)
            ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        var commandId = request.RequestId ?? $"cmd-{Guid.NewGuid():N}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(commandId, "^cmd-[a-f0-9]{32}$"))
            throw ApiException.BadRequest("Invalid request ID.", "HUB_INVALID_REQUEST_ID");
        JsonObject envelope;
        JsonObject data;
        try
        {
            data = JsonSerializer.SerializeToNode(request.Arguments ?? new Dictionary<string, object?>())!.AsObject();
            envelope = HubProtocol.Build(request.Action, data, request.IfVersion, commandId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException)
        {
            throw ApiException.BadRequest(ex.Message, "HUB_INVALID_COMMAND");
        }
        var payloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
        var topic = $"protec/hubs/{hub.HubId}/config";
        // Resolve retries before checking the current version: a completed write may already have advanced it.
        var savedCommand = await hubs.GetCommandAsync(hub.Id, commandId, ct);
        if (savedCommand is not null)
        {
            if (savedCommand.PayloadHash != payloadHash) throw ApiException.BadRequest("Request ID already used for different parameters.", "HUB_DUPLICATE_ID");
            return Accepted(new { topic, action = savedCommand.Action, commandId, state = savedCommand.State, deadline = savedCommand.Deadline, duplicate = true });
        }
        var snapshot = ParseSnapshot(hub);
        if (!HubProtocol.IsReadOnly(request.Action))
        {
            if (!ApplyEnabled(hub)) throw ApiException.BadRequest("Remote apply is not verified for this HUB. Enable apply on the Pi, pass acceptance checks, then configure HubManagement:ApplyEnabledHubIds on the server.", "HUB_APPLY_DISABLED");
            if (snapshot?["v"]?.GetValue<int>() != 2 || snapshot["config_version"]?.GetValue<int>() != request.IfVersion)
                throw ApiException.BadRequest("Configuration version changed or no V2 snapshot is available. Request a fresh snapshot and review the form.", "HUB_VERSION_CONFLICT");
            if (hub.LastSeenAt is null || hub.LastSeenAt < DateTimeOffset.UtcNow.AddMinutes(-5) || hub.LastHeartbeatAt is null || hub.LastHeartbeatAt < DateTimeOffset.UtcNow.AddMinutes(-5) || hub.Status != HubStatus.Online
                || !DateTimeOffset.TryParse(snapshot["time_utc"]?.GetValue<string>(), out var generatedAt) || generatedAt < DateTimeOffset.UtcNow.AddMinutes(-5))
                throw ApiException.BadRequest("HUB is offline or stale. Refresh state before applying changes.", "HUB_OFFLINE");
            if (request.Action == "tv.select")
            {
                var inventory = snapshot["devices"] as JsonArray ?? [];
                foreach (var sensor in data["sensors"]!.AsArray())
                {
                    var matches = inventory.Where(d => d?["source"]?.ToJsonString() == sensor!["source"]?.ToJsonString() && d?["site"]?.ToJsonString() == sensor!["site"]?.ToJsonString() && d?["sensor"]?.ToJsonString() == sensor!["sensor"]?.ToJsonString()).ToList();
                    if (matches.Count != 1 || matches[0]?["age_seconds"] is not JsonValue age || !age.TryGetValue<double>(out var seconds) || seconds + (DateTimeOffset.UtcNow - generatedAt).TotalSeconds > 18000)
                        throw ApiException.BadRequest("Unknown, ambiguous or older-than-five-hours sensor. Review current inventory.", "HUB_SENSOR_UNAVAILABLE");
                }
            }
        }
        var now = DateTimeOffset.UtcNow;
        // Keep the actor and deadline durably before publishing. Never store logo bytes in audit history.
        var journalData = (JsonObject)data.DeepClone();
        if (request.Action == "logo.chunk") journalData.Remove("base64");
        if (request.Action == "logo.commit") journalData["previous_logo_revision"] = snapshot?["branding"]?["client_logo_revision"]?.DeepClone();
        var receipt = new HubCommandReceipt(commandId, request.Action, "pending", now, now.AddMinutes(2), request.IfVersion, journalData, PayloadHash: payloadHash, Payload: HubProtocol.Redact(envelope.ToJsonString()));
        if (!await hubs.RecordCommandAsync(hub.Id, hub.TenantId, CurrentUserId, receipt, ct))
        {
            var original = await hubs.GetCommandAsync(hub.Id, commandId, ct);
            if (original?.PayloadHash != payloadHash) throw ApiException.BadRequest("Request ID already used. Allocate a new ID for changed parameters.", "HUB_DUPLICATE_ID");
            return Accepted(new { topic, action = original.Action, commandId, state = original.State, deadline = original.Deadline, duplicate = true });
        }
        try { await mqtt.PublishAsync(topic, JsonSerializer.SerializeToUtf8Bytes(envelope), retain: false, ct, expirySeconds: 120); }
        catch
        {
            await hubs.MarkPublishFailureAsync(hub.Id, commandId, CancellationToken.None);
            throw;
        }
        return Accepted(new { topic, action = request.Action, commandId, state = "pending", deadline = receipt.Deadline });
    }

    [HttpGet("{id:guid}/commands/prepare")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Prepare(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        _ = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        return Ok(new { requestId = $"cmd-{Guid.NewGuid():N}" });
    }

    // Preserve the HTTP entry point while translating supported timing fields to V2.
    // RC1 has no operation for changing display mode or history range.
    [HttpPost("{id:guid}/config/desired")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetDesiredConfig(Guid id, [FromQuery] Guid? tenantId, HubDesiredConfigRequest request, CancellationToken ct)
    {
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct)
            ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        if ((!string.IsNullOrWhiteSpace(request.DisplayMode) && request.DisplayMode != hub.DisplayMode)
            || (!string.IsNullOrWhiteSpace(request.HistoryRange) && request.HistoryRange != hub.HistoryRange))
            throw ApiException.BadRequest("The installed RC1 contract supports timing changes through display.set; changing display mode or history range is unsupported.", "HUB_UNSUPPORTED_SETTING");
        var data = new Dictionary<string, object?>
        {
            ["welcome_seconds"] = request.WelcomeSeconds ?? hub.WelcomeSeconds,
            ["rotation_seconds"] = request.RotationSeconds ?? hub.RotationSeconds
        };
        var result = await Command(id, tenantId, new HubCommandRequest("display.set", data, hub.ConfigRevision), ct);
        if (result is not AcceptedResult accepted) return result;
        var command = JsonSerializer.SerializeToElement(accepted.Value);
        var desiredRevision = hub.ConfigRevision + 1;
        await hubs.UpdateDesiredConfigAsync(hub.Id, desiredRevision, JsonSerializer.Serialize(new { operation = "display.set", if_version = hub.ConfigRevision, data, state = "pending" }), ct);
        return Accepted(new { topic = command.GetProperty("topic").GetString(), requestId = command.GetProperty("commandId").GetString(), desiredRevision, patch = data, state = "pending" });
    }

    private static JsonObject? ParseSnapshot(Hub hub)
    {
        try { return hub.LastStatusJson is { } json ? JsonNode.Parse(json) as JsonObject : null; }
        catch (JsonException) { return null; }
    }

    private bool ApplyEnabled(Hub hub) => configuration.GetSection("HubManagement:ApplyEnabledHubIds").Get<string[]>()?.Contains(hub.HubId, StringComparer.OrdinalIgnoreCase) == true
        && ParseSnapshot(hub)?["capabilities"]?["allow_apply"]?.GetValue<bool>() != false;

    private HubDto Map(Hub h)
    {
        var siteName = h.Site?.Name ?? "— Unassigned —";
        var tenantName = h.Tenant?.Name ?? h.Site?.Tenant?.Name;

        return new HubDto(
            h.Id,
            h.TenantId,
            tenantName,
            h.SiteId,
            siteName,
            h.HubId,
            h.Name,
            h.Status == HubStatus.Online && (h.LastSeenAt is null || h.LastSeenAt < DateTimeOffset.UtcNow.AddMinutes(-5)) ? "Stale" : h.Status.ToString(),
            h.IpAddress,
            h.AppVersion,
            h.AgentVersion,
            h.SecurityMode,
            h.Rssi,
            h.UptimeSeconds,
            h.ConfigRevision,
            h.ConfigHash,
            h.DisplayMode,
            h.RotationSeconds,
            h.WelcomeSeconds,
            h.HistoryRange,
            h.LastSeenAt,
            h.LastHeartbeatAt,
            h.LastMeasurementAt,
            h.LastConfigSyncAt,
            h.DesiredConfigJson,
            h.ReportedConfigJson,
            h.LastStatusJson,
            ApplyEnabled(h)
        );
    }
}

