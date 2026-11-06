using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;

namespace Bms.Infrastructure.Notifications;

internal static class AlertRecipients
{
    public static IQueryable<User> ForDevice(BmsDbContext db, Device device) => db.Users.Where(u => u.IsActive &&
        (u.Role!.Name == AccessRoles.SuperAdmin || (u.TenantId == device.TenantId &&
         (u.Role.Name == AccessRoles.CompanyAdmin || (u.Role.Name == AccessRoles.SiteAdmin &&
          db.UserSiteAccess.Any(a => a.UserId == u.Id && a.SiteId == device.SiteId))))));
}
