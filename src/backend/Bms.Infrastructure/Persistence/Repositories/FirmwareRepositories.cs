using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class FirmwareRepository(BmsDbContext db) : IFirmwareRepository
{
    public async Task<IReadOnlyList<Firmware>> ListAsync(CancellationToken ct) =>
        await db.Firmwares.OrderByDescending(f => f.ReleaseDate).ToListAsync(ct);

    public Task<Firmware?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Firmwares.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<Firmware> AddAsync(Firmware firmware, CancellationToken ct)
    {
        db.Firmwares.Add(firmware);
        await db.SaveChangesAsync(ct);
        return firmware;
    }

    public Task<bool> VersionExistsAsync(Guid deviceTypeId, string version, CancellationToken ct)
    {
        var lowered = version.ToLower();
        return db.Firmwares.AnyAsync(f => f.DeviceTypeId == deviceTypeId && f.Version.ToLower() == lowered, ct);
    }

    public async Task DeleteAsync(Firmware firmware, CancellationToken ct)
    {
        // Deployments reference the release with Restrict, so they go first.
        await db.FirmwareDeployments.Where(d => d.FirmwareId == firmware.Id).ExecuteDeleteAsync(ct);
        db.Firmwares.Remove(firmware);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class FirmwareDeploymentRepository(BmsDbContext db) : IFirmwareDeploymentRepository
{
    private static readonly FirmwareDeploymentStatus[] Finished =
        [FirmwareDeploymentStatus.Success, FirmwareDeploymentStatus.Failed, FirmwareDeploymentStatus.Timeout];

    public Task<FirmwareDeployment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.FirmwareDeployments.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<FirmwareDeployment>> ListForFirmwareAsync(Guid firmwareId, CancellationToken ct) =>
        await db.FirmwareDeployments.Where(d => d.FirmwareId == firmwareId).OrderByDescending(d => d.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<FirmwareDeployment>> ListTimedOutAsync(TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        return await db.FirmwareDeployments
            .Where(d => d.StartedAt != null && d.StartedAt < cutoff &&
                        d.Status != FirmwareDeploymentStatus.WaitingForDevice &&
                        !Finished.Contains(d.Status))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FirmwareDeployment>> ListReadyToResumeAsync(CancellationToken ct) =>
        await db.FirmwareDeployments
            .Include(d => d.Device)
            .Include(d => d.Firmware)
            .Where(d => d.Status == FirmwareDeploymentStatus.WaitingForDevice && d.Device!.Status == DeviceStatus.Online)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FirmwareDeployment>> ListWaitExpiredAsync(TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        return await db.FirmwareDeployments
            .Where(d => d.Status == FirmwareDeploymentStatus.WaitingForDevice && d.UpdatedAt < cutoff)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> DevicesWithActiveDeploymentAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct) =>
        (await db.FirmwareDeployments
            .Where(d => deviceIds.Contains(d.DeviceId) && !Finished.Contains(d.Status))
            .Select(d => d.DeviceId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

    public Task<bool> AnyActiveForFirmwareAsync(Guid firmwareId, CancellationToken ct) =>
        db.FirmwareDeployments.AnyAsync(d => d.FirmwareId == firmwareId && !Finished.Contains(d.Status), ct);

    public async Task<FirmwareDeployment> AddAsync(FirmwareDeployment deployment, CancellationToken ct)
    {
        db.FirmwareDeployments.Add(deployment);
        await db.SaveChangesAsync(ct);
        return deployment;
    }

    public async Task UpdateAsync(FirmwareDeployment deployment, CancellationToken ct)
    {
        deployment.UpdatedAt = DateTimeOffset.UtcNow;
        db.FirmwareDeployments.Update(deployment);
        await db.SaveChangesAsync(ct);
    }
}
