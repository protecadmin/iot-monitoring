using Bms.Application.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// Drives <see cref="TimeSeriesBackfillService"/>: every IntervalSeconds
/// while healthy, immediately again while a full batch was written (a
/// backlog is draining), and with exponential backoff up to
/// MaxBackoffSeconds while InfluxDB or PostgreSQL keeps failing.
/// </summary>
public sealed class TimeSeriesBackfillWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TimeSeriesBackfillOptions> options,
    ILogger<TimeSeriesBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogWarning("Time-series backfill is disabled: readings that miss InfluxDB will not be retried");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds));
        var maxBackoff = TimeSpan.FromSeconds(Math.Max(interval.TotalSeconds, settings.MaxBackoffSeconds));
        var wait = interval;

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
                var backfill = scope.ServiceProvider.GetRequiredService<TimeSeriesBackfillService>();
                var result = await backfill.RunPassAsync(stoppingToken);

                wait = result.StoreUnavailable
                    ? TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, maxBackoff.TotalSeconds))
                    : result.Written >= settings.BatchSize ? TimeSpan.FromMilliseconds(100) : interval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, maxBackoff.TotalSeconds));
                logger.LogError(ex, "Time-series backfill pass failed; next attempt in {Delay}s", wait.TotalSeconds);
            }
        }
    }
}
