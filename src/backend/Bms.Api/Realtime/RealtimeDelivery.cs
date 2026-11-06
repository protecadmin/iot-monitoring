using System.Text.Json;
using Bms.Domain;
using Bms.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Bms.Api.Realtime;

public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}

/// <summary>Resolve recipients against current DB scope for every event, including open connections after access revocation.</summary>
public sealed class RealtimeDelivery(IHubContext<TelemetryHub> hub, IServiceScopeFactory scopes)
{
    public async Task SendAsync(Guid tenantId, string eventName, object payload, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        var data = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var business = eventName is "TelemetryUpdated" or "DeviceStatusChanged" or "AlertCreated" or "AlertResolved" or "CalculationUpdated";
        Guid? siteId = data.TryGetProperty("siteId", out var site) && site.TryGetGuid(out var sid) ? sid : null;
        if (siteId is null && data.TryGetProperty("deviceId", out var device) && device.TryGetGuid(out var did))
            siteId = await db.Devices.Where(d => d.Id == did && d.TenantId == tenantId).Select(d => (Guid?)d.SiteId).FirstOrDefaultAsync(ct);
        var recipients = await db.Users.Where(u => u.IsActive &&
            (u.Role!.Name == AccessRoles.SuperAdmin || (business &&
             (u.Role.Name == AccessRoles.Executive || (u.TenantId == tenantId &&
              (u.Role.Name == AccessRoles.CompanyAdmin || (u.Role.Name == AccessRoles.SiteAdmin && siteId != null &&
                db.UserSiteAccess.Any(a => a.UserId == u.Id && a.SiteId == siteId && db.Sites.Any(s => s.Id == a.SiteId && s.TenantId == u.TenantId)))))))))
            .Select(u => u.Id.ToString()).ToListAsync(ct);
        await hub.Clients.Users(recipients).SendAsync(eventName, payload, ct);
    }
}
