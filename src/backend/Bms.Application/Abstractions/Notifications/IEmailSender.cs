namespace Bms.Application.Abstractions.Notifications;

/// <summary>
/// Thin SMTP abstraction — see Bms.Infrastructure/Notifications for the
/// concrete sender. Kept generic (not alert-specific) so it can be reused
/// for anything else that needs to email someone later (e.g. user invites).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct);
}
