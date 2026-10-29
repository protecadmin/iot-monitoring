using Bms.Application.Common;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Persistence;

public interface IRawMessageRepository
{
    Task<RawDeviceMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<RawDeviceMessage>> ListForDeviceAsync(Guid deviceId, PageRequest request, CancellationToken ct);
    Task<RawDeviceMessage> AddAsync(RawDeviceMessage message, CancellationToken ct);
    Task UpdateAsync(RawDeviceMessage message, CancellationToken ct);

    /// <summary>
    /// Inserts a message that passed validation and deduplication. Returns
    /// false — without throwing — when the database's uniqueness constraint
    /// on (device, DedupKey) says the same reading is already Persisted,
    /// i.e. a concurrent or redelivered copy won the race.
    /// </summary>
    Task<bool> TryAddPersistedAsync(RawDeviceMessage message, CancellationToken ct);

    /// <summary>True if a Persisted message for this device already carries this DedupKey.</summary>
    Task<bool> ExistsByDedupKeyAsync(Guid deviceId, string dedupKey, CancellationToken ct);

    /// <summary>
    /// True if a Persisted message for this device with this sequence number
    /// and byte-identical payload was received since <paramref name="since"/>
    /// — the duplicate check for devices that send a sequence number but no
    /// timestamp of their own. See docs/architecture/data-flow.md.
    /// </summary>
    Task<bool> ExistsRecentAsync(Guid deviceId, long sequenceNumber, byte[] rawPayload, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Persisted messages whose reading has not reached the time-series store,
    /// received before <paramref name="receivedBefore"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<RawDeviceMessage>> ListPendingTimeSeriesAsync(
        DateTimeOffset receivedBefore, int maxAttempts, int batchSize, CancellationToken ct);

    Task MarkTimeSeriesWrittenAsync(Guid id, DateTimeOffset writtenAt, CancellationToken ct);
    Task RecordTimeSeriesAttemptFailedAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> messages received before
    /// <paramref name="cutoff"/>, oldest first, and returns how many went.
    /// A Persisted row whose reading has not reached the time-series store
    /// is never deleted however old it is: the backfill still needs it, and
    /// dropping it would lose the reading silently. Those rows are reported
    /// by <see cref="CountExpiredAwaitingTimeSeriesAsync"/> instead.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct);

    /// <summary>
    /// Persisted rows older than <paramref name="cutoff"/> that retention
    /// must leave behind because their reading never reached the time-series
    /// store. A number that keeps climbing means the backfill is stuck, and
    /// that the table has no effective ceiling until it is fixed.
    /// </summary>
    Task<int> CountExpiredAwaitingTimeSeriesAsync(DateTimeOffset cutoff, CancellationToken ct);
}
