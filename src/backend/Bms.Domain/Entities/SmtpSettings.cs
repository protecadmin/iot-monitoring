using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// Singleton row (always Id == SingletonId) — platform-wide SMTP config,
/// editable from the Settings UI instead of only appsettings.json. Password
/// is stored AES-encrypted (via IReversibleEncryptor, same as MqttCredential)
/// rather than plaintext, and never returned to the frontend — only whether
/// one is set.
/// </summary>
public class SmtpSettings : Entity
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string? EncryptedPassword { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = "alerts@bms.local";
    public string FromName { get; set; } = "BMS Platform";
}
