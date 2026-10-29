using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Gateway : TenantScopedEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    public Site? Site { get; set; }
}
