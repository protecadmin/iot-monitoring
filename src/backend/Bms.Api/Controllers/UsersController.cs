using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class UsersController(IUserRepository users, IPasswordHasher hasher, IAuditLogRepository auditLog, IRefreshTokenRepository refreshTokens) : BmsControllerBase
{
    private static readonly string[] AssignableRoles = ["Admin", "Engineer", "Viewer", "Customer"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ManagedUserDto>>> List([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var effectiveTenantId = EffectiveTenantFilter(tenantId);
        var list = await users.ListAsync(effectiveTenantId, ct);
        return Ok(list.Select(Map).ToList());
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
        if (request.Role == "SuperAdmin" || !AssignableRoles.Contains(request.Role))
            throw ApiException.BadRequest($"Role must be one of: {string.Join(", ", AssignableRoles)}", "INVALID_ROLE");

        var role = await users.GetRoleByNameAsync(request.Role, ct) ?? throw ApiException.BadRequest("Unknown role", "INVALID_ROLE");

        // An Admin can only provision users inside their own tenant,
        // regardless of what tenantId was requested — a SuperAdmin may
        // target any tenant.
        Guid tenantId;
        if (IsSuperAdmin)
        {
            tenantId = request.TenantId ?? throw ApiException.BadRequest("tenantId is required", "TENANT_REQUIRED");
        }
        else
        {
            tenantId = CurrentTenantId ?? throw ApiException.Forbidden("No tenant associated with this account", "NO_TENANT");
        }

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
        var created = await users.AddAsync(user, ct);
        created.Role = role;
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "User.Create",
            EntityType = nameof(User),
            EntityId = created.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { created.Email, Role = request.Role })
        }, ct);
        return Ok(Map(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ManagedUserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("User not found", "USER_NOT_FOUND");

        if (user.TenantId is { } tenantId)
            RequireTenantAccess(tenantId);
        else if (!IsSuperAdmin)
            throw ApiException.Forbidden("Only SuperAdmin can manage platform-wide accounts", "FORBIDDEN");

        string? newRoleName = null;
        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role != user.Role?.Name)
        {
            if (user.Id == CurrentUserId)
                throw ApiException.BadRequest("You cannot change your own role", "CANNOT_CHANGE_OWN_ROLE");
            if (user.Role?.Name == "SuperAdmin")
                throw ApiException.BadRequest("A SuperAdmin's role can't be changed here", "INVALID_ROLE");
            if (!AssignableRoles.Contains(request.Role))
                throw ApiException.BadRequest($"Role must be one of: {string.Join(", ", AssignableRoles)}", "INVALID_ROLE");

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
        await users.UpdateAsync(user, ct);

        // A reset password (or a changed role) should not leave old sessions alive.
        if (passwordChanged || newRoleName is not null)
            await refreshTokens.RevokeAllActiveForUserAsync(user.Id, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = CurrentUserId,
            Action = "User.Update",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email, Role = newRoleName, PasswordReset = passwordChanged })
        }, ct);
        return Ok(Map(user));
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) => await SetActive(id, false, ct);

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => await SetActive(id, true, ct);

    private async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("User not found", "USER_NOT_FOUND");

        if (user.TenantId is { } tenantId)
            RequireTenantAccess(tenantId);
        else if (!IsSuperAdmin)
            throw ApiException.Forbidden("Only SuperAdmin can manage platform-wide accounts", "FORBIDDEN");

        if (user.Id == CurrentUserId && !active)
            throw ApiException.BadRequest("You cannot deactivate your own account", "CANNOT_DEACTIVATE_SELF");

        user.IsActive = active;
        await users.UpdateAsync(user, ct);
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

    private static ManagedUserDto Map(User u) => new(u.Id, u.Email, u.DisplayName, u.Role?.Name ?? "Viewer", u.TenantId, u.IsActive, u.CreatedAt, u.TelegramChatId);
}
