using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Application.Ingestion;

public sealed record BackfillPassResult(int Written, int Failed, bool StoreUnavailable);

/// <summary>
/// Writes Persisted readings that never reached the time-series store
/// (InfluxDB down, or the process died between the PostgreSQL insert and the
/// InfluxDB write) by re-normalizing the stored raw payload. PostgreSQL is
/// never written to except to mark progress, so no raw message is duplicated,
/// and an InfluxDB point is keyed by its tags and timestamp, so writing one
/// that did in fact land earlier only overwrites it with the same values.
/// </summary>
public sealed class TimeSeriesBackfillService(
    IRawMessageRepository rawMessages,
    TelemetryNormalizer normalizer,
    ITelemetryStore telemetryStore,
    ITransientFailureClassifier transientFailures,
    IOptions<TimeSeriesBackfillOptions> options,
    TimeProvider clock,
    ILogger<TimeSeriesBackfillService> logger)
{
    public async Task<BackfillPassResult> RunPassAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var pending = await rawMessages.ListPendingTimeSeriesAsync(
            clock.GetUtcNow().AddSeconds(-settings.MinAgeSeconds), settings.MaxAttempts, settings.BatchSize, ct);

        var written = 0;
        var failed = 0;

        foreach (var message in pending)
        {
            ct.ThrowIfCancellationRequested();

            var normalized = await normalizer.NormalizeAsync(message, ct);
            if (normalized.Error is not null || normalized.Telemetry is null)
            {
                // The payload or its mapping no longer normalizes (e.g. the
                // device or mapping was deleted since). Count the attempt so
                // the row eventually stops being retried; it stays in
                // PostgreSQL either way.
                logger.LogWarning("Time-series backfill cannot rebuild message {MessageId} for device {DeviceId}: {Reason}",
                    message.Id, message.DeviceId, normalized.Error);
                await rawMessages.RecordTimeSeriesAttemptFailedAsync(message.Id, ct);
                failed++;
                continue;
            }

            try
            {
                await telemetryStore.WriteAsync(normalized.Telemetry, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                IngestionMetrics.TimeSeriesWriteFailures.Add(1);
                await rawMessages.RecordTimeSeriesAttemptFailedAsync(message.Id, ct);
                failed++;

                // The store itself is down: stop this pass rather than
                // burning one attempt per row on the same outage.
                if (transientFailures.IsTransient(ex))
                {
                    logger.LogWarning(ex, "Time-series store unavailable; backfill paused with {Written} written this pass", written);
                    return new BackfillPassResult(written, failed, StoreUnavailable: true);
                }

                logger.LogError(ex, "Time-series backfill write failed for message {MessageId}", message.Id);
                continue;
            }

            await rawMessages.MarkTimeSeriesWrittenAsync(message.Id, clock.GetUtcNow(), ct);
            IngestionMetrics.TimeSeriesBackfilled.Add(1);
            written++;
        }

        if (written > 0 || failed > 0)
            logger.LogInformation("Time-series backfill pass: {Written} written, {Failed} failed, {Pending} examined",
                written, failed, pending.Count);

        return new BackfillPassResult(written, failed, StoreUnavailable: false);
    }
}
