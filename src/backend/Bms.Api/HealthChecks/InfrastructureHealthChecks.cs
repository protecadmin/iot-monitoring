using System.Net.Sockets;
using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Telemetry;
using Bms.Protocols.Configuration;
using InfluxDB.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bms.Api.HealthChecks;

public sealed class PostgresHealthCheck(BmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("PostgreSQL reachable")
                : HealthCheckResult.Unhealthy("PostgreSQL not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL check failed", ex);
        }
    }
}

public sealed class InfluxDbHealthCheck(IOptions<InfluxDbOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var client = new InfluxDBClient(options.Value.Url, options.Value.Token);
            var healthy = await client.PingAsync();
            return healthy
                ? HealthCheckResult.Healthy("InfluxDB reachable")
                : HealthCheckResult.Unhealthy("InfluxDB ping failed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("InfluxDB check failed", ex);
        }
    }
}

public sealed class MqttHealthCheck(IOptions<MqttOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(options.Value.Host, options.Value.Port, ct).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(3), ct));

            return completed == connectTask && tcpClient.Connected
                ? HealthCheckResult.Healthy("MQTT broker port reachable")
                : HealthCheckResult.Unhealthy("MQTT broker port not reachable within timeout");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MQTT broker check failed", ex);
        }
    }
}
