using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IFirmwareRepository
{
    Task<IReadOnlyList<Firmware>> ListAsync(CancellationToken ct);
    Task<Firmware?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Firmware> AddAsync(Firmware firmware, CancellationToken ct);

    /// <summary>Case-insensitive, per device type — "1.1.0" can exist once for Vibration Sensors and once for Energy Meters.</summary>
    Task<bool> VersionExistsAsync(Guid deviceTypeId, string version, CancellationToken ct);

    /// <summary>Removes the release together with its deployment history.</summary>
    Task DeleteAsync(Firmware firmware, CancellationToken ct);
}

public interface IFirmwareDeploymentRepository
{
    Task<FirmwareDeployment?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<FirmwareDeployment>> ListForFirmwareAsync(Guid firmwareId, CancellationToken ct);

    /// <summary>Sent (StartedAt set) but not finished within the window. Deployments still waiting for their device are excluded.</summary>
    Task<IReadOnlyList<FirmwareDeployment>> ListTimedOutAsync(TimeSpan olderThan, CancellationToken ct);

    /// <summary>WaitingForDevice deployments whose device is Online again — ready to send. Device and Firmware loaded.</summary>
    Task<IReadOnlyList<FirmwareDeployment>> ListReadyToResumeAsync(CancellationToken ct);

    /// <summary>WaitingForDevice deployments that have waited (since queued or last retried) longer than the window.</summary>
    Task<IReadOnlyList<FirmwareDeployment>> ListWaitExpiredAsync(TimeSpan olderThan, CancellationToken ct);

    /// <summary>Devices among these that already have an update in progress (any release).</summary>
    Task<IReadOnlySet<Guid>> DevicesWithActiveDeploymentAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    Task<bool> AnyActiveForFirmwareAsync(Guid firmwareId, CancellationToken ct);

    Task<FirmwareDeployment> AddAsync(FirmwareDeployment deployment, CancellationToken ct);
    Task UpdateAsync(FirmwareDeployment deployment, CancellationToken ct);
}
