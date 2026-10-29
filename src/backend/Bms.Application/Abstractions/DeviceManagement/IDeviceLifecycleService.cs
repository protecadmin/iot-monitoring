using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.DeviceManagement;

/// <summary>Implemented by Bms.DeviceManagement; owns ONLINE/OFFLINE transitions and Last*At tracking.</summary>
public interface IDeviceLifecycleService
{
    Task RecordTelemetryAsync(Device device, DateTimeOffset at, CancellationToken ct);
    Task RecordHeartbeatAsync(Device device, DateTimeOffset at, CancellationToken ct);

    /// <summary>
    /// Runs periodically to flip devices whose heartbeat has gone stale to OFFLINE.
    /// <paramref name="silenceTrustedUntil"/> is the instant ingestion stopped being
    /// trustworthy (see <see cref="IIngestionHealthMonitor"/>); silence is only counted
    /// up to it, so an outage cannot age devices into Offline. Null trusts silence up to now.
    /// </summary>
    Task<int> SweepOfflineDevicesAsync(DateTimeOffset? silenceTrustedUntil, CancellationToken ct);
}
