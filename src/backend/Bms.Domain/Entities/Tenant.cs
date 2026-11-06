using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool DigitalTwinEnabled { get; set; }

    public ICollection<Site> Sites { get; set; } = new List<Site>();
}
