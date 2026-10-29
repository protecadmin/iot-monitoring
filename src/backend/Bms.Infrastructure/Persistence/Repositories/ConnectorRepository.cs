using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Connectors;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class ConnectorRepository(BmsDbContext db) : IConnectorRepository
{
    public async Task<IReadOnlyList<Connector>> ListAsync(CancellationToken ct) =>
        await db.Connectors.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Connector?> GetAsync(Guid id, CancellationToken ct) => db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Connector connector, CancellationToken ct)
    {
        db.Connectors.Add(connector);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Connector connector, CancellationToken ct)
    {
        connector.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Connector connector, CancellationToken ct)
    {
        db.Connectors.Remove(connector);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> PortTakenAsync(ConnectorProtocol protocol, int port, Guid? exceptId, CancellationToken ct) =>
        db.Connectors.AnyAsync(c => c.Protocol == protocol && c.Port == port && c.Id != exceptId, ct);

    public Task<bool> PrefixTakenAsync(string prefix, Guid? exceptId, CancellationToken ct) =>
        db.Connectors.AnyAsync(c => c.DeviceCodePrefix == prefix && c.Id != exceptId, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountDevicesAsync(CancellationToken ct) =>
        await db.Devices.Where(d => d.ConnectorId != null)
            .GroupBy(d => d.ConnectorId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyList<ConnectorDeviceEntry>> ListDeviceEntriesAsync(CancellationToken ct) =>
        await (from d in db.Devices.AsNoTracking()
               where d.Connection == DeviceConnection.Connector && d.ConnectorId != null && d.SensorId != null
                     && d.Status != DeviceStatus.Decommissioned
               join s in db.Sites on d.SiteId equals s.Id
               join t in db.Tenants on d.TenantId equals t.Id
               select new ConnectorDeviceEntry(d.ConnectorId!.Value, d.SensorId!, d.DeviceCode, t.Slug, s.Code))
            .ToListAsync(ct);

    public async Task RecordUnknownDevicesAsync(IReadOnlyList<ConnectorSighting> sightings, CancellationToken ct)
    {
        foreach (var s in sightings)
        {
            var row = await db.ConnectorUnknownDevices.FirstOrDefaultAsync(u => u.ConnectorId == s.ConnectorId && u.SensorId == s.Key, ct);
            if (row is null)
            {
                db.ConnectorUnknownDevices.Add(new ConnectorUnknownDevice
                {
                    ConnectorId = s.ConnectorId, SensorId = s.Key, FirstSeenAt = s.LastSeenAt,
                    LastSeenAt = s.LastSeenAt, MessageCount = s.Count, LastSample = Truncate(s.Sample, 512)
                });
            }
            else
            {
                row.MessageCount += s.Count;
                if (s.LastSeenAt > row.LastSeenAt) row.LastSeenAt = s.LastSeenAt;
                row.LastSample = Truncate(s.Sample, 512) ?? row.LastSample;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConnectorUnknownDevice>> ListUnknownDevicesAsync(CancellationToken ct) =>
        await db.ConnectorUnknownDevices.AsNoTracking().OrderByDescending(u => u.LastSeenAt).ToListAsync(ct);

    public Task DeleteUnknownDeviceAsync(Guid connectorId, string sensorId, CancellationToken ct) =>
        db.ConnectorUnknownDevices.Where(u => u.ConnectorId == connectorId && u.SensorId == sensorId).ExecuteDeleteAsync(ct);

    public async Task RecordUnmappedKeysAsync(IReadOnlyList<ConnectorSighting> sightings, CancellationToken ct)
    {
        foreach (var s in sightings)
        {
            var row = await db.ConnectorUnmappedKeys.FirstOrDefaultAsync(u => u.ConnectorId == s.ConnectorId && u.Source == s.Key, ct);
            if (row is null)
            {
                db.ConnectorUnmappedKeys.Add(new ConnectorUnmappedKey
                {
                    ConnectorId = s.ConnectorId, Source = s.Key, MessageCount = s.Count,
                    LastSeenAt = s.LastSeenAt, LastValue = Truncate(s.Sample, 128)
                });
            }
            else
            {
                row.MessageCount += s.Count;
                if (s.LastSeenAt > row.LastSeenAt) row.LastSeenAt = s.LastSeenAt;
                row.LastValue = Truncate(s.Sample, 128) ?? row.LastValue;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConnectorUnmappedKey>> ListUnmappedKeysAsync(Guid? connectorId, CancellationToken ct) =>
        await db.ConnectorUnmappedKeys.AsNoTracking()
            .Where(u => connectorId == null || u.ConnectorId == connectorId)
            .OrderByDescending(u => u.MessageCount).ToListAsync(ct);

    public Task DeleteUnmappedKeysAsync(Guid connectorId, IReadOnlyCollection<string> sources, CancellationToken ct) =>
        db.ConnectorUnmappedKeys.Where(u => u.ConnectorId == connectorId && sources.Contains(u.Source)).ExecuteDeleteAsync(ct);

    public Task<ConnectorServiceStatus?> GetStatusAsync(CancellationToken ct) =>
        db.ConnectorServiceStatuses.OrderByDescending(s => s.LastHeartbeatAt).FirstOrDefaultAsync(ct);

    public async Task SaveStatusAsync(ConnectorServiceStatus status, CancellationToken ct)
    {
        if (db.Entry(status).State == EntityState.Detached)
            db.ConnectorServiceStatuses.Add(status);
        status.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddCaptureAsync(ConnectorCapture capture, CancellationToken ct)
    {
        db.ConnectorCaptures.Add(capture);
        await db.SaveChangesAsync(ct);
    }

    public Task<ConnectorCapture?> GetCaptureAsync(Guid id, CancellationToken ct) =>
        db.ConnectorCaptures.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<ConnectorCapture>> ListPendingCapturesAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.ConnectorCaptures.AsNoTracking().Where(c => c.CapturedAt == null && c.ExpiresAt > now).ToListAsync(ct);

    public async Task<bool> CompleteCaptureAsync(Guid id, string sample, bool isHex, DateTimeOffset at, CancellationToken ct) =>
        await db.ConnectorCaptures.Where(c => c.Id == id && c.CapturedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CapturedAt, at).SetProperty(c => c.Sample, Truncate(sample, 8192)).SetProperty(c => c.SampleIsHex, isHex), ct) > 0;

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
