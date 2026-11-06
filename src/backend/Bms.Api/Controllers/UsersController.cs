using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Api.Controllers;

/// <summary>
/// User provisioning — this is how a customer actually gets a login: a
/// SuperAdmin can create a user for any tenant (or none, for another
/// SuperAdmin), and a tenant Admin can create one for their own tenant
/// only. There is no self-service signup; accounts only exist because
/// someone with access here created them.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = "SuperAdmin,CompanyAdmin")]
public sealed class UsersController(IUserRepository users, IPasswordHasher hasher, IAuditLogRepository auditLog, IRefreshTokenRepository refreshTokens, BmsDbContext db) : BmsControllerBase
{
    private static readonly string[] AssignableRoles = ["Executive", "CompanyAdmin", "SiteAdmin"];

    private void RequireAssignable(string role)
    {
        if (!AssignableRoles.Contains(role) || (!IsSuperAdmin && role != "SiteAdmin"))
            throw ApiException.Forbidden("You cannot assign this role", "ROLE_FORBIDDEN");
    }

    private void RequireManagedUser(User user)
    {
        if (IsSuperAdmin) return;
        if (user.TenantId != CurrentTenantId || user.Role?.Name != "SiteAdmin")
            throw ApiException.Forbidden("Company administrators may manage only their company's Site Admin accounts", "USER_FORBIDDEN");
    }

    private async Task<Guid[]> ValidateSites(string role, Guid? tenantId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        var selected = ids?.Distinct().ToArray() ?? [];
        if (role != "SiteAdmin")
        {
            if (selected.Length > 0) throw ApiException.BadRequest("Only Site Admin accounts have site assignments", "INVALID_SITES");
            return [];
        }
        if (selected.Length == 0) throw ApiException.BadRequest("Assign at least one site", "SITES_REQUIRED");
        if (await db.Sites.CountAsync(s => selected.Contains(s.Id) && s.TenantId == tenantId, ct) != selected.Length)
            throw ApiException.Forbidden("All assigned sites must belong to the account's company", "SITE_FORBIDDEN");
        return selected;
    }

    private async Task ReplaceSites(Guid userId, Guid[] ids, CancellationToken ct)
    {
        var old = await db.UserSiteAccess.Where(a => a.UserId == userId).ToListAsync(ct);
        db.UserSiteAccess.RemoveRange(old.Where(a => !ids.Contains(a.SiteId)));
        db.UserSiteAccess.AddRange(ids.Where(id => old.All(a => a.SiteId != id)).Select(id => new UserSiteAccess { UserId = userId, SiteId = id }));
        await db.SaveChangesAsync(ct);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ManagedUserDto>>> List([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var list = await users.ListAsync(effectiveTenantId, ct);
        var assignments = await db.UserSiteAccess.Where(a => list.Select(u => u.Id).Contains(a.UserId)).ToListAsync(ct);
        return Ok(list.Select(u => Map(u) with { AllowedSiteIds = assignments.Where(a => a.UserId == u.Id).Select(a => a.SiteId).ToArray() }).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ManagedUserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            throw ApiException.BadRequest("A valid email is required", "INVALID_EMAIL");
        if (request.Password.Length < 8)
            throw ApiException.BadRequest("Password must be at least 8 characters", "PASSWORD_TOO_SHORT");
        if (await users.GetByEmailAsync(request.Email, ct) is not null)
            throw ApiException.Conflict("A user with this email already exists", "USER_EMAIL_TAKEN");

        // SuperAdmin is not an assignable role from this endpoint — granting
        // platform-wide access is deliberately not a checkbox in a form.
        RequireAssignable(request.Role);

        var role = await users.GetRoleByNameAsync(request.Role, ct) ?? throw ApiException.BadRequest("Unknown role", "INVALID_ROLE");

        // An Admin can only provision users inside their own tenant,
        // regardless of what tenantId was requested — a SuperAdmin may
        // target any tenant.
        Guid? tenantId;
        if (IsSuperAdmin)
        {
            tenantId = request.Role == "Executive" ? null : request.TenantId ?? throw ApiException.BadRequest("tenantId is required", "TENANT_REQUIRED");
        }

        else
        {
            tenantId = CurrentTenantId ?? throw ApiException.Forbidden("No tenant associated with this account", "NO_TENANT");
            if (request.TenantId is { } requested && requested != tenantId)
                throw ApiException.Forbidden("You cannot create accounts in another company", "TENANT_FORBIDDEN");
        }

        if (tenantId is { } targetTenant && !await db.Tenants.AnyAsync(t => t.Id == targetTenant, ct))
            throw ApiException.BadRequest("Company not found", "TENANT_NOT_FOUND");
        var assignedSites = await ValidateSites(request.Role, tenantId, request.AllowedSiteIds, ct);

        var user = new User
        {
            TenantId = tenantId,
            RoleId = role.Id,
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName,
            PasswordHash = hasher.Hash(request.Password),
            IsActive = true,
            TelegramChatId = string.IsNullOrWhiteSpace(request.TelegramChatId) ? null : request.TelegramChatId.Trim()
        };
        db.Users.Add(user);
        var created = user;
        await ReplaceSites(created.Id, assignedSites, ct);
        created.Role = role;
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "User.Create",
            EntityType = nameof(User),
            EntityId = created.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { created.Email, Role = request.Role, AllowedSiteIds = assignedSites })
        }, ct);
        return Ok(Map(created) with { AllowedSiteIds = assignedSites });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ManagedUserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("User not found", "USER_NOT_FOUND");
        RequireManagedUser(user);

        if (user.TenantId is { } tenantId)
            RequireTenantAccess(tenantId);
        else if (!IsSuperAdmin)
            throw ApiException.Forbidden("Only SuperAdmin can manage platform-wide accounts", "FORBIDDEN");

        string? newRoleName = null;
        var targetRole = string.IsNullOrWhiteSpace(request.Role) ? user.Role?.Name ?? "" : request.Role;
        var oldAssignments = await db.UserSiteAccess.Where(a => a.UserId == id).Select(a => a.SiteId).ToArrayAsync(ct);
        var assignedSites = await ValidateSites(targetRole, user.TenantId, request.AllowedSiteIds ?? (targetRole == "SiteAdmin" ? oldAssignments : []), ct);
        if (targetRole == "Executive" && user.TenantId is not null)
            throw ApiException.BadRequest("Executive is an internal account. Create a separate account without a company", "EXECUTIVE_COMPANY");
        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role != user.Role?.Name)
        {
            if (user.Id == CurrentUserId)
                throw ApiException.BadRequest("You cannot change your own role", "CANNOT_CHANGE_OWN_ROLE");
            if (user.Role?.Name == "SuperAdmin")
                throw ApiException.BadRequest("A SuperAdmin's role can't be changed here", "INVALID_ROLE");
            RequireAssignable(request.Role);
            if (request.Role is "CompanyAdmin" or "SiteAdmin" && user.TenantId is null)
                throw ApiException.BadRequest("Customer roles require a company", "TENANT_REQUIRED");

            var role = await users.GetRoleByNameAsync(request.Role, ct) ?? throw ApiException.BadRequest("Unknown role", "INVALID_ROLE");
            user.RoleId = role.Id;
            user.Role = role;
            newRoleName = role.Name;
        }

