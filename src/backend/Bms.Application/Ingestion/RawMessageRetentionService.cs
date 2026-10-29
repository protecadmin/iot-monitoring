using Bms.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Application.Ingestion;

public sealed record RetentionPassResult(int Deleted, bool MoreToDelete, int AwaitingTimeSeries);

/// <summary>
/// Enforces the ceiling on RawDeviceMessages. Every message ever received is
/// stored there in full, and the acknowledgement path writes to that table
/// before anything else happens — so unbounded growth does not just cost
/// disk, it eventually stops the one write that must never fail.
///
/// Deletes run oldest-first in bounded batches rather than as one statement,
/// so the live insert path is never queued behind a long-running delete.
/// </summary>
public sealed class RawMessageRetentionService(
    IRawMessageRepository rawMessages,
    IOptions<RawMessageRetentionOptions> options,
    TimeProvider clock,
    ILogger<RawMessageRetentionService> logger)
{
    public async Task<RetentionPassResult> RunPassAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow().AddDays(-Math.Max(1, settings.RetentionDays));
        var batchSize = Math.Max(1, settings.BatchSize);
        var maxBatches = Math.Max(1, settings.MaxBatchesPerPass);

        var deleted = 0;
        var moreToDelete = false;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            ct.ThrowIfCancellationRequested();

            var removed = await rawMessages.DeleteExpiredAsync(cutoff, batchSize, ct);
            deleted += removed;
            IngestionMetrics.RawMessagesDeleted.Add(removed);

            if (removed < batchSize)
                break;

            // A full batch means there is probably more; the pass stops at
            // MaxBatchesPerPass either way and the worker comes straight back,
            // so a large backlog drains over several passes instead of one
            // unbounded run.
            moreToDelete = batch == maxBatches - 1;
        }

        // Rows the cutoff covers but retention must not touch, because their
        // reading never reached the time-series store. Deleting them would
        // discard the reading for good; leaving them means the table has no
        // real ceiling until the backfill is unstuck. Either way an operator
        // needs to know.
        var awaiting = await rawMessages.CountExpiredAwaitingTimeSeriesAsync(cutoff, ct);
        if (awaiting > 0)
        {
            logger.LogWarning(
                "Retention left {Count} message(s) older than {RetentionDays}d in place: their reading never reached the time-series store. " +
                "The backfill is behind or stuck — until it catches up these rows accumulate and the retention ceiling does not hold.",
                awaiting, settings.RetentionDays);
        }

        if (deleted > 0)
            logger.LogInformation("Retention deleted {Deleted} raw message(s) received before {Cutoff:u}", deleted, cutoff);

        return new RetentionPassResult(deleted, moreToDelete, awaiting);
    }
}
