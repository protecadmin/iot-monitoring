using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IDeviceBaselineRepository
{
    Task<IReadOnlyList<DeviceBaseline>> ListAsync(Guid deviceId, CancellationToken ct);
    Task<DeviceBaseline?> GetAsync(Guid deviceId, string parameterKey, CancellationToken ct);

    /// <summary>Replaces the device's baselines for these parameters; others are kept.</summary>
    Task SaveAsync(Guid deviceId, IReadOnlyList<DeviceBaseline> baselines, CancellationToken ct);

    /// <summary>Removes one parameter's baseline, or all of the device's when <paramref name="parameterKey"/> is null.</summary>
    Task<int> DeleteAsync(Guid deviceId, string? parameterKey, CancellationToken ct);
}
