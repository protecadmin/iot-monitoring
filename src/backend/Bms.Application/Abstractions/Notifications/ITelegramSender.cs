namespace Bms.Application.Abstractions.Notifications;

/// <summary>Thin Telegram Bot API abstraction — see Bms.Infrastructure/Notifications for the concrete sender.</summary>
public interface ITelegramSender
{
    Task SendAsync(string chatId, string text, CancellationToken ct);
}
