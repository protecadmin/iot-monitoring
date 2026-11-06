using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// Read-only view over AuditLog rows written by other controllers as a
/// side effect of admin actions (tenant/user/device/gateway/MQTT-credential
/// changes) — see each controller's own auditLog.AddAsync calls for what
/// gets recorded. SuperAdmin sees every tenant; Admin sees only their own.
/// </summary>
[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = "SuperAdmin")]
public sealed class AuditLogsController(IAuditLogRepository auditLog, ITenantRepository tenants, IUserRepository users) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var effectiveTenantId = IsSuperAdmin ? tenantId : (CurrentTenantId ?? throw ApiException.Forbidden("No tenant associated with this account", "NO_TENANT"));
        var result = await auditLog.ListAsync(effectiveTenantId, new PageRequest { Page = page, PageSize = pageSize, Search = search }, ct);

        var tenantIds = result.Items.Select(a => a.TenantId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var userIds = result.Items.Select(a => a.UserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        var tenantNames = new Dictionary<Guid, string>();
        foreach (var id in tenantIds)
            if (await tenants.GetByIdAsync(id, ct) is { } t) tenantNames[id] = t.Name;

        var userEmails = new Dictionary<Guid, string>();
        foreach (var id in userIds)
            if (await users.GetByIdAsync(id, ct) is { } u) userEmails[id] = u.Email;

        AuditLogDto Map(AuditLog a) => new(
            a.Id,
            a.TenantId,
            a.TenantId is { } tid ? tenantNames.GetValueOrDefault(tid) : null,
            a.UserId,
            a.UserId is { } uid ? userEmails.GetValueOrDefault(uid) : null,
            a.Action,
            a.EntityType,
            a.EntityId,
            a.DetailsJson,
            a.CreatedAt);

        return Ok(new PagedResult<AuditLogDto>
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }
}
