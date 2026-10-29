using Bms.Domain.Connectors;

namespace Bms.Application.Abstractions.Persistence;

/// <summary>A device a connector can deliver for: who it belongs to, so its readings reach the right MQTT topic.</summary>
public sealed record ConnectorDeviceEntry(Guid ConnectorId, string SensorId, string DeviceCode, string TenantSlug, string SiteCode);

public sealed record ConnectorSighting(Guid ConnectorId, string Key, long Count, DateTimeOffset LastSeenAt, string? Sample);

public interface IConnectorRepository
{
    Task<IReadOnlyList<Connector>> ListAsync(CancellationToken ct);
    Task<Connector?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(Connector connector, CancellationToken ct);
    Task UpdateAsync(Connector connector, CancellationToken ct);
    Task DeleteAsync(Connector connector, CancellationToken ct);
    Task<bool> PortTakenAsync(ConnectorProtocol protocol, int port, Guid? exceptId, CancellationToken ct);
    Task<bool> PrefixTakenAsync(string prefix, Guid? exceptId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, int>> CountDevicesAsync(CancellationToken ct);

    /// <summary>Every device that receives its data through a connector.</summary>
    Task<IReadOnlyList<ConnectorDeviceEntry>> ListDeviceEntriesAsync(CancellationToken ct);

    Task RecordUnknownDevicesAsync(IReadOnlyList<ConnectorSighting> sightings, CancellationToken ct);
    Task<IReadOnlyList<ConnectorUnknownDevice>> ListUnknownDevicesAsync(CancellationToken ct);
    Task DeleteUnknownDeviceAsync(Guid connectorId, string sensorId, CancellationToken ct);

    Task RecordUnmappedKeysAsync(IReadOnlyList<ConnectorSighting> sightings, CancellationToken ct);
    Task<IReadOnlyList<ConnectorUnmappedKey>> ListUnmappedKeysAsync(Guid? connectorId, CancellationToken ct);
    Task DeleteUnmappedKeysAsync(Guid connectorId, IReadOnlyCollection<string> sources, CancellationToken ct);

    Task<ConnectorServiceStatus?> GetStatusAsync(CancellationToken ct);
    Task SaveStatusAsync(ConnectorServiceStatus status, CancellationToken ct);

    Task AddCaptureAsync(ConnectorCapture capture, CancellationToken ct);
    Task<ConnectorCapture?> GetCaptureAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ConnectorCapture>> ListPendingCapturesAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> CompleteCaptureAsync(Guid id, string sample, bool isHex, DateTimeOffset at, CancellationToken ct);
}
