using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IHubRepository
{
    Task<Hub?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct);
    Task<PagedResult<Hub>> ListAsync(Guid? tenantId, Guid? siteId, PageRequest request, CancellationToken ct);
    Task<Hub> AddAsync(Hub hub, CancellationToken ct);
    Task UpdateAsync(Hub hub, CancellationToken ct);
    Task DeleteAsync(Hub hub, CancellationToken ct);
    Task<Hub?> GetByHubIdAsync(string hubId, CancellationToken ct);
    Task TouchAsync(string hubId, DateTimeOffset seenAt, CancellationToken ct);
    Task UpdateHeartbeatAsync(string hubId, DateTimeOffset heartbeatAt, long? uptimeSeconds, CancellationToken ct);
    Task UpdateSnapshotAsync(string hubId, string? ipAddress, string? appVersion, string? agentVersion, string? securityMode, int? rssi, long? uptimeSeconds, string? statusJson, CancellationToken ct);
    Task UpdateReportedConfigAsync(string hubId, int revision, string? configHash, string? displayMode, int? rotationSeconds, int? welcomeSeconds, string? historyRange, string? configJson, CancellationToken ct);
    Task UpdateDesiredConfigAsync(Guid id, int desiredRevision, string? desiredJson, CancellationToken ct);
    Task UpdateStatusAsync(string hubId, HubStatus status, DateTimeOffset seenAt, CancellationToken ct);
}

