using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

/// <summary>Always operates on the single TelegramSettings.SingletonId row — see that entity for why.</summary>
public interface ITelegramSettingsRepository
{
    Task<TelegramSettings?> GetAsync(CancellationToken ct);
    Task<TelegramSettings> UpsertAsync(TelegramSettings settings, CancellationToken ct);
}
