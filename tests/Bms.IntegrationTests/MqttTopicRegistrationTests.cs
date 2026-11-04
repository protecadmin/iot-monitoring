using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bms.Infrastructure.Persistence;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class MqttTopicRegistrationTests(BmsApiFactory factory)
{
    [Fact]
    public async Task Blank_custom_topic_uses_registered_gateway_measurement_topic()
    {
        var (client, reference) = await SetupAsync();
        var gatewayId = "gateway-test-" + Guid.NewGuid().ToString("N");
        var topic = $"protec/gateway/{gatewayId}/measurement";
        Guid? createdId = null;
        try
        {
            var response = await client.PostAsJsonAsync($"/api/devices?tenantId={reference.GetProperty("tenantId").GetGuid()}", NewDevice(reference, gatewayId, null));
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            createdId = created.GetProperty("id").GetGuid();
            Assert.Equal(JsonValueKind.Null, created.GetProperty("mqttTelemetryTopic").ValueKind);
            Assert.Equal(topic, created.GetProperty("effectiveMqttTelemetryTopic").GetString());
            using var scope = factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Bms.Application.Abstractions.Persistence.IDeviceRepository>();
            Assert.Equal(createdId, (await repository.GetByMqttTopicAsync(topic, default))?.Id);
            Assert.Contains(topic, await repository.ListMqttTopicsAsync(default));
            var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
            var grants = await db.Database.SqlQuery<string>($"""SELECT "Username" AS "Value" FROM "MqttAllowedTopics" WHERE "Topic" = {topic}""").ToListAsync();
            Assert.Contains("bms-ingestion", grants);
            Assert.Null(await repository.GetByMqttTopicAsync("protec/gateway/not-registered/measurement", default));
            var alias = await client.PostAsJsonAsync($"/api/devices?tenantId={reference.GetProperty("tenantId").GetGuid()}", NewDevice(reference, "alias-" + Guid.NewGuid().ToString("N"), topic));
            Assert.Equal(HttpStatusCode.Conflict, alias.StatusCode);
        }
        finally
        {
            if (createdId is not null) (await client.DeleteAsync($"/api/devices/{createdId}")).EnsureSuccessStatusCode();
        }
    }

    private async Task<(HttpClient Client, JsonElement Device)> SetupAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "superadmin@bms.local", password = "Passw0rd!" });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/devices?pageSize=200");
        return (client, list.GetProperty("items").EnumerateArray().First().Clone());
    }

    private static object NewDevice(JsonElement reference, string code, string? topic) => new
    {
        siteId = reference.GetProperty("siteId").GetGuid(), deviceTypeId = reference.GetProperty("deviceTypeId").GetGuid(),
        deviceCode = code, name = code, mqttTelemetryTopic = topic
    };

    [Fact]
    public async Task Any_topic_layout_round_trips_and_duplicate_topics_are_rejected()
    {
        var (client, reference) = await SetupAsync();
        var tenantId = reference.GetProperty("tenantId").GetGuid();
        var topic = $"arbitrary-{Guid.NewGuid():N}";
        Guid? createdId = null;
        Guid? temporaryCredentialId = null;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
                if (!await db.MqttCredentials.AnyAsync(c => c.TenantId == tenantId))
                {
                    var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
                    var credential = new MqttCredential { TenantId = tenantId, Username = tenant.Slug, PasswordHash = "disabled-test-credential", EncryptedPassword = "test-only" };
                    db.MqttCredentials.Add(credential);
                    await db.SaveChangesAsync();
                    temporaryCredentialId = credential.Id;
                }
            }
            var create = await client.PostAsJsonAsync($"/api/devices?tenantId={tenantId}", NewDevice(reference, $"mqtt-test-{Guid.NewGuid():N}", topic));
            create.EnsureSuccessStatusCode();
            var created = await create.Content.ReadFromJsonAsync<JsonElement>();
            createdId = created.GetProperty("id").GetGuid();
            Assert.Equal(topic, created.GetProperty("mqttTelemetryTopic").GetString());

            var duplicate = await client.PostAsJsonAsync($"/api/devices?tenantId={tenantId}", NewDevice(reference, $"mqtt-test-{Guid.NewGuid():N}", topic));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
                // Only this device's company and the trusted ingestion account receive the custom grant.
                var grants = await db.Database.SqlQuery<string>($"""SELECT "Username" AS "Value" FROM "MqttAllowedTopics" WHERE "Topic" = {topic}""").ToListAsync();
                Assert.Contains("bms-ingestion", grants);
                var credentialNames = await db.MqttCredentials.Where(c => c.TenantId == tenantId).Select(c => c.Username).ToListAsync();
                Assert.All(credentialNames, g => Assert.Contains(g, grants));
                Assert.All(grants.Where(g => g != "bms-ingestion"), g => Assert.Contains(g, credentialNames));
                // Exercise the exact shipped broker query with both parameters go-auth binds.
                var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../infrastructure/mosquitto/mosquitto.conf.example"));
                var aclQuery = File.ReadLines(configPath).Single(line => line.StartsWith("auth_opt_pg_aclquery ", StringComparison.Ordinal))["auth_opt_pg_aclquery ".Length..];
                var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
                await connection.OpenAsync();
                await using var command = new Npgsql.NpgsqlCommand(aclQuery, connection);
                command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = credentialNames.Single() });
                command.Parameters.Add(new Npgsql.NpgsqlParameter { Value = 2 });
                var allowedTopics = new List<string>();
                await using (var reader = await command.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) allowedTopics.Add(reader.GetString(0));
                Assert.Contains(topic, allowedTopics);
            }

            var change = await client.PutAsJsonAsync($"/api/devices/{createdId}", new { name = "Custom MQTT test", mqttTelemetryTopic = $"factory/{topic}/sensor/data" });
            change.EnsureSuccessStatusCode();
            var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/devices/{createdId}");
            Assert.Equal($"factory/{topic}/sensor/data", fetched.GetProperty("mqttTelemetryTopic").GetString());

            var clear = await client.PutAsJsonAsync($"/api/devices/{createdId}", new { name = "Custom MQTT test", mqttTelemetryTopic = (string?)null });
            clear.EnsureSuccessStatusCode();
            var cleared = await clear.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("mqttTelemetryTopic").ValueKind);
            Assert.Equal($"protec/gateway/{created.GetProperty("deviceCode").GetString()}/measurement", cleared.GetProperty("effectiveMqttTelemetryTopic").GetString());
        }
        finally
        {
            if (createdId is not null) (await client.DeleteAsync($"/api/devices/{createdId}")).EnsureSuccessStatusCode();
            if (temporaryCredentialId is not null)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
                await db.MqttCredentials.Where(c => c.Id == temporaryCredentialId).ExecuteDeleteAsync();
            }
        }
    }

    [Theory]
    [InlineData("factory/+")]
    [InlineData("factory/#")]
    [InlineData("bms/realtime/events")]
    [InlineData("iot/another-company/site/device/telemetry")]
    public async Task Invalid_or_conflicting_service_addresses_are_rejected(string topic)
    {
        var (client, reference) = await SetupAsync();
        var response = await client.PostAsJsonAsync($"/api/devices?tenantId={reference.GetProperty("tenantId").GetGuid()}",
            NewDevice(reference, $"mqtt-test-{Guid.NewGuid():N}", topic));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
