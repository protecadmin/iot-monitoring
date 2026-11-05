using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<PagedResult<Tenant>> ListAsync(PageRequest request, CancellationToken ct);
    Task<Tenant> AddAsync(Tenant tenant, CancellationToken ct);
    Task UpdateAsync(Tenant tenant, CancellationToken ct);
}

public interface ISiteRepository
{
    Task<Site?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>Null tenantId lists across every tenant — only valid for a SuperAdmin caller (see SitesController).</summary>
    Task<PagedResult<Site>> ListAsync(Guid? tenantId, PageRequest request, CancellationToken ct);
    Task<Site> AddAsync(Site site, CancellationToken ct);
    Task UpdateAsync(Site site, CancellationToken ct);
}

public interface IGatewayRepository
{
    Task<Gateway?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct);

    /// <summary>Null tenantId lists across every tenant — only valid for a SuperAdmin caller (see GatewaysController). siteId further narrows to one site.</summary>
    Task<PagedResult<Gateway>> ListAsync(Guid? tenantId, Guid? siteId, PageRequest request, CancellationToken ct);
    Task<Gateway> AddAsync(Gateway gateway, CancellationToken ct);
    Task UpdateAsync(Gateway gateway, CancellationToken ct);
    Task DeleteAsync(Gateway gateway, CancellationToken ct);
    Task<Gateway?> GetByCodeAsync(string gatewayCode, CancellationToken ct);
    Task TouchAsync(string gatewayCode, DateTimeOffset seenAt, bool measurement, CancellationToken ct);
    Task UpdateHealthAsync(string gatewayCode, string? ipAddress, string? firmwareVersion, int? rssi, CancellationToken ct);
}

public interface IDeviceTypeRepository
{
    /// <summary>Always includes Parameters — every caller needs the allow-list, and the list is small.</summary>
    Task<IReadOnlyList<DeviceType>> ListAllAsync(CancellationToken ct);
    Task<DeviceType?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DeviceType> AddAsync(DeviceType deviceType, CancellationToken ct);
    Task<DeviceTypeParameter> AddParameterAsync(DeviceTypeParameter parameter, CancellationToken ct);

    /// <summary>Applies <paramref name="update"/> and saves. Null if no parameter with this id exists on this device type. Callers must not change the key — see DeviceTypesController.UpdateParameter for why.</summary>
    Task<DeviceTypeParameter?> UpdateParameterAsync(Guid deviceTypeId, Guid parameterId, Action<DeviceTypeParameter> update, CancellationToken ct);
    Task<bool> RemoveParameterAsync(Guid deviceTypeId, Guid parameterId, CancellationToken ct);

    /// <summary>Case-insensitive, so "noise sensor" and "Noise Sensor" can't coexist.</summary>
    Task<bool> NameTakenAsync(string name, Guid? excludingId, CancellationToken ct);
    Task UpdateAsync(DeviceType deviceType, CancellationToken ct);

    /// <summary>Everything that still points at a device type and would break (or silently stop matching) if it were deleted.</summary>
    Task<DeviceTypeUsage> GetUsageAsync(Guid id, CancellationToken ct);

    /// <summary>Removes the type and, by cascade, its parameters. Callers check <see cref="GetUsageAsync"/> first.</summary>
    Task DeleteAsync(DeviceType deviceType, CancellationToken ct);
}

public sealed record DeviceTypeUsage(int Devices, int Firmwares, int AlertRules, int CalculationRules)
{
    public bool InUse => Devices + Firmwares + AlertRules + CalculationRules > 0;
}
