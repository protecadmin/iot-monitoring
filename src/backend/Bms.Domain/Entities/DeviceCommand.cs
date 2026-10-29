using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class DeviceCommand : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public CommandType Type { get; set; }
    public string? PayloadJson { get; set; }
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public Guid? IssuedByUserId { get; set; }

    public Device? Device { get; set; }
}
