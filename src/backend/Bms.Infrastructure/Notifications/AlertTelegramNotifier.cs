using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Notifications;

/// <summary>Telegram counterpart to AlertEmailNotifier — see that class for the recipient-eligibility rules this mirrors.</summary>
public sealed class AlertTelegramNotifier(BmsDbContext db, ITelegramSender telegram, ILogger<AlertTelegramNotifier> logger) : IAlertTelegramNotifier
{

    public async Task NotifyAsync(Alert alert, Device device, CancellationToken ct)
    {
        if (alert.Severity != AlertSeverity.Critical)
            return;

        var recipients = await AlertRecipients.ForDevice(db, device).Where(u => u.TelegramChatId != null && u.TelegramChatId != "").ToListAsync(ct);

        if (recipients.Count == 0)
        {
            logger.LogDebug("No Telegram recipients for tenant {TenantId} — skipping alert Telegram message for alert {AlertId}", alert.TenantId, alert.Id);
            return;
        }

        var text = $"""
            <b>🔴 BMS Critical Alert</b>
            {alert.Message}

            Device: {device.Name} ({device.DeviceCode})
            Parameter: {alert.Parameter}
            Current value: {alert.CurrentValue?.ToString("0.##") ?? "—"}
            Threshold: {alert.Threshold?.ToString("0.##") ?? "—"}
            Triggered at: {alert.TriggeredAt:u}
            """;

        foreach (var recipient in recipients)
        {
            try
            {
                await telegram.SendAsync(recipient.TelegramChatId!, text, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed sending Telegram alert {AlertId} to chat {ChatId}", alert.Id, recipient.TelegramChatId);
            }
        }
    }
}
