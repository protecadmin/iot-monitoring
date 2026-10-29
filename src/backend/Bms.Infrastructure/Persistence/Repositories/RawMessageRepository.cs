using Bms.Application.Abstractions.Persistence;
using Bms.Application.Common;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bms.Infrastructure.Persistence.Repositories;

public sealed class RawMessageRepository(BmsDbContext db) : IRawMessageRepository
{
    public Task<RawDeviceMessage?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.RawDeviceMessages.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<PagedResult<RawDeviceMessage>> ListForDeviceAsync(Guid deviceId, PageRequest request, CancellationToken ct) =>
        db.RawDeviceMessages
            .AsNoTracking()
            .Where(m => m.DeviceId == deviceId)
            .OrderByDescending(m => m.ReceivedAt)
            .ToPagedResultAsync(request, ct);

    public async Task<RawDeviceMessage> AddAsync(RawDeviceMessage message, CancellationToken ct)
    {
        db.RawDeviceMessages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Leave the context clean so the caller can retry or record the
            // same message differently (e.g. as Rejected) without EF trying
            // to insert this failed row a second time.
            db.Entry(message).State = EntityState.Detached;
            throw;
        }
        return message;
    }

    public async Task<bool> TryAddPersistedAsync(RawDeviceMessage message, CancellationToken ct)
    {
        try
        {
            await AddAsync(message, ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public async Task UpdateAsync(RawDeviceMessage message, CancellationToken ct)
    {
        db.RawDeviceMessages.Update(message);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsByDedupKeyAsync(Guid deviceId, string dedupKey, CancellationToken ct) =>
        db.RawDeviceMessages.AnyAsync(m =>
            m.DeviceId == deviceId && m.DedupKey == dedupKey && m.ProcessingStatus == MessageProcessingStatus.Persisted, ct);

    public Task<bool> ExistsRecentAsync(Guid deviceId, long sequenceNumber, byte[] rawPayload, DateTimeOffset since, CancellationToken ct) =>
        db.RawDeviceMessages.AnyAsync(m =>
            m.DeviceId == deviceId
            && m.SequenceNumber == sequenceNumber
            && m.ProcessingStatus == MessageProcessingStatus.Persisted
            && m.ReceivedAt >= since
            && m.RawPayload == rawPayload, ct);

    public async Task<IReadOnlyList<RawDeviceMessage>> ListPendingTimeSeriesAsync(
        DateTimeOffset receivedBefore, int maxAttempts, int batchSize, CancellationToken ct) =>
        await db.RawDeviceMessages
            .AsNoTracking()
            .Where(m => m.ProcessingStatus == MessageProcessingStatus.Persisted
                        && m.TimeSeriesWrittenAt == null
                        && m.TimeSeriesWriteAttempts < maxAttempts
                        && m.ReceivedAt < receivedBefore)
            .OrderBy(m => m.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task MarkTimeSeriesWrittenAsync(Guid id, DateTimeOffset writtenAt, CancellationToken ct) =>
        db.RawDeviceMessages
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TimeSeriesWrittenAt, writtenAt.ToUniversalTime()), ct);

    public Task RecordTimeSeriesAttemptFailedAsync(Guid id, CancellationToken ct) =>
        db.RawDeviceMessages
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TimeSeriesWriteAttempts, m => m.TimeSeriesWriteAttempts + 1), ct);

    public Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct) =>
        db.RawDeviceMessages
            .Where(m => m.ReceivedAt < cutoff
                        // Rejected and duplicate rows never reach InfluxDB, so age
                        // alone decides them. A Persisted row is only expendable
                        // once its reading is safely in the time-series store.
                        && (m.ProcessingStatus != MessageProcessingStatus.Persisted || m.TimeSeriesWrittenAt != null))
            .OrderBy(m => m.ReceivedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);

    public Task<int> CountExpiredAwaitingTimeSeriesAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        db.RawDeviceMessages
            .CountAsync(m => m.ReceivedAt < cutoff
                             && m.ProcessingStatus == MessageProcessingStatus.Persisted
                             && m.TimeSeriesWrittenAt == null, ct);
}
