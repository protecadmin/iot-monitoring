using System.Security.Cryptography;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;

namespace Bms.Api.Common;

/// <summary>
/// Shared by MqttCredentialsController (manual create/rotate) and
/// TenantsController (auto-provision on tenant creation) so both produce
/// credentials the same way — one random raw password, bcrypt-hashed for
/// the broker and AES-encrypted for later admin viewing, raw value never
/// persisted. See MqttCredential for why both forms are stored.
/// </summary>
public static class MqttCredentialFactory
{
    public static MqttCredential Create(Guid tenantId, string username, IMqttCredentialHasher hasher, IReversibleEncryptor encryptor)
    {
        var rawPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        return new MqttCredential
        {
            TenantId = tenantId,
            Username = username,
            PasswordHash = hasher.Hash(rawPassword),
            EncryptedPassword = encryptor.Encrypt(rawPassword)
        };
    }
}
