using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

/// <summary>Always operates on the single SmtpSettings.SingletonId row — see that entity for why.</summary>
public interface ISmtpSettingsRepository
{
    Task<SmtpSettings?> GetAsync(CancellationToken ct);
    Task<SmtpSettings> UpsertAsync(SmtpSettings settings, CancellationToken ct);
}
