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

/// <summary>Site-level gateway hardware — mirrors SitesController's shape/authorization exactly.</summary>
[ApiController]
[Route("api/gateways")]
[Authorize]
public sealed class GatewaysController(IGatewayRepository gateways, IAuditLogRepository auditLog, MqttTransportAdapter mqtt) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<PagedResult<GatewayDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] GatewayStatus? status = null, CancellationToken ct = default)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var result = await gateways.ListAsync(effectiveTenantId, siteId, new PageRequest { Page = page, PageSize = pageSize, Search = search }, ct);
        var gatewayItems = status is { } selected
            ? result.Items.Where(g => g.Status == selected).ToList()
            : result.Items.ToList();

        return Ok(new PagedResult<GatewayDto>
        {
            Items = gatewayItems.Select(Map).ToList(),
            TotalCount = status is null ? result.TotalCount : gatewayItems.Count,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("available")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<GatewayAvailableDto>>> Available([FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, CancellationToken ct)
    {
        var result = await gateways.ListAsync(EffectiveTenantFilter(tenantId), siteId, new PageRequest { Page = 1, PageSize = 500 }, ct);
        return Ok(result.Items.Select(g => new GatewayAvailableDto(g.Id, g.GatewayCode, g.Name, g.Status.ToString(), g.TenantId, g.SiteId)).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")] // not Customer: integration detail
    public async Task<ActionResult<GatewayDto>> GetById(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var gateway = await gateways.GetByIdAsync(effectiveTenantId, id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        return Ok(Map(gateway));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<GatewayDto>> Create([FromQuery] Guid? tenantId, CreateGatewayRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        if (string.IsNullOrWhiteSpace(request.GatewayCode))
            throw ApiException.BadRequest("GatewayCode is required", "GATEWAY_CODE_REQUIRED");
        var gateway = new Gateway
        {
            TenantId = tenantId,
            SiteId = request.SiteId,
            GatewayCode = request.GatewayCode.Trim(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.GatewayCode! : request.Name.Trim(),
            Status = GatewayStatus.Standalone,
            IpAddress = request.IpAddress,
            FirmwareVersion = request.FirmwareVersion,
            Rssi = request.Rssi,
            BatteryPct = request.BatteryPct
        };
        await gateways.AddAsync(gateway, ct);
        if (tenantId is { } auditTenantId)
        {
            await auditLog.AddAsync(new AuditLog
            {
                TenantId = auditTenantId,
                UserId = CurrentUserId,
                Action = "Gateway.Create",
                EntityType = nameof(Gateway),
                EntityId = gateway.Id.ToString(),
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { gateway.Name, gateway.SiteId })
            }, ct);
        }

        var created = await gateways.GetByIdAsync(tenantId, gateway.Id, ct) ?? gateway;
        return CreatedAtAction(nameof(GetById), new { id = gateway.Id, tenantId }, Map(created));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<GatewayDto>> Update(Guid id, [FromQuery] Guid? tenantId, UpdateGatewayRequest request, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var gateway = await gateways.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        gateway.Name = request.Name;
        gateway.IpAddress = request.IpAddress;
        gateway.FirmwareVersion = request.FirmwareVersion;
        gateway.Rssi = request.Rssi;
        gateway.BatteryPct = request.BatteryPct;
        await gateways.UpdateAsync(gateway, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = gateway.TenantId,
            UserId = CurrentUserId,
            Action = "Gateway.Update",
            EntityType = nameof(Gateway),
            EntityId = gateway.Id.ToString()
        }, ct);
        return Ok(Map(gateway));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var gateway = await gateways.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        await gateways.DeleteAsync(gateway, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = gateway.TenantId,
            UserId = CurrentUserId,
            Action = "Gateway.Delete",
            EntityType = nameof(Gateway),
            EntityId = gateway.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { gateway.Name })
        }, ct);
        return NoContent();
    }

    /// <summary>Send one of the gateway's documented control requests.</summary>
    [HttpPost("{id:guid}/commands")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Command(Guid id, [FromQuery] Guid? tenantId, GatewayCommandRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Op) || request.Op.Length > 64)
            throw ApiException.BadRequest("A gateway command operation is required", "GATEWAY_COMMAND_REQUIRED");
        if (request.Op is not ("get_capabilities" or "get_config" or "get_status" or "get_latest" or "get_history" or "get_logs" or "recover_cellular" or "set_config"))
            throw ApiException.BadRequest("Unsupported gateway command", "GATEWAY_COMMAND_UNSUPPORTED");

        if (tenantId is { } requestedTenant) RequireTenantAccess(requestedTenant);
        var gateway = await gateways.GetByIdAsync(EffectiveTenantFilter(tenantId), id, ct)
            ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");

        var envelope = new Dictionary<string, object?> { ["v"] = 1, ["id"] = Guid.NewGuid().ToString("N"), ["op"] = request.Op };
        if (request.Payload.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            envelope["payload"] = request.Payload;
        var topic = $"protec/gateway/{gateway.GatewayCode}/command";
        await mqtt.SendAsync(topic, JsonSerializer.SerializeToUtf8Bytes(envelope), ct);
        await auditLog.AddAsync(new AuditLog { TenantId = gateway.TenantId, UserId = CurrentUserId, Action = "Gateway.Command", EntityType = nameof(Gateway), EntityId = gateway.Id.ToString(), DetailsJson = JsonSerializer.Serialize(new { request.Op, topic }) }, ct);
        return Accepted(new { topic, operation = request.Op });
    }

    private static GatewayDto Map(Gateway g)
    {
        var mappedDevice = g.Devices.FirstOrDefault();
        var tenantId = g.TenantId ?? mappedDevice?.TenantId;
        var siteId = g.SiteId ?? mappedDevice?.SiteId;
        var siteName = g.Site?.Name ?? mappedDevice?.Site?.Name ?? "— Unassigned —";
        var tenantName = g.Tenant?.Name ?? mappedDevice?.Site?.Tenant?.Name;

        return new GatewayDto(g.Id, tenantId, siteId, siteName, g.Name, g.IpAddress, g.LastSeenAt,
            g.GatewayCode, g.Status.ToString(), g.LastMeasurementAt, g.FirmwareVersion, g.Rssi, g.BatteryPct,
            g.Devices.Select(d => d.Name).ToList(), tenantName);
    }
}

public sealed record GatewayCommandRequest(string Op, JsonElement Payload);
