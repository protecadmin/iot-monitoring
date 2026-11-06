using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public sealed class SitesController(ISiteRepository sites) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SiteDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        // Unlike a tenant-scoped caller, a SuperAdmin may omit tenantId to
        // list sites across every tenant (EffectiveTenantFilter returns
        // null in that case) — see DevicesController for the same pattern.
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var result = await sites.ListAsync(effectiveTenantId, new PageRequest { Page = page, PageSize = pageSize, Search = search }, ct);

        return Ok(new PagedResult<SiteDto>
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteDto>> GetById(Guid id, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var site = await sites.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Site not found", "SITE_NOT_FOUND");
        return Ok(Map(site));
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<SiteDto>> Create([FromQuery] Guid tenantId, CreateSiteRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var site = new Site
        {
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        };
        await sites.AddAsync(site, ct);
        return CreatedAtAction(nameof(GetById), new { id = site.Id, tenantId }, Map(site));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
    public async Task<ActionResult<SiteDto>> Update(Guid id, [FromQuery] Guid tenantId, UpdateSiteRequest request, CancellationToken ct)
    {
        RequireTenantAccess(tenantId);
        var site = await sites.GetByIdAsync(tenantId, id, ct) ?? throw ApiException.NotFound("Site not found", "SITE_NOT_FOUND");
        site.Name = request.Name;
        site.Address = request.Address;
        site.Latitude = request.Latitude;
        site.Longitude = request.Longitude;
        await sites.UpdateAsync(site, ct);
        return Ok(Map(site));
    }

    private static SiteDto Map(Site s) => new(s.Id, s.TenantId, s.Code, s.Name, s.Address, s.Latitude, s.Longitude);
}
