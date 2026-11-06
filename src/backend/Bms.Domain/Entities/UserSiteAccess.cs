namespace Bms.Domain.Entities;

/// <summary>Explicit, reviewed site access for a Site Admin. No assignments means no site data.</summary>
public class UserSiteAccess
{
    public Guid UserId { get; set; }
    public Guid SiteId { get; set; }
}
