using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Bms.Api.Realtime;

/// <summary>
/// Clients join their own tenant's group right after connecting — never a
/// group they don't belong to, since the tenant id is read from the
/// caller's own JWT, not a client-supplied value. This is what keeps
/// real-time delivery tenant-isolated the same way the REST API is. See
/// docs/api/api-design.md.
/// </summary>
[Authorize]
public sealed class TelemetryHub : Hub
{
    public async Task JoinTenant()
    {
        var isSuperAdmin = Context.User?.IsInRole("SuperAdmin") ?? false;
        var tenantId = Context.User?.FindFirst("tenantId")?.Value;

        if (isSuperAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.AllTenants);
        }
        else if (tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Tenant(tenantId));
        }
    }
}

public static class RealtimeGroups
{
    public const string AllTenants = "all-tenants";
    public static string Tenant(string tenantId) => $"tenant-{tenantId}";
    public static string Tenant(Guid tenantId) => Tenant(tenantId.ToString());
}
