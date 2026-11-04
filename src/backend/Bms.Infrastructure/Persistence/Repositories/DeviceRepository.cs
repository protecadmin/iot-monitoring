using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class DeviceRepository(BmsDbContext db) : IDeviceRepository
{
    public Task<Device?> GetByMqttTopicAsync(string topic, CancellationToken ct)
    {
        var gatewayId = MqttTelemetryTopicRules.GatewayIdFrom(topic);
        return db.Devices.FirstOrDefaultAsync(d => d.Connection == DeviceConnection.MqttDirect &&
            (d.MqttTelemetryTopic == topic || (gatewayId != null && d.MqttTelemetryTopic == null && d.DeviceCode == gatewayId)), ct);
    }

    public async Task<IReadOnlyList<string>> ListMqttTopicsAsync(CancellationToken ct) =>
        await db.Devices.AsNoTracking().Where(d => d.Connection == DeviceConnection.MqttDirect)
            .Select(d => d.MqttTelemetryTopic ?? "protec/gateway/" + d.DeviceCode + "/measurement").ToListAsync(ct);

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Devices.Include(d => d.Gateway).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Device?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.Devices.Include(d => d.Gateway).FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, ct);

    public Task<Device?> GetByCodeAsync(Guid tenantId, string deviceCode, CancellationToken ct) =>
        db.Devices.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.DeviceCode == deviceCode, ct);

    public Task<Device?> GetByDeviceCodeAsync(string deviceCode, CancellationToken ct) =>
        db.Devices.FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, ct);

    public Task<Device?> GetByCodesAsync(string tenantSlug, string siteCode, string deviceCode, CancellationToken ct) =>
        db.Devices
            .Include(d => d.Site)
            .ThenInclude(s => s!.Tenant)
            .FirstOrDefaultAsync(d =>
                d.DeviceCode == deviceCode &&
                d.Site!.Code == siteCode &&
                d.Site.Tenant!.Slug == tenantSlug, ct);

    public Task<PagedResult<Device>> ListAsync(DeviceFilter filter, CancellationToken ct)
    {
        var query = db.Devices.AsNoTracking().Include(d => d.Gateway).AsQueryable();

        if (filter.TenantId is { } tenantId) query = query.Where(d => d.TenantId == tenantId);
        if (filter.SiteId is { } siteId) query = query.Where(d => d.SiteId == siteId);
        if (filter.DeviceTypeId is { } deviceTypeId) query = query.Where(d => d.DeviceTypeId == deviceTypeId);
        if (filter.Transport is { } transport) query = query.Where(d => d.Transport == transport);
        if (filter.Protocol is { } protocol) query = query.Where(d => d.Protocol == protocol);
        if (filter.Status is { } status) query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.Name.Contains(filter.Search) || d.DeviceCode.Contains(filter.Search) || (d.SerialNumber != null && d.SerialNumber.Contains(filter.Search)));

        query = (filter.SortBy?.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("name", true) => query.OrderByDescending(d => d.Name),
            ("name", false) => query.OrderBy(d => d.Name),
            ("status", true) => query.OrderByDescending(d => d.Status),
            ("status", false) => query.OrderBy(d => d.Status),
            ("lastseenat", true) => query.OrderByDescending(d => d.LastSeenAt),
            ("lastseenat", false) => query.OrderBy(d => d.LastSeenAt),
            _ => query.OrderBy(d => d.Name)
        };

        return query.ToPagedResultAsync(filter, ct);
    }

    public async Task<IReadOnlyList<Device>> ListForOfflineSweepAsync(CancellationToken ct) =>
        // Offline devices stay in, so Device Offline can fire once they have
        // been silent long enough; never-connected ones, for Never Connected.
        await db.Devices.Include(d => d.DeviceType).Where(d => d.Status == DeviceStatus.Online || d.Status == DeviceStatus.Warning || d.Status == DeviceStatus.Offline
                                    || d.Status == DeviceStatus.Registered || d.Status == DeviceStatus.Provisioned).ToListAsync(ct);

    public async Task<Device> AddAsync(Device device, CancellationToken ct)
    {
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return device;
    }

    public async Task UpdateAsync(Device device, CancellationToken ct)
    {
        device.UpdatedAt = DateTimeOffset.UtcNow;
        db.Devices.Update(device);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var device = await GetByIdAsync(tenantId, id, ct);
        if (device is null) return;
        db.Devices.Remove(device);
        await db.SaveChangesAsync(ct);
    }
}
