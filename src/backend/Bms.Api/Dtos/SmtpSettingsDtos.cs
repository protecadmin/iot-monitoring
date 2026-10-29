namespace Bms.Api.Dtos;

// Password is never returned — HasPassword tells the UI whether one is
// already set, so a masked "••••••••" placeholder can show without ever
// round-tripping the real (or even the encrypted) value to the browser.
public sealed record SmtpSettingsDto(string Host, int Port, string Username, bool HasPassword, bool EnableSsl, string FromAddress, string FromName);

// Password is optional on write — omit it to keep whatever's already
// stored (see SmtpSettingsRepository.UpsertAsync); send it to set/replace it.
public sealed record UpdateSmtpSettingsRequest(string Host, int Port, string Username, string? Password, bool EnableSsl, string FromAddress, string FromName);

public sealed record SendTestEmailRequest(string ToEmail);
