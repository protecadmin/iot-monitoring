using Bms.Application.Abstractions.DeviceManagement;
using Bms.DeviceManagement.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// "No heartbeat within threshold -> OFFLINE" can only be noticed by
/// checking the clock periodically — there is no inbound event for
/// "a message didn't arrive." See docs/database/schema.md#device-lifecycle.
/// </summary>
public sealed class OfflineSweepWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DeviceLifecycleOptions> options,
    IIngestionHealthMonitor ingestionHealth,
    TimeProvider clock,
    ILogger<OfflineSweepWorker> logger) : BackgroundService
{
    private string? _pausedReason;
    private DateTimeOffset? _untrustedSince;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.SweepIntervalSeconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // While ingestion is down/degraded/draining, every device looks
            // silent at once; that silence says nothing about the devices, so
            // it is not counted. The sweep still runs: devices that had already
            // been silent too long BEFORE the outage are genuinely gone, and an
            // outage that outlives the process must not leave them shown as
            // Online forever. SweepOfflineDevicesAsync only credits silence up
            // to the instant below.
            var now = clock.GetUtcNow();
            var untrusted = ingestionHealth.DeviceSilenceUntrustedReason(now);
            if (untrusted is not null)
            {
                if (_pausedReason is null)
                {
                    _untrustedSince = now;
                    logger.LogWarning("Device silence no longer counted (frozen at {Since}): {Reason}", now, untrusted);
                }
                else
                {
                    logger.LogDebug("Device silence still not counted (frozen at {Since}): {Reason}", _untrustedSince, untrusted);
                }
                _pausedReason = untrusted;
            }
            else if (_pausedReason is not null)
            {
                logger.LogInformation("Device silence counted again (was frozen: {Reason})", _pausedReason);
                _pausedReason = null;
                _untrustedSince = null;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<IDeviceLifecycleService>();
                var flipped = await lifecycle.SweepOfflineDevicesAsync(_untrustedSince, stoppingToken);
                if (flipped > 0)
                    logger.LogInformation("Offline sweep flipped {Count} device(s) to Offline", flipped);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Offline sweep failed");
            }
        }
    }
}
