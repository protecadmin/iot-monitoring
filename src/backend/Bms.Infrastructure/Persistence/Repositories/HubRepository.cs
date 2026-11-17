using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class HubRepository(BmsDbContext db) : IHubRepository
{
    public async Task CaptureMessageAsync(string hubId, HubCapturedMessage message, CancellationToken ct)
    {
        var hub = await GetByHubIdAsync(hubId, ct);
        if (hub is null) return;
        var action = "Hub.MessageV2." + message.Topic.Split('/').Last();
        var entry = await db.AuditLogs.FirstOrDefaultAsync(l => l.EntityType == nameof(Hub) && l.EntityId == hub.Id.ToString() && l.Action == action, ct);
        if (entry is null)
        {
            entry = new AuditLog { TenantId = hub.TenantId, EntityType = nameof(Hub), EntityId = hub.Id.ToString(), Action = action };
            db.AuditLogs.Add(entry);
        }
        entry.DetailsJson = JsonSerializer.Serialize(message);
        entry.UpdatedAt = message.ReceivedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<HubMonitor> GetMonitorAsync(Guid id, DateTimeOffset dayStart, CancellationToken ct)
    {
        dayStart = dayStart.ToUniversalTime();
        var captured = await db.AuditLogs.AsNoTracking().Where(l => l.EntityType == nameof(Hub) && l.EntityId == id.ToString() && l.Action.StartsWith("Hub.MessageV2.")).ToListAsync(ct);
        var today = await CommandLogs(id).AsNoTracking().Where(l => l.CreatedAt >= dayStart).ToListAsync(ct);
        var receipts = today.Select(l => JsonSerializer.Deserialize<HubCommandReceipt>(l.DetailsJson!)!).ToList();
        var completed = receipts.Where(c => c.State is "applied" or "succeeded" or "rejected" or "publish_failed" || (c.Deadline < DateTimeOffset.UtcNow && !(c.AcknowledgedAt != null && c.Action is "logo.begin" or "logo.chunk"))).ToList();
        return new HubMonitor(captured.Select(l => JsonSerializer.Deserialize<HubCapturedMessage>(l.DetailsJson!)!).ToList(), today.Count,
            completed.Count == 0 ? null : Math.Round(100.0 * completed.Count(c => c.State is "applied" or "succeeded") / completed.Count, 1));
    }

    public async Task<IReadOnlyList<HubCommandReceipt>> GetCommandsAsync(Guid id, CancellationToken ct)
    {
        var logs = await CommandLogs(id).AsNoTracking().Take(100).ToListAsync(ct);
        var actorIds = logs.Where(l => l.UserId != null).Select(l => l.UserId!.Value).Distinct().ToArray();
        var actors = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return logs.Select(l => JsonSerializer.Deserialize<HubCommandReceipt>(l.DetailsJson!)! with { Actor = l.UserId is { } uid && actors.TryGetValue(uid, out var name) ? name : "Unavailable" })
            .Select(c => (c.State is "pending" or "received" or "accepted" or "awaiting_snapshot") && c.Deadline < DateTimeOffset.UtcNow && !(c.AcknowledgedAt != null && c.Action is "logo.begin" or "logo.chunk")
                ? c with { State = "timed_out", Message = "No confirmed device result before the deadline. Refresh state before retrying." } : c).ToList();
    }

    private IOrderedQueryable<AuditLog> CommandLogs(Guid id) => db.AuditLogs
        .Where(l => l.EntityType == nameof(Hub) && l.EntityId == id.ToString() && l.Action == "Hub.CommandV2")
        .OrderByDescending(l => l.CreatedAt);

    public async Task<HubCommandReceipt?> GetCommandAsync(Guid id, string commandId, CancellationToken ct)
    {
        var key = Guid.ParseExact(commandId[4..], "N");
        var log = await CommandLogs(id).AsNoTracking().FirstOrDefaultAsync(l => l.Id == key, ct);
        return log is null ? null : JsonSerializer.Deserialize<HubCommandReceipt>(log.DetailsJson!);
    }

    public async Task<bool> RecordCommandAsync(Guid id, Guid? tenantId, Guid? actorId, HubCommandReceipt command, CancellationToken ct)
    {
        // Primary-key uniqueness makes request reuse fail before a second MQTT publish, including races.
        var key = Guid.ParseExact(command.CommandId[4..], "N");
        if (await db.AuditLogs.AnyAsync(l => l.Id == key, ct)) return false;
        var entry = new AuditLog { Id = key, TenantId = tenantId, UserId = actorId, EntityType = nameof(Hub), EntityId = id.ToString(),
            Action = "Hub.CommandV2", DetailsJson = JsonSerializer.Serialize(command) };
        db.AuditLogs.Add(entry);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkPublishFailureAsync(Guid id, string commandId, CancellationToken ct)
    {
        var key = Guid.ParseExact(commandId[4..], "N");
        var log = await CommandLogs(id).FirstOrDefaultAsync(l => l.Id == key, ct);
        if (log is null) return;
        var receipt = JsonSerializer.Deserialize<HubCommandReceipt>(log.DetailsJson!)!;
        // A transport error is not a device rejection; delivery may have happened before the connection dropped.
        if (receipt.AcknowledgedAt != null) return;
        log.DetailsJson = JsonSerializer.Serialize(receipt with { State = "publish_failed", Code = "publish_failed", Message = "Cloud publish failed; device delivery is unconfirmed. Request a fresh snapshot before retrying." });
        await db.SaveChangesAsync(ct);
    }

    public async Task ProcessCommandReplyAsync(string hubId, string subtopic, string json, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var hub = await GetByHubIdAsync(hubId, ct);
        if (hub is null || JsonNode.Parse(json) is not JsonObject packet || packet["v"]?.GetValue<int>() != 2 || packet["hub"]?.GetValue<string>() != hubId) return;
        var logs = await CommandLogs(hub.Id).Take(100).ToListAsync(ct);
        foreach (var log in logs)
        {
            var c = JsonSerializer.Deserialize<HubCommandReceipt>(log.DetailsJson!)!;
            if (subtopic == "ack" && packet["id"]?.GetValue<string>() == c.CommandId && packet["op"]?.GetValue<string>() == c.Action)
            {
                var state = packet["state"]?.GetValue<string>();
                if (state is not ("received" or "accepted" or "applied" or "succeeded" or "rejected")) continue;
                // Duplicate or delayed intermediate ACKs must not undo a terminal result.
                if (c.State is "applied" or "succeeded" or "rejected" || (c.State == "awaiting_snapshot" && state is "received" or "accepted")) continue;
                state = state == "applied" ? "awaiting_snapshot" : state;
                if (state == "succeeded" && c.Action is "site.set" or "display.set" or "api.set" or "threshold.set" or "tv.select" or "logo.commit") state = "awaiting_snapshot";
                c = c with { State = state, Code = packet["code"]?.GetValue<string>(), Message = packet["message"]?.GetValue<string>(),
                    AcknowledgedAt = receivedAt, DeviceTimeUtc = packet["time_utc"]?.GetValue<string>(), AppliedVersion = packet["config_version"]?.GetValue<int>() };
            }
            var snapshot = subtopic == "snapshot" ? packet : hub.LastStatusJson is { } saved ? JsonNode.Parse(saved) as JsonObject : null;
            if (c.State == "awaiting_snapshot" && snapshot is not null && c.AppliedVersion is { } version && snapshot["config_version"]?.GetValue<int>() == version && HubProtocol.Matches(snapshot, c.Action, c.Data))
                c = c with { State = "applied", Message = "Device acknowledgement and authoritative snapshot match." };
            if (c.State == "awaiting_snapshot" && c.Action == "logo.commit" && snapshot?["branding"]?["has_client_logo"]?.GetValue<bool>() == true && snapshot["branding"]?["client_logo_revision"]?.ToJsonString() != c.Data["previous_logo_revision"]?.ToJsonString() && snapshot["config_version"]?.GetValue<int>() == c.AppliedVersion)
                c = c with { State = "applied", Message = "Client logo revision confirmed in snapshot." };
            log.DetailsJson = JsonSerializer.Serialize(c);
        }
        await db.SaveChangesAsync(ct);
    }

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

