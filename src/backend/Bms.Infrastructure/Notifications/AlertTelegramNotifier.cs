using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Bms.Infrastructure.Notifications;

/// <summary>Telegram counterpart to AlertEmailNotifier — see that class for the recipient-eligibility rules this mirrors.</summary>
public sealed class AlertTelegramNotifier(IUserRepository users, ITelegramSender telegram, ILogger<AlertTelegramNotifier> logger) : IAlertTelegramNotifier
{
    private static readonly string[] RecipientRoles = ["SuperAdmin", "Admin", "Engineer"];

    public async Task NotifyAsync(Alert alert, Device device, CancellationToken ct)
    {
        if (alert.Severity != AlertSeverity.Critical)
            return;

        var recipients = (await users.ListAsync(alert.TenantId, ct))
            .Where(u => u.IsActive && u.Role is not null && RecipientRoles.Contains(u.Role.Name) && !string.IsNullOrWhiteSpace(u.TelegramChatId))
            .ToList();

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
