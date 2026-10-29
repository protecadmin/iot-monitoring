using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class AuditLog : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
