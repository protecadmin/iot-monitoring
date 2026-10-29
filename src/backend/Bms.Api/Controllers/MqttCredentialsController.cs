using System.Security.Cryptography;
using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// Manages device-facing MQTT credentials — one per tenant, username
/// always that tenant's Slug (see infrastructure/mosquitto/acl, whose
/// pattern rule keys off it). SuperAdmin-only: these are what lets any
/// device impersonate a whole tenant if leaked, not a per-user setting.
/// </summary>
[ApiController]
[Route("api/mqtt-credentials")]
[Authorize(Roles = "SuperAdmin")]
public sealed class MqttCredentialsController(
    IMqttCredentialRepository credentials,
    ITenantRepository tenants,
    IMqttCredentialHasher hasher,
    IReversibleEncryptor encryptor,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MqttCredentialDto>>> List(CancellationToken ct)
    {
        var list = await credentials.ListAsync(ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<MqttCredentialDto>> Create([FromQuery] Guid tenantId, CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct) ?? throw ApiException.NotFound("Tenant not found", "TENANT_NOT_FOUND");
        if (await credentials.GetByTenantIdAsync(tenantId, ct) is not null)
            throw ApiException.Conflict("This tenant already has an MQTT credential — use rotate instead", "MQTT_CREDENTIAL_EXISTS");

        var credential = MqttCredentialFactory.Create(tenantId, tenant.Slug, hasher, encryptor);
        await credentials.AddAsync(credential, ct);
        credential.Tenant = tenant;

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = tenantId,
            UserId = CurrentUserId,
            Action = "MqttCredential.Create",
            EntityType = nameof(MqttCredential),
            EntityId = credential.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { credential.Username })
        }, ct);

        return Ok(Map(credential));
    }

    [HttpPost("{id:guid}/rotate")]
    public async Task<ActionResult<MqttCredentialDto>> Rotate(Guid id, CancellationToken ct)
    {
        var credential = await credentials.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("MQTT credential not found", "MQTT_CREDENTIAL_NOT_FOUND");

        // Username is intentionally left untouched — it's still that
        // tenant's slug, only the secret itself is replaced. Any device
        // still configured with the old password stops authenticating
        // the instant this saves; it needs the new one pushed to it.
        var rawPassword = GenerateRawPassword();
        credential.PasswordHash = hasher.Hash(rawPassword);
        credential.EncryptedPassword = encryptor.Encrypt(rawPassword);
        credential.RotatedAt = DateTimeOffset.UtcNow;
        await credentials.UpdateAsync(credential, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = credential.TenantId,
            UserId = CurrentUserId,
            Action = "MqttCredential.Rotate",
            EntityType = nameof(MqttCredential),
            EntityId = credential.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { credential.Username })
        }, ct);

        return Ok(Map(credential));
    }

    /// <summary>
    /// Replaces the password with one the admin chooses. The username can't
    /// be edited: it must stay the tenant's slug, because the broker's ACL
    /// grants iot/{username}/... and BMS addresses devices under
    /// iot/{tenantSlug}/... — any other username locks the devices out of
    /// every topic.
    /// </summary>
    [HttpPut("{id:guid}/password")]
    public async Task<ActionResult<MqttCredentialDto>> SetPassword(Guid id, SetMqttPasswordRequest request, CancellationToken ct)
    {
        var credential = await credentials.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("MQTT credential not found", "MQTT_CREDENTIAL_NOT_FOUND");

        var password = request.Password ?? string.Empty;
        if (password.Length < MinPasswordLength)
            throw ApiException.BadRequest($"Password must be at least {MinPasswordLength} characters", "MQTT_PASSWORD_TOO_SHORT");
        if (password.Length > MaxPasswordLength)
            throw ApiException.BadRequest($"Password must be {MaxPasswordLength} characters or fewer", "MQTT_PASSWORD_TOO_LONG");
        // Whitespace is legal in MQTT but is routinely mangled by device
        // config files and shell commands, so a device would fail to log in
        // with what looks like the right password.
        if (password.Any(char.IsWhiteSpace))
            throw ApiException.BadRequest("Password can't contain spaces", "MQTT_PASSWORD_WHITESPACE");

        credential.PasswordHash = hasher.Hash(password);
        credential.EncryptedPassword = encryptor.Encrypt(password);
        credential.RotatedAt = DateTimeOffset.UtcNow;
        await credentials.UpdateAsync(credential, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = credential.TenantId,
            UserId = CurrentUserId,
            Action = "MqttCredential.SetPassword",
            EntityType = nameof(MqttCredential),
            EntityId = credential.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { credential.Username })
        }, ct);

        return Ok(Map(credential));
    }

    /// <summary>
    /// Removes the tenant's broker login. New device connections are refused
    /// from the next attempt; connections already open stay up until they
    /// reconnect. A fresh credential can be created again at any time.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var credential = await credentials.GetByIdAsync(id, ct) ?? throw ApiException.NotFound("MQTT credential not found", "MQTT_CREDENTIAL_NOT_FOUND");
        await credentials.DeleteAsync(credential, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = credential.TenantId,
            UserId = CurrentUserId,
            Action = "MqttCredential.Delete",
            EntityType = nameof(MqttCredential),
            EntityId = credential.Id.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { credential.Username })
        }, ct);

        return NoContent();
    }

    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;

    private static string GenerateRawPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    private MqttCredentialDto Map(MqttCredential c) =>
        new(c.Id, c.TenantId, c.Tenant?.Name ?? "—", c.Username, encryptor.Decrypt(c.EncryptedPassword), c.CreatedAt, c.RotatedAt);
}
