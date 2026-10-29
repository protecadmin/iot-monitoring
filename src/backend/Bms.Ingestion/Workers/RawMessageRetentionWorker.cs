using Bms.Application.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// Drives <see cref="RawMessageRetentionService"/>: every IntervalSeconds
/// while the table is inside its retention window, immediately again while a
/// pass hit its batch ceiling (a backlog is draining), and with exponential
/// backoff up to one interval while PostgreSQL keeps failing.
/// </summary>
public sealed class RawMessageRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RawMessageRetentionOptions> options,
    ILogger<RawMessageRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogWarning(
                "Raw message retention is disabled: RawDeviceMessages will grow without bound, and the acknowledgement path " +
                "writes to that table before anything else. Only run this way with an external retention policy in place.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, settings.IntervalSeconds));
        var wait = interval;

        logger.LogInformation("Raw message retention started: keeping {Days} day(s), checking every {Interval}",
            settings.RetentionDays, interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<RawMessageRetentionService>();
                var result = await retention.RunPassAsync(stoppingToken);

                // Straight back round while a backlog is draining; otherwise
                // settle to the normal interval.
                wait = result.MoreToDelete ? TimeSpan.FromSeconds(1) : interval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Retention falling behind is not urgent on any single pass —
                // back off rather than hammer a database that is already
                // struggling, since the ingestion path needs it more.
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, interval.TotalSeconds));
                logger.LogError(ex, "Raw message retention pass failed; retrying in {Delay}", wait);
            }
        }
    }
}
