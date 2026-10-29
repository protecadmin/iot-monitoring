using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IDeviceCommandRepository
{
    Task<DeviceCommand?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DeviceCommand>> ListForDeviceAsync(Guid deviceId, CancellationToken ct);
    Task<IReadOnlyList<DeviceCommand>> ListTimedOutAsync(TimeSpan olderThan, CancellationToken ct);
    Task<DeviceCommand> AddAsync(DeviceCommand command, CancellationToken ct);
    Task UpdateAsync(DeviceCommand command, CancellationToken ct);
}

public interface IDeviceConfigurationRepository
{
    Task<DeviceConfiguration?> GetForDeviceAsync(Guid deviceId, CancellationToken ct);
    Task<DeviceConfiguration> UpsertAsync(DeviceConfiguration configuration, CancellationToken ct);
}
