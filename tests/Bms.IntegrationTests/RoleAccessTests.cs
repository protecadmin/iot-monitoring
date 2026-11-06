using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bms.Application.Abstractions.Security;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Bms.Api.Realtime;
using System.Reflection;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class RoleAccessTests(BmsApiFactory factory)
{
    private sealed record Scenario(Guid Tenant, Guid OtherTenant, Guid Site, Guid HiddenSite, Guid OtherSite, Guid Device, Guid HiddenDevice, Guid OtherDevice, Guid Alert, Guid HiddenAlert, Guid SiteUser, Guid CompanyUser, Dictionary<string, string> Emails);
    private const string Password = "Role-Access-Test-Password!";

    private async Task<Scenario> Seed()
    {
        // Start the real API and migrations before arranging isolated test records.
        using var started = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = new Tenant { Name = "Access A", Slug = "access-a-" + suffix };
        var other = new Tenant { Name = "Access B", Slug = "access-b-" + suffix };
        db.Tenants.AddRange(tenant, other);
        var site = new Site { TenantId = tenant.Id, Code = "a", Name = "Assigned" };
        var hidden = new Site { TenantId = tenant.Id, Code = "b", Name = "Unassigned" };
        var foreign = new Site { TenantId = other.Id, Code = "c", Name = "Other company" };
        db.Sites.AddRange(site, hidden, foreign);
        var type = await db.DeviceTypes.FirstAsync();
        Device Make(Site s) => new() { TenantId = s.TenantId, SiteId = s.Id, DeviceTypeId = type.Id, DeviceCode = Guid.NewGuid().ToString("N"), Name = s.Name };
        var device = Make(site); var hiddenDevice = Make(hidden); var foreignDevice = Make(foreign);
        db.Devices.AddRange(device, hiddenDevice, foreignDevice);
        var alert = new Alert { TenantId = tenant.Id, DeviceId = device.Id, Message = "allowed-" + suffix };
        var hiddenAlert = new Alert { TenantId = tenant.Id, DeviceId = hiddenDevice.Id, Message = "hidden-" + suffix };
        db.Alerts.AddRange(alert, hiddenAlert);
        var emails = new Dictionary<string, string>();
        var ids = new Dictionary<string, Guid>();
        foreach (var role in new[] { "SuperAdmin", "Executive", "CompanyAdmin", "SiteAdmin", "Customer" })
        {
            var entityRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == role);
            if (entityRole is null) { entityRole = new Role { Name = role }; db.Roles.Add(entityRole); }
            var email = role.ToLowerInvariant() + "-" + suffix + "@rbac.test";
            var user = new User { Email = email, DisplayName = role, RoleId = entityRole.Id, PasswordHash = hasher.Hash(Password), TenantId = AccessRoles.IsPlatform(role) ? null : tenant.Id };
            db.Users.Add(user); emails.Add(role, email); ids.Add(role, user.Id);
        }
        db.UserSiteAccess.Add(new UserSiteAccess { UserId = ids["SiteAdmin"], SiteId = site.Id });
        await db.SaveChangesAsync();
        return new(tenant.Id, other.Id, site.Id, hidden.Id, foreign.Id, device.Id, hiddenDevice.Id, foreignDevice.Id, alert.Id, hiddenAlert.Id, ids["SiteAdmin"], ids["CompanyAdmin"], emails);
    }

    private async Task<HttpClient> Login(Scenario s, string role)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = s.Emails[role], password = Password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task ListsAndExports_RespectTenantAndAssignedSites()
    {
        var s = await Seed(); using var site = await Login(s, "SiteAdmin"); using var company = await Login(s, "CompanyAdmin"); using var executive = await Login(s, "Executive");
        var sites = await site.GetFromJsonAsync<JsonElement>("/api/sites?pageSize=200");
        Assert.Equal([s.Site], sites.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());
        var devices = await site.GetFromJsonAsync<JsonElement>("/api/devices?pageSize=200");
        Assert.Equal([s.Device], devices.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());
        var companySites = await company.GetFromJsonAsync<JsonElement>("/api/sites?pageSize=200");
        Assert.Equal(2, companySites.GetProperty("totalCount").GetInt32());
        var executiveSites = await executive.GetFromJsonAsync<JsonElement>($"/api/sites?tenantId={s.OtherTenant}");
        Assert.Single(executiveSites.GetProperty("items").EnumerateArray());
        var export = await site.GetStringAsync("/api/reports/alerts");
        Assert.Contains(s.Device.ToString(), export); Assert.DoesNotContain(s.HiddenDevice.ToString(), export);
        var alerts = await site.GetFromJsonAsync<JsonElement>("/api/alerts");
        Assert.Single(alerts.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await executive.GetAsync("/api/tenants")).StatusCode);
    }

    [Theory]
    [InlineData("CompanyAdmin")]
    [InlineData("Executive")]
    [InlineData("SiteAdmin")]
    public async Task BusinessRoles_CannotAccessTechnicalEndpoints(string role)
    {
        var s = await Seed(); using var client = await Login(s, role);
        foreach (var path in new[] { "/api/gateways", "/api/hubs", "/api/connectors", "/api/mqtt-credentials", "/api/firmware", "/api/audit-log", "/api/settings/smtp" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/gateways", new { gatewayCode = "unauthorized" })).StatusCode);
        if (role != "CompanyAdmin") Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task TamperedIdsAndUnassignedResources_AreForbidden()
    {
        var s = await Seed(); using var site = await Login(s, "SiteAdmin"); using var company = await Login(s, "CompanyAdmin");
        foreach (var path in new[] { $"/api/sites?tenantId={s.OtherTenant}", $"/api/sites/{s.HiddenSite}?tenantId={s.Tenant}", $"/api/devices/{s.HiddenDevice}", $"/api/devices/{s.HiddenDevice}/telemetry/history", $"/api/reports/telemetry?deviceId={s.HiddenDevice}", $"/api/devices?siteId={s.HiddenSite}", $"/api/devices?tenantId={s.OtherTenant}", $"/api/telemetry/latest?siteId={s.HiddenSite}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await site.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.GetAsync($"/api/devices/{s.OtherDevice}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.PostAsJsonAsync($"/api/alerts/{s.HiddenAlert}/acknowledge", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await site.PostAsJsonAsync($"/api/alerts/{s.Alert}/acknowledge", new { })).StatusCode);
    }

    [Fact]
    public async Task CompanyUserManagement_PreventsEscalationAndCrossCompanyAssignment()
    {
        var s = await Seed(); using var client = await Login(s, "CompanyAdmin");
        foreach (var role in new[] { "SuperAdmin", "Executive", "CompanyAdmin", "Viewer", "Customer", "Engineer" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", new { email = Guid.NewGuid() + "@rbac.test", displayName = "Denied", password = Password, role, tenantId = s.Tenant })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", new { email = Guid.NewGuid() + "@rbac.test", displayName = "Denied", password = Password, role = "SiteAdmin", allowedSiteIds = new[] { s.OtherSite } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{s.CompanyUser}", new { displayName = "Denied", role = "SiteAdmin", allowedSiteIds = new[] { s.Site } })).StatusCode);
        var create = await client.PostAsJsonAsync("/api/users", new { email = Guid.NewGuid() + "@rbac.test", displayName = "Allowed", password = Password, role = "SiteAdmin", allowedSiteIds = new[] { s.Site, s.HiddenSite } });
        create.EnsureSuccessStatusCode();
        var json = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(s.Tenant, json.GetProperty("tenantId").GetGuid()); Assert.Equal(2, json.GetProperty("allowedSiteIds").GetArrayLength());
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.All(listed.EnumerateArray(), u => Assert.Equal(s.Tenant, u.GetProperty("tenantId").GetGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", new { email = Guid.NewGuid() + "@rbac.test", displayName = "Denied", password = Password, role = "SiteAdmin", tenantId = s.OtherTenant, allowedSiteIds = new[] { s.Site } })).StatusCode);
        using var super = await Login(s, "SuperAdmin");
        var foreignCreate = await super.PostAsJsonAsync("/api/users", new { email = Guid.NewGuid() + "@rbac.test", displayName = "Foreign", password = Password, role = "SiteAdmin", tenantId = s.OtherTenant, allowedSiteIds = new[] { s.OtherSite } });
        foreignCreate.EnsureSuccessStatusCode();
        var foreignId = (await foreignCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/users/{foreignId}/deactivate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{foreignId}", new { displayName = "Denied" })).StatusCode);
    }

    [Fact]
    public async Task StaleRoleClaims_DoNotOverrideCurrentDatabaseRole()
    {
        var s = await Seed(); using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == s.SiteUser);
        var stale = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().IssueTokens(user, "SuperAdmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stale.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/gateways")).StatusCode);
        var devices = await client.GetFromJsonAsync<JsonElement>("/api/devices");
        Assert.Single(devices.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task RevokingAssignmentsOrDeactivatingUser_TakesEffectForExistingToken()
    {
        var s = await Seed(); using var client = await Login(s, "SiteAdmin");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
            await db.UserSiteAccess.Where(a => a.UserId == s.SiteUser).ExecuteDeleteAsync();
        }
        var list = await client.GetFromJsonAsync<JsonElement>("/api/devices");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/devices/{s.Device}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
            await db.Users.Where(u => u.Id == s.SiteUser).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/devices")).StatusCode);
    }

    [Fact]
    public async Task Realtime_UsesCurrentAssignments_AndKeepsTechnicalEventsInternal()
    {
        var s = await Seed();
        var clients = DispatchProxy.Create<IHubClients, CaptureClients>();
        var captured = (CaptureClients)(object)clients;
        var delivery = new RealtimeDelivery(new CaptureHub(clients), factory.Services.GetRequiredService<IServiceScopeFactory>());
        await delivery.SendAsync(s.Tenant, "TelemetryUpdated", new { deviceId = s.Device, siteId = s.Site, fields = new { reading = 59 } }, default);
        Assert.Contains(s.SiteUser.ToString(), captured.Recipients);
        Assert.Contains(s.CompanyUser.ToString(), captured.Recipients);
        await delivery.SendAsync(s.Tenant, "TelemetryUpdated", new { deviceId = s.HiddenDevice, siteId = s.HiddenSite }, default);
        Assert.DoesNotContain(s.SiteUser.ToString(), captured.Recipients);
        await delivery.SendAsync(Guid.Empty, "GatewayMessageReceived", new { payload = "technical" }, default);
        Assert.DoesNotContain(s.SiteUser.ToString(), captured.Recipients);
        Assert.DoesNotContain(s.CompanyUser.ToString(), captured.Recipients);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BmsDbContext>().UserSiteAccess.Where(a => a.UserId == s.SiteUser).ExecuteDeleteAsync();
        await delivery.SendAsync(s.Tenant, "AlertCreated", new { deviceId = s.Device }, default);
        Assert.DoesNotContain(s.SiteUser.ToString(), captured.Recipients);
    }

    public class CaptureClients : DispatchProxy
    {
        public IReadOnlyList<string> Recipients { get; private set; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "Users") throw new NotSupportedException(targetMethod?.Name);
            Recipients = (IReadOnlyList<string>)args![0]!;
            return new Sink();
        }
    }
    private sealed class Sink : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class CaptureHub(IHubClients clients) : IHubContext<TelemetryHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    [Fact]
    public async Task LegacyRolesRequireReview_AndSuperAdminRetainsTechnicalAccess()
    {
        var s = await Seed(); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = s.Emails["Customer"], password = Password })).StatusCode);
        using var super = await Login(s, "SuperAdmin");
        foreach (var path in new[] { "/api/tenants", "/api/sites", "/api/gateways", "/api/hubs", "/api/connectors", "/api/device-types", "/api/users", "/api/alert-rules/global" })
            Assert.Equal(HttpStatusCode.OK, (await super.GetAsync(path)).StatusCode);
        using var executive = await Login(s, "Executive");
        Assert.Equal(HttpStatusCode.Forbidden, (await executive.PutAsJsonAsync($"/api/sites/{s.Site}?tenantId={s.Tenant}", new { name = "Denied" })).StatusCode);
    }
}
