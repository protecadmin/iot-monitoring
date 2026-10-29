using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// Singleton row (always Id == SingletonId) — platform-wide Telegram bot
/// config for Critical-alert notifications, mirrors SmtpSettings exactly.
/// Bot token is AES-encrypted (via IReversibleEncryptor) and never returned
/// to the frontend — only whether one is set.
/// </summary>
public class TelegramSettings : Entity
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public string? EncryptedBotToken { get; set; }
}
