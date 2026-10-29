using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class CounterResetRepository(BmsDbContext db) : ICounterResetRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<CounterReset> resets, CancellationToken ct)
    {
        if (resets.Count == 0) return;
        foreach (var r in resets) r.EffectiveAt = r.EffectiveAt.ToUniversalTime();
        db.CounterResets.AddRange(resets);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ListAsync(Guid deviceId, string parameterKey, DateTimeOffset? upTo, CancellationToken ct)
    {
        var until = upTo?.ToUniversalTime();
        return await db.CounterResets.AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ParameterKey == parameterKey && (until == null || r.EffectiveAt <= until))
            .OrderBy(r => r.EffectiveAt)
            .Select(r => r.EffectiveAt)
            .ToListAsync(ct);
    }

    public Task<bool> AnyBetweenAsync(Guid deviceId, string parameterKey, DateTimeOffset after, DateTimeOffset upTo, CancellationToken ct)
    {
        var from = after.ToUniversalTime();
        var to = upTo.ToUniversalTime();
        return db.CounterResets.AnyAsync(r => r.DeviceId == deviceId && r.ParameterKey == parameterKey && r.EffectiveAt > from && r.EffectiveAt <= to, ct);
    }
}
