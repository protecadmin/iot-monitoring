using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>Site-level gateway hardware — mirrors SitesController's shape/authorization exactly.</summary>
[ApiController]
[Route("api/gateways")]
[Authorize]
public sealed class GatewaysController(IGatewayRepository gateways, IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer,Viewer")] // not Customer: integration detail
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
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<IReadOnlyList<GatewayAvailableDto>>> Available([FromQuery] Guid? tenantId, [FromQuery] Guid? siteId, CancellationToken ct)
    {
        var result = await gateways.ListAsync(EffectiveTenantFilter(tenantId), siteId, new PageRequest { Page = 1, PageSize = 500 }, ct);
        return Ok(result.Items.Select(g => new GatewayAvailableDto(g.Id, g.GatewayCode, g.Name, g.Status.ToString(), g.TenantId, g.SiteId)).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer,Viewer")] // not Customer: integration detail
    public async Task<ActionResult<GatewayDto>> GetById(Guid id, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var gateway = await gateways.GetByIdAsync(effectiveTenantId, id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        return Ok(Map(gateway));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
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
            Name = request.Name,
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
    [Authorize(Roles = "SuperAdmin,Admin,Engineer")]
    public async Task<ActionResult<GatewayDto>> Update(Guid id, [FromQuery] Guid tenantId, UpdateGatewayRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var gateway = await gateways.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        gateway.Name = request.Name;
        gateway.IpAddress = request.IpAddress;
        gateway.FirmwareVersion = request.FirmwareVersion;
        gateway.Rssi = request.Rssi;
        gateway.BatteryPct = request.BatteryPct;
        await gateways.UpdateAsync(gateway, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "Gateway.Update",
            EntityType = nameof(Gateway),
            EntityId = gateway.Id.ToString()
        }, ct);
        return Ok(Map(gateway));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var gateway = await gateways.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Gateway not found", "GATEWAY_NOT_FOUND");
        await gateways.DeleteAsync(gateway, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "Gateway.Delete",
            EntityType = nameof(Gateway),
            EntityId = gateway.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { gateway.Name })
        }, ct);
        return NoContent();
    }

    private static GatewayDto Map(Gateway g) => new(g.Id, g.TenantId, g.SiteId, g.Site?.Name ?? "— Unassigned —", g.Name, g.IpAddress, g.LastSeenAt,
        g.GatewayCode, g.Status.ToString(), g.LastMeasurementAt, g.FirmwareVersion, g.Rssi, g.BatteryPct, g.Devices.Select(d => d.Name).ToList());
}
