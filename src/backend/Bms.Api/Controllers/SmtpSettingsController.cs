using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Security;
using Bms.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Controllers;

/// <summary>
/// Platform-wide SMTP config for Critical-alert emails (see
/// AlertEmailNotifier) — editable here instead of only via
/// appsettings.json, so an admin can turn email alerts on/off or rotate
/// credentials without a config-file edit + restart.
/// </summary>
[ApiController]
[Route("api/settings/smtp")]
[Authorize(Roles = "SuperAdmin")]
public sealed class SmtpSettingsController(
    ISmtpSettingsRepository settingsRepo,
    IReversibleEncryptor encryptor,
    IEmailSender email,
    IAuditLogRepository auditLog) : BmsControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SmtpSettingsDto>> Get(CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        if (settings is null)
            return Ok(new SmtpSettingsDto("", 587, "", false, true, "alerts@bms.local", "BMS Platform"));
        return Ok(Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<SmtpSettingsDto>> Update(UpdateSmtpSettingsRequest request, CancellationToken ct)
    {
        var settings = new SmtpSettings
        {
            Host = request.Host,
            Port = request.Port,
            Username = request.Username,
            // null (not "") means "leave the stored password alone" — see
            // SmtpSettingsRepository.UpsertAsync. An explicitly empty string
            // clears it (e.g. switching to an unauthenticated relay).
            EncryptedPassword = request.Password is null ? null : (request.Password.Length == 0 ? "" : encryptor.Encrypt(request.Password)),
            EnableSsl = request.EnableSsl,
            FromAddress = request.FromAddress,
            FromName = request.FromName
        };
        var saved = await settingsRepo.UpsertAsync(settings, ct);

        await auditLog.AddAsync(new AuditLog
        {
            TenantId = null,
            UserId = CurrentUserId,
            Action = "SmtpSettings.Update",
            EntityType = nameof(SmtpSettings),
            EntityId = SmtpSettings.SingletonId.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { saved.Host, saved.Port, saved.Username, saved.FromAddress })
        }, ct);

        return Ok(Map(saved));
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(SendTestEmailRequest request, CancellationToken ct)
    {
        var settings = await settingsRepo.GetAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
            throw ApiException.BadRequest("Save SMTP settings with a host before sending a test email", "SMTP_NOT_CONFIGURED");

        try
        {
            await email.SendAsync(request.ToEmail, "BMS Platform — test email",
                "<p>This is a test email from your BMS Platform's SMTP settings. If you received this, Critical-severity alert emails are configured correctly.</p>", ct);
        }
        catch (Exception ex)
        {
            throw ApiException.BadRequest($"Failed to send: {ex.Message}", "SMTP_SEND_FAILED");
        }

        return NoContent();
    }

    private static SmtpSettingsDto Map(SmtpSettings s) =>
        new(s.Host, s.Port, s.Username, !string.IsNullOrEmpty(s.EncryptedPassword), s.EnableSsl, s.FromAddress, s.FromName);
}
