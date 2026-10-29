using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// A refresh token is stored by its SHA-256 hash, never the raw value —
/// the raw value is only ever known to the client that received it. Login
/// issues one; /api/auth/refresh rotates it (the presented token is
/// revoked and a new one issued) rather than reusing the same token
/// indefinitely, so a leaked-and-later-revoked token can be detected if
/// someone tries to replay it after rotation (see AuthController).
/// </summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public User? User { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
