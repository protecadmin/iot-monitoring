namespace Bms.Infrastructure.Security;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>Base64-encoded 32-byte AES-256 key. Never in appsettings.json — set via the Encryption__Key environment variable or a secrets manager, same discipline as Jwt:SecretKey.</summary>
    public string Key { get; set; } = string.Empty;
}
