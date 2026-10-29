using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IMqttCredentialRepository
{
    /// <summary>Always includes Tenant — every caller needs the tenant name/slug alongside the credential.</summary>
    Task<IReadOnlyList<MqttCredential>> ListAsync(CancellationToken ct);
    Task<MqttCredential?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MqttCredential?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task<MqttCredential> AddAsync(MqttCredential credential, CancellationToken ct);
    Task UpdateAsync(MqttCredential credential, CancellationToken ct);
    Task DeleteAsync(MqttCredential credential, CancellationToken ct);
}
