using Bms.Application.Abstractions.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class MqttCredentialRepository(BmsDbContext db) : IMqttCredentialRepository
{
    public async Task<IReadOnlyList<MqttCredential>> ListAsync(CancellationToken ct) =>
        await db.MqttCredentials.AsNoTracking().Include(c => c.Tenant).OrderBy(c => c.Username).ToListAsync(ct);

    public Task<MqttCredential?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.MqttCredentials.Include(c => c.Tenant).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<MqttCredential?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct) =>
        db.MqttCredentials.Include(c => c.Tenant).FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task<MqttCredential> AddAsync(MqttCredential credential, CancellationToken ct)
    {
        db.MqttCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
        return credential;
    }

    public async Task UpdateAsync(MqttCredential credential, CancellationToken ct)
    {
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        db.MqttCredentials.Update(credential);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(MqttCredential credential, CancellationToken ct)
    {
        db.MqttCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);
    }
}
