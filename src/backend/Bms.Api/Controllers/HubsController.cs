using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Bms.Protocols.Transports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Bms.Api.Controllers;

/// <summary>PRO-TEC HUB hardware fleet management and remote configuration.</summary>
[ApiController]
[Route("api/hubs")]
[Authorize]
public sealed class HubsController(IHubRepository hubs, IAuditLogRepository auditLog, MqttTransportAdapter mqtt) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer,Viewer")]
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
    [Authorize(Roles = "SuperAdmin,Admin,Engineer,Viewer")]
    public async Task<ActionResult<HubDto>> GetById(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var hub = await hubs.GetByIdAsync(effectiveTenantId, id, ct) ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");
        return Ok(Map(hub));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<HubDto>> Create([FromQuery] Guid? tenantId, CreateHubRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        if (string.IsNullOrWhiteSpace(request.HubId))
            throw ApiException.BadRequest("HubId is required", "HUB_ID_REQUIRED");

        var cleanHubId = request.HubId.Trim().ToLowerInvariant();
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
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
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
    [Authorize(Roles = "SuperAdmin,Admin")]
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

    /// <summary>Send a one-time command request to the PRO-TEC HUB (Section 6).</summary>
    [HttpPost("{id:guid}/commands")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<IActionResult> Command(Guid id, [FromQuery] Guid? tenantId, HubCommandRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Action) || request.Action.Length > 64)
            throw ApiException.BadRequest("A hub command action is required", "HUB_COMMAND_ACTION_REQUIRED");

        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct)
            ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");

        var now = DateTimeOffset.UtcNow;
        // Slice the generated string, not the Guid format specifier.  Using
        // `:N[..6]` is parsed as an invalid Guid format and causes every
        // command request to fail with HTTP 500 before it is published.
        var commandNonce = Guid.NewGuid().ToString("N")[..6];
        var commandId = $"cmd-{now:yyyyMMddHHmmss}-{commandNonce}";
        var envelope = new Dictionary<string, object?>
        {
            ["schema"] = "protec.hub.command.request.v1",
            ["hub_id"] = hub.HubId,
            ["command_id"] = commandId,
            ["action"] = request.Action,
            ["arguments"] = request.Arguments ?? new Dictionary<string, object?>(),
            ["issued_at"] = now.ToString("o"),
            ["expires_at"] = now.AddMinutes(2).ToString("o")
        };

        var topic = $"protec/hubs/{hub.HubId}/command/request";
        await mqtt.SendAsync(topic, JsonSerializer.SerializeToUtf8Bytes(envelope), ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = hub.TenantId,
            UserId = CurrentUserId,
            Action = "Hub.Command",
            EntityType = nameof(Hub),
            EntityId = hub.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { request.Action, commandId, topic })
        }, ct);

        return Accepted(new { topic, action = request.Action, commandId });
    }

    /// <summary>Deploy desired configuration patch to the PRO-TEC HUB (Section 5).</summary>
    [HttpPost("{id:guid}/config/desired")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<IActionResult> SetDesiredConfig(Guid id, [FromQuery] Guid? tenantId, HubDesiredConfigRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var hub = await hubs.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct)
            ?? throw ApiException.NotFound("HUB not found", "HUB_NOT_FOUND");

        var now = DateTimeOffset.UtcNow;
        var requestNonce = Guid.NewGuid().ToString("N")[..6];
        var requestId = $"cfg-{now:yyyyMMddHHmmss}-{requestNonce}";
        var nextRevision = hub.ConfigRevision + 1;

        var patch = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.DisplayMode))
            patch["display_mode"] = request.DisplayMode;
        if (request.RotationSeconds.HasValue)
            patch["rotation_seconds"] = request.RotationSeconds.Value;
        if (request.WelcomeSeconds.HasValue)
            patch["welcome_seconds"] = request.WelcomeSeconds.Value;
        if (!string.IsNullOrWhiteSpace(request.HistoryRange))
            patch["history_range"] = request.HistoryRange;

        var envelope = new Dictionary<string, object?>
        {
            ["schema"] = "protec.hub.config.desired.v1",
            ["hub_id"] = hub.HubId,
            ["request_id"] = requestId,
            ["desired_revision"] = nextRevision,
            ["expected_reported_revision"] = hub.ConfigRevision,
            ["issued_at"] = now.ToString("o"),
            ["expires_at"] = now.AddMinutes(5).ToString("o"),
            ["patch"] = patch
        };

        var topic = $"protec/hubs/{hub.HubId}/config/desired";
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await mqtt.SendAsync(topic, jsonBytes, ct);

        var desiredJson = JsonSerializer.Serialize(envelope);
        await hubs.UpdateDesiredConfigAsync(hub.Id, nextRevision, desiredJson, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = hub.TenantId,
            UserId = CurrentUserId,
            Action = "Hub.ConfigDesired",
            EntityType = nameof(Hub),
            EntityId = hub.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { requestId, nextRevision, topic, patch })
        }, ct);

        return Accepted(new { topic, requestId, desiredRevision = nextRevision, patch });
    }

    private static HubDto Map(Hub h)
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
            h.Status.ToString(),
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
            h.LastStatusJson
        );
    }
}

