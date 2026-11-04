using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public sealed class DeviceFilter : PageRequest
{
    public Guid? TenantId { get; init; }
    public Guid? SiteId { get; init; }
    public Guid? DeviceTypeId { get; init; }
    public Domain.TransportType? Transport { get; init; }
    public Domain.ProtocolType? Protocol { get; init; }
    public Domain.DeviceStatus? Status { get; init; }
}

public interface IDeviceRepository
{
    Task<Device?> GetByMqttTopicAsync(string topic, CancellationToken ct);
    Task<IReadOnlyList<string>> ListMqttTopicsAsync(CancellationToken ct);
    Task<Device?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Device?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<Device?> GetByCodeAsync(Guid tenantId, string deviceCode, CancellationToken ct);

    /// <summary>
    /// Resolves a device from the human-readable codes carried in an MQTT
    /// topic or TCP frame header (tenant slug / site code / device code) —
    /// used by protocol handlers, which never see the internal Guid keys.
    /// </summary>
    Task<Device?> GetByCodesAsync(string tenantSlug, string siteCode, string deviceCode, CancellationToken ct);

    /// <summary>
    /// Resolves a device by its code alone, platform-wide — used by
    /// transports (raw TCP frames, BACnet device numbers) whose addressing
    /// carries only a device identifier, not tenant/site context. Assumes
    /// device codes are unique across the whole platform, not just within
    /// a tenant — a deliberate simplification, documented in
    /// docs/tcp/tcp-framing.md.
    /// </summary>
    Task<Device?> GetByDeviceCodeAsync(string deviceCode, CancellationToken ct);
    Task<PagedResult<Device>> ListAsync(DeviceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<Device>> ListForOfflineSweepAsync(CancellationToken ct);
    Task<Device> AddAsync(Device device, CancellationToken ct);
    Task UpdateAsync(Device device, CancellationToken ct);
    Task DeleteAsync(Guid tenantId, Guid id, CancellationToken ct);
}
