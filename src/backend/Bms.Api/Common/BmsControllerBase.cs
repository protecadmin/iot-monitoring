using Bms.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Common;

/// <summary>
/// Tenant isolation is enforced here, once, rather than repeated per
/// controller: every non-SuperAdmin JWT carries a "tenantId" claim (see
/// JwtTokenService), and every tenant-scoped endpoint must call
/// <see cref="RequireTenantAccess"/> before touching another tenant's data.
/// See docs/api/api-design.md#tenant-isolation-is-mandatory.
/// </summary>
public abstract class BmsControllerBase : ControllerBase
{
    protected bool IsSuperAdmin => User.IsInRole("SuperAdmin");
    protected bool HasPlatformRead => IsSuperAdmin || User.IsInRole("Executive");

    protected Guid? CurrentTenantId
    {
        get
        {
            var claim = User.FindFirst("tenantId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    protected Guid CurrentUserId => Guid.Parse(User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!.Value);

    /// <summary>Throws 403 unless the caller is a SuperAdmin or belongs to <paramref name="tenantId"/>.</summary>
    protected void RequireTenantAccess(Guid tenantId)
    {
        if (HasPlatformRead)
            return;

        if (CurrentTenantId != tenantId)
            throw ApiException.Forbidden("You do not have access to this tenant's data", "TENANT_FORBIDDEN");
    }

    /// <summary>For list endpoints: the tenant filter to apply (null only for a SuperAdmin browsing everything).</summary>
    protected Guid? EffectiveTenantFilter(Guid? requestedTenantId)
    {
        if (HasPlatformRead)
            return requestedTenantId;

        if (CurrentTenantId is not { } tenantId)
            throw ApiException.Forbidden("No tenant associated with this account", "NO_TENANT");

        if (requestedTenantId is { } requested && requested != tenantId)
            throw ApiException.Forbidden("You do not have access to this tenant's data", "TENANT_FORBIDDEN");

        return tenantId;
    }
}
