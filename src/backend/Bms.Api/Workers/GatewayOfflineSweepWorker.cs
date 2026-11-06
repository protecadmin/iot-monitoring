using Bms.Application.Abstractions.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bms.Api.Workers;

public sealed class GatewayOfflineSweepWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<GatewayOfflineSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(10, configuration.GetValue("GatewayLifecycle:SweepIntervalSeconds", 30));
        var offlineAfter = Math.Max(30, configuration.GetValue("GatewayLifecycle:OfflineAfterSeconds", 300));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IGatewayRepository>();
                var count = await repo.MarkOfflineAsync(DateTimeOffset.UtcNow.AddSeconds(-offlineAfter), stoppingToken);
                if (count > 0) logger.LogInformation("Gateway offline sweep marked {Count} gateway(s) offline", count);
            }
            catch (Exception ex) { logger.LogError(ex, "Gateway offline sweep failed"); }
        }
    }
}