        var passwordChanged = !string.IsNullOrEmpty(request.Password);
        if (passwordChanged)
        {
            if (request.Password!.Length < 8)
                throw ApiException.BadRequest("Password must be at least 8 characters", "PASSWORD_TOO_SHORT");
            user.PasswordHash = hasher.Hash(request.Password);
        }

        user.DisplayName = request.DisplayName;
        user.TelegramChatId = string.IsNullOrWhiteSpace(request.TelegramChatId) ? null : request.TelegramChatId.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await ReplaceSites(user.Id, assignedSites, ct);

        // A reset password (or a changed role) should not leave old sessions alive.
        if (passwordChanged || newRoleName is not null || !oldAssignments.Order().SequenceEqual(assignedSites.Order()))
            await refreshTokens.RevokeAllActiveForUserAsync(user.Id, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = CurrentUserId,
            Action = "User.Update",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email, Role = newRoleName, PasswordReset = passwordChanged, PreviousSiteIds = oldAssignments, AllowedSiteIds = assignedSites })
        }, ct);
        return Ok(Map(user) with { AllowedSiteIds = assignedSites });
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) => await SetActive(id, false, ct);

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => await SetActive(id, true, ct);

    private async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("User not found", "USER_NOT_FOUND");
        RequireManagedUser(user);

        if (user.TenantId is { } tenantId)
            RequireTenantAccess(tenantId);
        else if (!IsSuperAdmin)
            throw ApiException.Forbidden("Only SuperAdmin can manage platform-wide accounts", "FORBIDDEN");

        if (user.Id == CurrentUserId && !active)
            throw ApiException.BadRequest("You cannot deactivate your own account", "CANNOT_DEACTIVATE_SELF");

        user.IsActive = active;
        await users.UpdateAsync(user, ct);
        if (!active) await refreshTokens.RevokeAllActiveForUserAsync(user.Id, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = CurrentUserId,
            Action = active ? "User.Activate" : "User.Deactivate",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email })
        }, ct);
        return NoContent();
    }

    private static ManagedUserDto Map(User u) => new(u.Id, u.Email, u.DisplayName, u.Role?.Name ?? "Unknown", u.TenantId, u.IsActive, u.CreatedAt, u.TelegramChatId);
}
