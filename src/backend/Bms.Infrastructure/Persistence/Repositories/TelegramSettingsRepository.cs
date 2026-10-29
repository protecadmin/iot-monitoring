using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class TelegramSettingsRepository(BmsDbContext db) : ITelegramSettingsRepository
{
    public Task<TelegramSettings?> GetAsync(CancellationToken ct) =>
        db.TelegramSettings.FirstOrDefaultAsync(s => s.Id == TelegramSettings.SingletonId, ct);

    public async Task<TelegramSettings> UpsertAsync(TelegramSettings settings, CancellationToken ct)
    {
        var existing = await db.TelegramSettings.FirstOrDefaultAsync(s => s.Id == TelegramSettings.SingletonId, ct);
        if (existing is null)
        {
            settings.Id = TelegramSettings.SingletonId;
            db.TelegramSettings.Add(settings);
            await db.SaveChangesAsync(ct);
            return settings;
        }

        // Only overwrite the stored token if a new one was actually provided
        // — same "null means leave it alone" convention as SmtpSettings.
        if (settings.EncryptedBotToken is not null)
            existing.EncryptedBotToken = settings.EncryptedBotToken;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }
}
