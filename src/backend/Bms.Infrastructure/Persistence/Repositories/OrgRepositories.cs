using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository(BmsDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<PagedResult<Tenant>> ListAsync(PageRequest request, CancellationToken ct)
    {
        var query = db.Tenants.AsNoTracking().OrderBy(t => t.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(t => t.Name.Contains(request.Search)).OrderBy(t => t.Name);
        return query.ToPagedResultAsync(request, ct);
    }

    public async Task<Tenant> AddAsync(Tenant tenant, CancellationToken ct)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct)
    {
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        db.Tenants.Update(tenant);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SiteRepository(BmsDbContext db) : ISiteRepository
{
    public Task<Site?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.Sites.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public Task<PagedResult<Site>> ListAsync(Guid? tenantId, PageRequest request, CancellationToken ct)
    {
        var query = db.Sites.AsNoTracking().AsQueryable();
        if (tenantId is { } id)
            query = query.Where(s => s.TenantId == id);
        query = query.OrderBy(s => s.Name);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(s => s.Name.Contains(request.Search)).OrderBy(s => s.Name);
        return query.ToPagedResultAsync(request, ct);
    }

    public async Task<Site> AddAsync(Site site, CancellationToken ct)
    {
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return site;
    }

    public async Task UpdateAsync(Site site, CancellationToken ct)
    {
        site.UpdatedAt = DateTimeOffset.UtcNow;
        db.Sites.Update(site);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class GatewayRepository(BmsDbContext db) : IGatewayRepository
{
    public Task<Gateway?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct) =>
          db.Gateways.Include(g => g.Site).Include(g => g.Tenant)
              .Include(g => g.Devices).ThenInclude(d => d.Site).ThenInclude(s => s.Tenant)
              .FirstOrDefaultAsync(g => g.Id == id && (tenantId == null || g.TenantId == tenantId), ct);

    public Task<PagedResult<Gateway>> ListAsync(Guid? tenantId, Guid? siteId, PageRequest request, CancellationToken ct)
    {
          var query = db.Gateways.AsNoTracking().Include(g => g.Site).Include(g => g.Tenant)
              .Include(g => g.Devices).ThenInclude(d => d.Site).ThenInclude(s => s.Tenant).AsQueryable();
        // Device creation may claim a standalone gateway. Keep unassigned
        // gateways visible while still restricting already-mapped gateways
        // to the requested tenant/site.
        if (tenantId is { } id)
            query = query.Where(g => g.TenantId == null || g.TenantId == id);
        if (siteId is { } sid)
            query = query.Where(g => g.SiteId == null || g.SiteId == sid);
        query = query.OrderBy(g => g.Name);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(g => g.Name.Contains(request.Search) || g.GatewayCode.Contains(request.Search)).OrderBy(g => g.Name);
        return query.ToPagedResultAsync(request, ct);
    }

    public async Task<Gateway> AddAsync(Gateway gateway, CancellationToken ct)
    {
        db.Gateways.Add(gateway);
        await db.SaveChangesAsync(ct);
        return gateway;
    }

    public async Task UpdateAsync(Gateway gateway, CancellationToken ct)
    {
        gateway.UpdatedAt = DateTimeOffset.UtcNow;
        db.Gateways.Update(gateway);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Gateway gateway, CancellationToken ct)
    {
        db.Gateways.Remove(gateway);
        await db.SaveChangesAsync(ct);
    }

    public Task<Gateway?> GetByCodeAsync(string gatewayCode, CancellationToken ct) =>
        db.Gateways.FirstOrDefaultAsync(g => g.GatewayCode == gatewayCode, ct);

    public async Task TouchAsync(string gatewayCode, DateTimeOffset seenAt, bool measurement, CancellationToken ct)
    {
          if (string.IsNullOrWhiteSpace(gatewayCode)) return;
          var gateway = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayCode == gatewayCode, ct);
          if (gateway is null)
          {
              gateway = new Gateway
              {
                  GatewayCode = gatewayCode.Trim(),
                  Name = gatewayCode.Trim(),
                  Status = GatewayStatus.Online,
                  LastSeenAt = seenAt,
                  LastMeasurementAt = measurement ? seenAt : null
              };
              db.Gateways.Add(gateway);
          }
          else
          {
              gateway.LastSeenAt = seenAt;
              gateway.Status = GatewayStatus.Online;
              if (measurement) gateway.LastMeasurementAt = seenAt;
          }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateHealthAsync(string gatewayCode, string? ipAddress, string? firmwareVersion, int? rssi, CancellationToken ct)
    {
        var gateway = await db.Gateways.FirstOrDefaultAsync(g => g.GatewayCode == gatewayCode, ct);
        if (gateway is null) return;
        gateway.IpAddress = ipAddress ?? gateway.IpAddress;
        gateway.FirmwareVersion = firmwareVersion ?? gateway.FirmwareVersion;
        gateway.Rssi = rssi ?? gateway.Rssi;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DeviceTypeRepository(BmsDbContext db) : IDeviceTypeRepository
{
    public async Task<IReadOnlyList<DeviceType>> ListAllAsync(CancellationToken ct) =>
        await db.DeviceTypes.AsNoTracking().Include(t => t.Parameters).OrderBy(t => t.Name).ToListAsync(ct);

    public Task<DeviceType?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.DeviceTypes.Include(t => t.Parameters).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<DeviceType> AddAsync(DeviceType deviceType, CancellationToken ct)
    {
        db.DeviceTypes.Add(deviceType);
        await db.SaveChangesAsync(ct);
        return deviceType;
    }

    public async Task<DeviceTypeParameter> AddParameterAsync(DeviceTypeParameter parameter, CancellationToken ct)
    {
        db.Set<DeviceTypeParameter>().Add(parameter);
        await db.SaveChangesAsync(ct);
        return parameter;
    }

    public async Task<DeviceTypeParameter?> UpdateParameterAsync(Guid deviceTypeId, Guid parameterId, Action<DeviceTypeParameter> update, CancellationToken ct)
    {
        var parameter = await db.Set<DeviceTypeParameter>()
            .FirstOrDefaultAsync(p => p.Id == parameterId && p.DeviceTypeId == deviceTypeId, ct);
        if (parameter is null)
            return null;

        update(parameter);
        await db.SaveChangesAsync(ct);
        return parameter;
    }

    public async Task<bool> RemoveParameterAsync(Guid deviceTypeId, Guid parameterId, CancellationToken ct)
    {
        var parameter = await db.Set<DeviceTypeParameter>()
            .FirstOrDefaultAsync(p => p.Id == parameterId && p.DeviceTypeId == deviceTypeId, ct);
        if (parameter is null)
            return false;

        db.Set<DeviceTypeParameter>().Remove(parameter);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> NameTakenAsync(string name, Guid? excludingId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        return db.DeviceTypes.AnyAsync(t => t.Name.ToLower() == lowered && (excludingId == null || t.Id != excludingId), ct);
    }

    public async Task UpdateAsync(DeviceType deviceType, CancellationToken ct)
    {
        deviceType.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeviceTypeUsage> GetUsageAsync(Guid id, CancellationToken ct) => new(
        await db.Devices.CountAsync(d => d.DeviceTypeId == id, ct),
        await db.Firmwares.CountAsync(f => f.DeviceTypeId == id, ct),
        await db.AlertRules.CountAsync(r => r.DeviceTypeId == id, ct),
        await db.CalculationRules.CountAsync(r => r.DeviceTypeId == id, ct));

    public async Task DeleteAsync(DeviceType deviceType, CancellationToken ct)
    {
        db.DeviceTypes.Remove(deviceType);
        await db.SaveChangesAsync(ct);
    }
}
