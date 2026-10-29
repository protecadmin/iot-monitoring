using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>Platform-wide Telegram bot config for Critical-alert messages — mirrors SmtpSettingsController exactly.</summary>
[ApiController]
[Route("api/settings/telegram")]
[Authorize(Roles = "SuperAdmin")]
public sealed class TelegramSettingsController(
    ITelegramSettingsRepository settingsRepo,
    IReversibleEncryptor encryptor,
    ITelegramSender telegram,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TelegramSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        return Ok(new TelegramSettingsDto(!string.IsNullOrEmpty(settings?.EncryptedBotToken)));
    }

    [HttpPut]
    public async Task<ActionResult<TelegramSettingsDto>> Update(UpdateTelegramSettingsRequest request, CancellationToken ct)
    {
        var settings = new TelegramSettings
        {
            EncryptedBotToken = request.BotToken is null ? null : (request.BotToken.Length == 0 ? "" : encryptor.Encrypt(request.BotToken))
        };
        var saved = await settingsRepo.UpsertAsync(settings, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = null,
            UserId = CurrentUserId,
            Action = "TelegramSettings.Update",
            EntityType = nameof(TelegramSettings),
            EntityId = TelegramSettings.SingletonId.ToString()
        }, ct);

        return Ok(new TelegramSettingsDto(!string.IsNullOrEmpty(saved.EncryptedBotToken)));
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(SendTelegramTestRequest request, CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.EncryptedBotToken))
            throw ApiException.BadRequest("Save a bot token before sending a test message", "TELEGRAM_NOT_CONFIGURED");

        try
        {
            await telegram.SendAsync(request.ChatId, "This is a test message from your BMS Platform's Telegram settings. If you received this, Critical-severity alerts are configured correctly.", ct);
        }
        catch (Exception ex)
        {
            throw ApiException.BadRequest($"Failed to send: {ex.Message}", "TELEGRAM_SEND_FAILED");
        }

        return NoContent();
    }
}
