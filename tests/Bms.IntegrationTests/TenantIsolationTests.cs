using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Bms.IntegrationTests;

/// <summary>Tenant isolation is mandatory (core.txt section 40) — this proves it, not just documents it.</summary>
[Collection(IntegrationTestCollection.Name)]
public class TenantIsolationTests
{
    private readonly BmsApiFactory _factory;

    public TenantIsolationTests(BmsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string? TenantId)> AuthenticatedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tenantId = body.GetProperty("user").GetProperty("tenantId").GetString();
        return (client, tenantId);
    }

    [Fact]
    public async Task DevicesList_OnlyReturnsCallersOwnTenantDevices()
    {
        var (whirlpoolClient, whirlpoolTenantId) = await AuthenticatedClientAsync("admin@whirlpool.example", "Passw0rd!");

        var devices = await whirlpoolClient.GetFromJsonAsync<JsonElement>("/api/devices?pageSize=200");

        foreach (var device in devices.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(whirlpoolTenantId, device.GetProperty("tenantId").GetString());
        }
    }

    [Fact]
    public async Task RequestingAnotherTenantsSites_IsForbidden()
    {
        var (whirlpoolClient, _) = await AuthenticatedClientAsync("admin@whirlpool.example", "Passw0rd!");
        var (_, abcTenantId) = await AuthenticatedClientAsync("admin@abc-manufacturing.example", "Passw0rd!");

        var response = await whirlpoolClient.GetAsync($"/api/sites?tenantId={abcTenantId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanSeeAcrossAllTenants()
    {
        var (superAdminClient, _) = await AuthenticatedClientAsync("superadmin@bms.local", "Passw0rd!");

        var devices = await superAdminClient.GetFromJsonAsync<JsonElement>("/api/devices?pageSize=200");
        var distinctTenants = devices.GetProperty("items").EnumerateArray()
            .Select(d => d.GetProperty("tenantId").GetString())
            .Distinct()
            .Count();

        Assert.True(distinctTenants > 1, "SuperAdmin should see devices across more than one tenant");
    }
}
