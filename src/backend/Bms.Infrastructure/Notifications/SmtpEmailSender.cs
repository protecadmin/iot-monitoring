using System.Net;
using System.Net.Mail;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Bms.Infrastructure.Notifications;

/// <summary>
/// Reads config from the SmtpSettings DB row (edited via the Settings UI)
/// rather than appsettings.json — lets an admin turn email alerts on/off
/// and change credentials without touching config files or restarting the
/// process. Plain SMTP relay send, works with any provider that speaks SMTP
/// (SendGrid, SES, a corporate relay, Gmail with an app password).
/// </summary>
public sealed class SmtpEmailSender(ISmtpSettingsRepository settingsRepo, IReversibleEncryptor encryptor, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
        {
            logger.LogDebug("SMTP not configured — skipping email to {ToEmail} (\"{Subject}\")", toEmail, subject);
            return;
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = string.IsNullOrEmpty(settings.Username)
                ? null
                : new NetworkCredential(settings.Username, settings.EncryptedPassword is null ? "" : encryptor.Decrypt(settings.EncryptedPassword))
        };

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex)
        {
            // Same isolation as every other IngestionPipeline side effect — a
            // broken SMTP relay must not take telemetry ingestion down with it.
            logger.LogError(ex, "Failed sending email to {ToEmail} (\"{Subject}\")", toEmail, subject);
            throw; // rethrown so the Settings UI's "send test email" can surface the real error — callers from IngestionPipeline already wrap this in TryAsync.
        }
    }
}
