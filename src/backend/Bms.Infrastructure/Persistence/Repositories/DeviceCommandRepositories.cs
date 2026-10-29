using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class DeviceCommandRepository(BmsDbContext db) : IDeviceCommandRepository
{
    public Task<DeviceCommand?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.DeviceCommands.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<DeviceCommand>> ListForDeviceAsync(Guid deviceId, CancellationToken ct) =>
        await db.DeviceCommands.Where(c => c.DeviceId == deviceId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceCommand>> ListTimedOutAsync(TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        return await db.DeviceCommands
            .Where(c => c.Status == CommandStatus.Sent && c.SentAt != null && c.SentAt < cutoff)
            .ToListAsync(ct);
    }

    public async Task<DeviceCommand> AddAsync(DeviceCommand command, CancellationToken ct)
    {
        db.DeviceCommands.Add(command);
        await db.SaveChangesAsync(ct);
        return command;
    }

    public async Task UpdateAsync(DeviceCommand command, CancellationToken ct)
    {
        command.UpdatedAt = DateTimeOffset.UtcNow;
        db.DeviceCommands.Update(command);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DeviceConfigurationRepository(BmsDbContext db) : IDeviceConfigurationRepository
{
    public Task<DeviceConfiguration?> GetForDeviceAsync(Guid deviceId, CancellationToken ct) =>
        db.DeviceConfigurations.FirstOrDefaultAsync(c => c.DeviceId == deviceId, ct);

    public async Task<DeviceConfiguration> UpsertAsync(DeviceConfiguration configuration, CancellationToken ct)
    {
        var existing = await GetForDeviceAsync(configuration.DeviceId, ct);
        if (existing is null)
        {
            db.DeviceConfigurations.Add(configuration);
        }
        else
        {
            existing.ReportingIntervalSeconds = configuration.ReportingIntervalSeconds;
            existing.HeartbeatIntervalSeconds = configuration.HeartbeatIntervalSeconds;
            existing.TemperatureThreshold = configuration.TemperatureThreshold;
            existing.Version += 1;
            existing.AcknowledgedAt = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return existing ?? configuration;
    }
}
