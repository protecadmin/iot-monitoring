using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public sealed class TenantsController(
    ITenantRepository tenants,
    IMqttCredentialRepository mqttCredentials,
    IMqttCredentialHasher mqttHasher,
    IReversibleEncryptor mqttEncryptor,
    IAuditLogRepository auditLog,
    Bms.Calculations.DerivedParameterCalculations derivedCalculations) : BmsControllerBase
{
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<PagedResult<TenantDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await tenants.ListAsync(new PageRequest { Page = page, PageSize = pageSize, Search = search }, ct);
        return Ok(new PagedResult<TenantDto>
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> GetById(Guid id, CancellationToken ct)
    {
        RequireTenantAccess(id);
        var tenant = await tenants.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Tenant not found", "TENANT_NOT_FOUND");
        return Ok(Map(tenant));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<TenantDto>> Create(CreateTenantRequest request, CancellationToken ct)
    {
        if (await tenants.GetBySlugAsync(request.Slug, ct) is not null)
            throw ApiException.Conflict("A tenant with this slug already exists", "TENANT_SLUG_TAKEN");

        var tenant = new Tenant { Name = request.Name, Slug = request.Slug };
        await tenants.AddAsync(tenant, ct);

        // Without this, a new tenant is "broker-blind" until an admin
        // separately visits the MQTT Credentials tab — its devices simply
        // can't connect until then. Auto-provisioning here means every
        // tenant is immediately broker-ready; the generated password stays
        // viewable afterward via GET /api/mqtt-credentials, same as any
        // manually-created or rotated one.
        var credential = MqttCredentialFactory.Create(tenant.Id, tenant.Slug, mqttHasher, mqttEncryptor);
        await mqttCredentials.AddAsync(credential, ct);

        // Same reason: the calculations defined on Derived device-type
        // parameters (activePower = voltage * current * ...) apply to every
        // tenant, so a new one gets its rules now rather than calculating
        // nothing until someone re-saves each parameter.
        await derivedCalculations.MaterializeForTenantAsync(tenant.Id, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenant.Id,
            UserId = CurrentUserId,
            Action = "Tenant.Create",
            EntityType = nameof(Tenant),
            EntityId = tenant.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { tenant.Name, tenant.Slug })
        }, ct);

        return CreatedAtAction(nameof(GetById), new { id = tenant.Id }, Map(tenant));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<TenantDto>> Update(Guid id, UpdateTenantRequest request, CancellationToken ct)
    {
        RequireTenantAccess(id);
        var tenant = await tenants.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("Tenant not found", "TENANT_NOT_FOUND");
        tenant.Name = request.Name;
        tenant.IsActive = request.IsActive;
        await tenants.UpdateAsync(tenant, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenant.Id,
            UserId = CurrentUserId,
            Action = "Tenant.Update",
            EntityType = nameof(Tenant),
            EntityId = tenant.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { tenant.Name, tenant.IsActive })
        }, ct);
        return Ok(Map(tenant));
    }

    private static TenantDto Map(Tenant t) => new(t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt);
}
