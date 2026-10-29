using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=bms_platform_test;Username=bms_app;Password=devpassword",
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
