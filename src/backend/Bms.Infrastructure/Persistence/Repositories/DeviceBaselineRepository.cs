using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class DeviceBaselineRepository(BmsDbContext db) : IDeviceBaselineRepository
{
    public async Task<IReadOnlyList<DeviceBaseline>> ListAsync(Guid deviceId, CancellationToken ct) =>
        await db.DeviceBaselines.AsNoTracking().Where(b => b.DeviceId == deviceId).OrderBy(b => b.ParameterKey).ToListAsync(ct);

    public Task<DeviceBaseline?> GetAsync(Guid deviceId, string parameterKey, CancellationToken ct) =>
        db.DeviceBaselines.AsNoTracking().FirstOrDefaultAsync(b => b.DeviceId == deviceId && b.ParameterKey == parameterKey, ct);

    public async Task SaveAsync(Guid deviceId, IReadOnlyList<DeviceBaseline> baselines, CancellationToken ct)
    {
        var keys = baselines.Select(b => b.ParameterKey).ToList();
        await db.DeviceBaselines.Where(b => b.DeviceId == deviceId && keys.Contains(b.ParameterKey)).ExecuteDeleteAsync(ct);
        db.DeviceBaselines.AddRange(baselines);
        await db.SaveChangesAsync(ct);
    }

    public Task<int> DeleteAsync(Guid deviceId, string? parameterKey, CancellationToken ct) =>
        db.DeviceBaselines.Where(b => b.DeviceId == deviceId && (parameterKey == null || b.ParameterKey == parameterKey)).ExecuteDeleteAsync(ct);
}
