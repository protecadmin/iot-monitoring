using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Notifications;

/// <summary>
/// Telegram counterpart to IAlertEmailNotifier — same Critical-only,
/// SuperAdmin/Admin/Engineer recipient rule, further filtered to users who
/// have actually set a TelegramChatId (opted in by messaging the bot).
/// Called from IngestionPipeline the same way, wrapped in the same
/// TryAsync isolation.
/// </summary>
public interface IAlertTelegramNotifier
{
    Task NotifyAsync(Alert alert, Device device, CancellationToken ct);
}
