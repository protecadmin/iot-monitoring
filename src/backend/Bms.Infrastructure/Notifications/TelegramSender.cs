using System.Net.Http.Json;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;

namespace Bms.Infrastructure.Notifications;

/// <summary>
/// Plain HTTP POST to Telegram's Bot API (api.telegram.org) — no SDK
/// needed, the API is a simple JSON POST. Bot token read live from the
/// TelegramSettings DB row (edited via the Settings UI), same pattern as
/// SmtpEmailSender reading SMTP config from the DB instead of appsettings.
/// </summary>
public sealed class TelegramSender(ITelegramSettingsRepository settingsRepo, IReversibleEncryptor encryptor, IHttpClientFactory httpClientFactory, ILogger<TelegramSender> logger) : ITelegramSender
{
    public async Task SendAsync(string chatId, string text, CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.EncryptedBotToken))
        {
            logger.LogDebug("Telegram bot token not configured — skipping message to chat {ChatId}", chatId);
            return;
        }

        var botToken = encryptor.Decrypt(settings.EncryptedBotToken);
        var client = httpClientFactory.CreateClient(nameof(TelegramSender));

        var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{botToken}/sendMessage",
            new { chat_id = chatId, text, parse_mode = "HTML" },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // Thrown, not just logged — same contract as IEmailSender, so
            // the Settings UI's "send test message" can surface the real
            // Telegram API error (e.g. "chat not found" if the recipient
            // never messaged the bot). Callers from IngestionPipeline
            // already wrap this in TryAsync.
            throw new InvalidOperationException($"Telegram API returned {(int)response.StatusCode}: {body}");
        }
    }
}
