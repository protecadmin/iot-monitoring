using System.Security.Claims;
using Bms.Api.Common;
using Bms.Domain;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Api.Middleware;

public sealed class AccessScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, BmsDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var ct = context.RequestAborted;
            var subject = context.User.FindFirst("sub")?.Value;
            var user = Guid.TryParse(subject, out var userId)
                ? await db.Users.Include(u => u.Role).AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) : null;
            // Read current DB authorization on every request: revoked roles/assignments and disabled users take effect immediately.
            if (user is null || !user.IsActive || !AccessRoles.IsSupported(user.Role?.Name))
                throw ApiException.Forbidden("Account requires an administrator access review", "ROLE_REVIEW_REQUIRED");
            var role = user.Role!.Name;
            if (!AccessRoles.IsPlatform(role) && user.TenantId is null)
                throw ApiException.Forbidden("A customer account must belong to a company", "NO_TENANT");
            if (role == AccessRoles.Executive && user.TenantId is not null)
                throw ApiException.Forbidden("Executive accounts must be internal platform accounts", "ROLE_REVIEW_REQUIRED");
            var identity = new ClaimsIdentity(context.User.Identity.AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim("sub", user.Id.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            if (user.TenantId is { } tenant) identity.AddClaim(new Claim("tenantId", tenant.ToString()));
            context.User = new ClaimsPrincipal(identity);
            db.AccessTenantId = AccessRoles.IsPlatform(role) ? null : user.TenantId;
            db.RestrictToSites = role == AccessRoles.SiteAdmin;
            if (db.RestrictToSites)
                db.AllowedSiteIds = await db.UserSiteAccess.Where(a => a.UserId == user.Id && db.Sites.Any(s => s.Id == a.SiteId && s.TenantId == user.TenantId)).Select(a => a.SiteId).ToArrayAsync(ct);
            db.EnforceAccessScope = true;

            // Reject explicit forged scopes, including query filters, instead of returning misleading empty results.
            if (context.Request.Query.TryGetValue("tenantId", out var requestedTenant) && Guid.TryParse(requestedTenant, out var tid) && !AccessRoles.IsPlatform(role) && tid != user.TenantId)
                throw ApiException.Forbidden("Company access denied", "TENANT_FORBIDDEN");
            if (context.Request.Query.TryGetValue("siteId", out var requestedSite) && Guid.TryParse(requestedSite, out var sid) &&
                !await db.Sites.AnyAsync(s => s.Id == sid, ct))
                throw ApiException.Forbidden("Site access denied", "SITE_FORBIDDEN");
        }
        await next(context);
    }
}
