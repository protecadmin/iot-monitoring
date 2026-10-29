using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Firmware : Entity
{
    public Guid DeviceTypeId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTimeOffset ReleaseDate { get; set; } = DateTimeOffset.UtcNow;

    public DeviceType? DeviceType { get; set; }
    public ICollection<FirmwareDeployment> Deployments { get; set; } = new List<FirmwareDeployment>();
}

public class FirmwareDeployment : TenantScopedEntity
{
    public Guid FirmwareId { get; set; }
    public Guid DeviceId { get; set; }
    public FirmwareDeploymentStatus Status { get; set; } = FirmwareDeploymentStatus.Pending;
    public int AttemptCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Firmware? Firmware { get; set; }
    public Device? Device { get; set; }
}
