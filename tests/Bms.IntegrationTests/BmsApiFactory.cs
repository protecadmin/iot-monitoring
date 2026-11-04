using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.IntegrationTests;

/// <summary>
/// Boots the real Bms.Api host (real DI graph, real EF Core migrations,
/// real seed data) against a dedicated local Postgres database — no
/// mocking of the application itself, only the external environment
/// (InfluxDB/MQTT aren't required to be running; the pipeline already
/// degrades gracefully without them, verified by IngestionFlowTests).
/// Requires a local PostgreSQL reachable at localhost:5432 with a
/// bms_platform_test database owned by bms_app/devpassword — see
/// docs/deployment/docker.md for the equivalent docker-compose path.
/// </summary>
public sealed class BmsApiFactory : WebApplicationFactory<Bms.Api.ApiMarker>
{
    // BMS_TEST_POSTGRES can select an isolated test container; production database names are refused.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // API tests must not take over the running application's MQTT client.
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType == typeof(Bms.Api.Realtime.RealtimeBridgeService)).ToList())
                services.Remove(descriptor);
        });
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var testPostgres = Environment.GetEnvironmentVariable("BMS_TEST_POSTGRES")
                ?? "Host=localhost;Port=5432;Database=bms_platform_test;Username=bms_app;Password=devpassword";
            var target = new Npgsql.NpgsqlConnectionStringBuilder(testPostgres);
            if (target.Database is null || !target.Database.EndsWith("_test", StringComparison.Ordinal))
                throw new InvalidOperationException("Integration tests require a database name ending in _test.");
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = testPostgres,
                ["InfluxDb:Url"] = "http://localhost:8086",
                ["InfluxDb:Org"] = "bms-platform",
                ["InfluxDb:Bucket"] = "telemetry",
                ["InfluxDb:Token"] = "dev-influx-admin-token-change-me",
                ["Mqtt:Host"] = "localhost",
                ["Mqtt:Port"] = "1883",
                ["Jwt:SecretKey"] = "integration-test-signing-key-not-used-in-production-0123456789"
            });
        });
    }
}
