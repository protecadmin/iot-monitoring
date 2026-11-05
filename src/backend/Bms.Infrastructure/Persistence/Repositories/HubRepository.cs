using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class HubRepository(BmsDbContext db) : IHubRepository
{
    public Task<Hub?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct) =>
        db.Hubs.Include(h => h.Site).Include(h => h.Tenant)
            .FirstOrDefaultAsync(h => h.Id == id && (tenantId == null || h.TenantId == tenantId), ct);

    public Task<PagedResult<Hub>> ListAsync(Guid? tenantId, Guid? siteId, PageRequest request, CancellationToken ct)
    {
        var query = db.Hubs.AsNoTracking().Include(h => h.Site).Include(h => h.Tenant).AsQueryable();

        if (tenantId is { } id)
            query = query.Where(h => h.TenantId == null || h.TenantId == id);
        if (siteId is { } sid)
            query = query.Where(h => h.SiteId == null || h.SiteId == sid);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(h => h.Name.Contains(search) || h.HubId.Contains(search));
        }

        query = query.OrderBy(h => h.Name);
        return query.ToPagedResultAsync(request, ct);
    }

    public async Task<Hub> AddAsync(Hub hub, CancellationToken ct)
    {
        db.Hubs.Add(hub);
        await db.SaveChangesAsync(ct);
        return hub;
    }

    public async Task UpdateAsync(Hub hub, CancellationToken ct)
    {
        hub.UpdatedAt = DateTimeOffset.UtcNow;
        db.Hubs.Update(hub);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Hub hub, CancellationToken ct)
    {
        db.Hubs.Remove(hub);
        await db.SaveChangesAsync(ct);
    }

    public Task<Hub?> GetByHubIdAsync(string hubId, CancellationToken ct) =>
        db.Hubs.Include(h => h.Site).Include(h => h.Tenant)
            .FirstOrDefaultAsync(h => h.HubId == hubId.Trim(), ct);

    public async Task TouchAsync(string hubId, DateTimeOffset seenAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hubId)) return;
        var cleanId = hubId.Trim();
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.HubId == cleanId, ct);
        if (hub is null)
        {
            hub = new Hub
            {
                HubId = cleanId,
                Name = $"HUB-{cleanId}",
                Status = HubStatus.Online,
                LastSeenAt = seenAt
            };
            db.Hubs.Add(hub);
        }
        else
        {
            hub.LastSeenAt = seenAt;
            hub.Status = HubStatus.Online;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateHeartbeatAsync(string hubId, DateTimeOffset heartbeatAt, long? uptimeSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hubId)) return;
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.HubId == hubId.Trim(), ct);
        if (hub is null)
        {
            hub = new Hub
            {
                HubId = hubId.Trim(),
                Name = $"HUB-{hubId.Trim()}",
                Status = HubStatus.Online,
                LastSeenAt = heartbeatAt,
                LastHeartbeatAt = heartbeatAt,
                UptimeSeconds = uptimeSeconds
            };
            db.Hubs.Add(hub);
        }
        else
        {
            hub.LastSeenAt = heartbeatAt;
            hub.LastHeartbeatAt = heartbeatAt;
            hub.Status = HubStatus.Online;
            if (uptimeSeconds.HasValue) hub.UptimeSeconds = uptimeSeconds.Value;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSnapshotAsync(string hubId, string? ipAddress, string? appVersion, string? agentVersion, string? securityMode, int? rssi, long? uptimeSeconds, string? statusJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hubId)) return;
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.HubId == hubId.Trim(), ct);
        if (hub is null)
        {
            hub = new Hub
            {
                HubId = hubId.Trim(),
                Name = $"HUB-{hubId.Trim()}",
                Status = HubStatus.Online,
                LastSeenAt = DateTimeOffset.UtcNow,
                IpAddress = ipAddress,
                AppVersion = appVersion,
                AgentVersion = agentVersion,
                SecurityMode = securityMode,
                Rssi = rssi,
                UptimeSeconds = uptimeSeconds,
                LastStatusJson = statusJson
            };
            db.Hubs.Add(hub);
        }
        else
        {
            hub.LastSeenAt = DateTimeOffset.UtcNow;
            hub.Status = HubStatus.Online;
            if (!string.IsNullOrWhiteSpace(ipAddress)) hub.IpAddress = ipAddress;
            if (!string.IsNullOrWhiteSpace(appVersion)) hub.AppVersion = appVersion;
            if (!string.IsNullOrWhiteSpace(agentVersion)) hub.AgentVersion = agentVersion;
            if (!string.IsNullOrWhiteSpace(securityMode)) hub.SecurityMode = securityMode;
            if (rssi.HasValue) hub.Rssi = rssi;
            if (uptimeSeconds.HasValue) hub.UptimeSeconds = uptimeSeconds.Value;
            if (!string.IsNullOrWhiteSpace(statusJson)) hub.LastStatusJson = statusJson;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateReportedConfigAsync(string hubId, int revision, string? configHash, string? displayMode, int? rotationSeconds, int? welcomeSeconds, string? historyRange, string? configJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hubId)) return;
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.HubId == hubId.Trim(), ct);
        if (hub is null) return;

        hub.ConfigRevision = revision;
        if (!string.IsNullOrWhiteSpace(configHash)) hub.ConfigHash = configHash;
        if (!string.IsNullOrWhiteSpace(displayMode)) hub.DisplayMode = displayMode;
        if (rotationSeconds.HasValue) hub.RotationSeconds = rotationSeconds;
        if (welcomeSeconds.HasValue) hub.WelcomeSeconds = welcomeSeconds;
        if (!string.IsNullOrWhiteSpace(historyRange)) hub.HistoryRange = historyRange;
        if (!string.IsNullOrWhiteSpace(configJson)) hub.ReportedConfigJson = configJson;
        hub.LastConfigSyncAt = DateTimeOffset.UtcNow;
        hub.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateDesiredConfigAsync(Guid id, int desiredRevision, string? desiredJson, CancellationToken ct)
    {
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hub is null) return;

        hub.DesiredConfigJson = desiredJson;
        hub.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(string hubId, HubStatus status, DateTimeOffset seenAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hubId)) return;
        var hub = await db.Hubs.FirstOrDefaultAsync(h => h.HubId == hubId.Trim(), ct);
        if (hub is null) return;
        hub.Status = status;
        hub.LastSeenAt = seenAt;
        await db.SaveChangesAsync(ct);
    }
}

