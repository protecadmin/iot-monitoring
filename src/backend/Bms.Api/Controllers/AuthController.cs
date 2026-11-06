using System.Security.Cryptography;
using System.Text;
using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Bms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bms.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IAuditLogRepository auditLog,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(request.Email, ct);
        if (user is null || !user.IsActive || !hasher.Verify(request.Password, user.PasswordHash))
            throw ApiException.BadRequest("Invalid email or password", "INVALID_CREDENTIALS");

        var roleName = RequireReviewedRole(user);
        var tokens = jwt.IssueTokens(user, roleName);
        await PersistRefreshTokenAsync(user.Id, tokens.RefreshToken, ct);

        return Ok(new LoginResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            new UserDto(user.Id, user.Email, user.DisplayName, roleName, user.TenantId)));
    }

    /// <summary>
    /// Rotates the presented refresh token: it's revoked and a brand new
    /// access+refresh pair is issued, rather than reusing the same refresh
    /// token across its whole lifetime. If a token that's already been
    /// rotated out gets presented again, that's a strong signal it leaked
    /// (the legitimate client would have moved on to the newer one) — so
    /// every active session for that user is revoked, not just this one.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var stored = await refreshTokens.GetByTokenHashAsync(Hash(request.RefreshToken), ct);
        if (stored is null)
            throw ApiException.BadRequest("Invalid refresh token", "INVALID_REFRESH_TOKEN");

        if (stored.RevokedAt is not null)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(stored.UserId, ct);
            throw ApiException.BadRequest("Refresh token has already been used; all sessions revoked", "REFRESH_TOKEN_REUSED");
        }

        if (stored.ExpiresAt <= DateTimeOffset.UtcNow)
            throw ApiException.BadRequest("Refresh token expired", "REFRESH_TOKEN_EXPIRED");

        var user = await users.GetByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
            throw ApiException.BadRequest("Invalid refresh token", "INVALID_REFRESH_TOKEN");

        var roleName = RequireReviewedRole(user);
        var newTokens = jwt.IssueTokens(user, roleName);
        var newEntity = await PersistRefreshTokenAsync(user.Id, newTokens.RefreshToken, ct);

        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.ReplacedByTokenId = newEntity.Id;
        await refreshTokens.UpdateAsync(stored, ct);

        return Ok(new LoginResponse(
            newTokens.AccessToken,
            newTokens.AccessTokenExpiresAt,
            newTokens.RefreshToken,
            new UserDto(user.Id, user.Email, user.DisplayName, roleName, user.TenantId)));
    }

    /// <summary>
    /// Revokes the presented refresh token server-side — without this,
    /// "logout" only ever forgot the token client-side while it stayed
    /// valid (and silently refreshable) until it naturally expired.
    /// AllowAnonymous because the access token may already be gone/expired
    /// by the time logout runs; possessing the raw refresh token is itself
    /// sufficient proof of the right to revoke it.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            var stored = await refreshTokens.GetByTokenHashAsync(Hash(request.RefreshToken), ct);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
                await refreshTokens.UpdateAsync(stored, ct);
            }
        }
        return NoContent();
    }

    /// <summary>
    /// A signed-in user changes their own password. The current password is
    /// required, so an unlocked, unattended session can't be used to take the
    /// account over. Every other session is signed out (its refresh token is
    /// revoked); this one continues with the fresh tokens returned.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!.Value);
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            throw ApiException.BadRequest("Your account is no longer active", "ACCOUNT_INACTIVE");

        if (string.IsNullOrEmpty(request.CurrentPassword) || !hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw ApiException.BadRequest("Current password is incorrect", "CURRENT_PASSWORD_INCORRECT");
        // Same rule as a password set by an admin (UsersController).
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 8)
            throw ApiException.BadRequest("Password must be at least 8 characters", "PASSWORD_TOO_SHORT");
        if (request.NewPassword == request.CurrentPassword)
            throw ApiException.BadRequest("The new password must be different from the current one", "PASSWORD_UNCHANGED");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await users.UpdateAsync(user, ct);
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, ct);
        await auditLog.AddAsync(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            Action = "User.ChangePassword",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email })
        }, ct);

        var roleName = RequireReviewedRole(user);
        var tokens = jwt.IssueTokens(user, roleName);
        await PersistRefreshTokenAsync(user.Id, tokens.RefreshToken, ct);
        return Ok(new LoginResponse(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            new UserDto(user.Id, user.Email, user.DisplayName, roleName, user.TenantId)));
    }

    private static string RequireReviewedRole(User user)
    {
        if (!Bms.Domain.AccessRoles.IsSupported(user.Role?.Name) ||
            (!Bms.Domain.AccessRoles.IsPlatform(user.Role?.Name) && user.TenantId is null) ||
            (user.Role?.Name == Bms.Domain.AccessRoles.Executive && user.TenantId is not null))
            throw ApiException.Forbidden("Your account requires an administrator role and site access review", "ROLE_REVIEW_REQUIRED");
        return user.Role!.Name;
    }

    private async Task<RefreshToken> PersistRefreshTokenAsync(Guid userId, string rawRefreshToken, CancellationToken ct) =>
        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(rawRefreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        }, ct);

    /// <summary>Only the hash is ever persisted — the raw refresh token is bearer-secret, known only to the client that received it.</summary>
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
