using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Site : TenantScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
