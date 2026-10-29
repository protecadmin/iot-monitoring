using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// One MQTT device-facing credential per tenant — Username is always that
/// tenant's Slug (the broker's ACL pattern rule keys off it, see
/// infrastructure/mosquitto/acl), never chosen freely. The raw password is
/// stored TWO different ways for two different purposes, neither of them
/// plaintext:
///  - PasswordHash (bcrypt): what mosquitto-go-auth's Postgres backend
///    checks on every device connection. One-way — cannot be reversed.
///  - EncryptedPassword (AES, reversible): lets an admin view the
///    password again later in the UI. Only the backend, holding the
///    server-side encryption key, can decrypt it.
/// </summary>
public class MqttCredential : Entity
{
    public Guid TenantId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public DateTimeOffset? RotatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}
