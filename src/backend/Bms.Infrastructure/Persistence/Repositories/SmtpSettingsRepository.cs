using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class SmtpSettingsRepository(BmsDbContext db) : ISmtpSettingsRepository
{
    public Task<SmtpSettings?> GetAsync(CancellationToken ct) =>
        db.SmtpSettings.FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);

    public async Task<SmtpSettings> UpsertAsync(SmtpSettings settings, CancellationToken ct)
    {
        var existing = await db.SmtpSettings.FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);
        if (existing is null)
        {
            settings.Id = SmtpSettings.SingletonId;
            db.SmtpSettings.Add(settings);
            await db.SaveChangesAsync(ct);
            return settings;
        }

        existing.Host = settings.Host;
        existing.Port = settings.Port;
        existing.Username = settings.Username;
        // Only overwrite the stored password if a new one was actually
        // provided — see SmtpSettingsController.Update for why (so saving
        // other fields doesn't require re-entering the password every time).
        if (settings.EncryptedPassword is not null)
            existing.EncryptedPassword = settings.EncryptedPassword;
        existing.EnableSsl = settings.EnableSsl;
        existing.FromAddress = settings.FromAddress;
        existing.FromName = settings.FromName;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }
}
