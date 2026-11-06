using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Notifications;

/// <summary>
/// Critical-only by design — Warning/Info alerts already show up live in
/// the dashboard bell; emailing every alert would train people to ignore
/// the inbox. Recipients are every active SuperAdmin/Admin/Engineer on the
/// alert's tenant (Viewer/Customer accounts are read-only by role, so they
/// aren't expected to act on an alert anyway).
/// </summary>
public sealed class AlertEmailNotifier(BmsDbContext db, IEmailSender email, ILogger<AlertEmailNotifier> logger) : IAlertEmailNotifier
{

    public async Task NotifyAsync(Alert alert, Device device, CancellationToken ct)
    {
        if (alert.Severity != AlertSeverity.Critical)
            return;

        var recipients = await AlertRecipients.ForDevice(db, device).ToListAsync(ct);

        if (recipients.Count == 0)
        {
            logger.LogDebug("No email recipients for tenant {TenantId} — skipping alert email for alert {AlertId}", alert.TenantId, alert.Id);
            return;
        }

        var subject = $"[BMS Critical Alert] {device.Name}: {alert.Parameter}";
        var body = $"""
            <p><strong>{alert.Message}</strong></p>
            <p>
              Device: {device.Name} ({device.DeviceCode})<br/>
              Parameter: {alert.Parameter}<br/>
              Current value: {alert.CurrentValue?.ToString("0.##") ?? "—"}<br/>
              Threshold: {alert.Threshold?.ToString("0.##") ?? "—"}<br/>
              Triggered at: {alert.TriggeredAt:u}
            </p>
            <p>Open the dashboard to acknowledge or resolve this alert.</p>
            """;

        foreach (var recipient in recipients)
        {
            try
            {
                await email.SendAsync(recipient.Email, subject, body, ct);
            }
            catch (Exception ex)
            {
                // IEmailSender.SendAsync throws on failure (so the Settings
                // UI's "send test email" can surface the real error) — caught
                // per-recipient here so one bad address doesn't stop the rest
                // of this tenant's admins from getting the same alert.
                logger.LogError(ex, "Failed emailing alert {AlertId} to {Email}", alert.Id, recipient.Email);
            }
        }
    }
}
