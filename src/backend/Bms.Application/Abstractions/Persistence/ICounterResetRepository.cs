using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

/// <summary>
/// Points where a device's counter parameter was re-based, so a Totalizer
/// Delta is never computed across one (see TotalizerDeltaHandler).
/// </summary>
public interface ICounterResetRepository
{
    Task AddRangeAsync(IReadOnlyCollection<CounterReset> resets, CancellationToken ct);

    /// <summary>Reset points for one device's counter, oldest first, optionally only those at or before <paramref name="upTo"/>.</summary>
    Task<IReadOnlyList<DateTimeOffset>> ListAsync(Guid deviceId, string parameterKey, DateTimeOffset? upTo, CancellationToken ct);

    /// <summary>Whether a reset lies after <paramref name="after"/> and at or before <paramref name="upTo"/>.</summary>
    Task<bool> AnyBetweenAsync(Guid deviceId, string parameterKey, DateTimeOffset after, DateTimeOffset upTo, CancellationToken ct);
}
