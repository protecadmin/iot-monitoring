using Bms.Domain.Entities;

namespace Bms.Application.Ingestion;

/// <summary>
/// Runs a raw device message through decode -> map -> normalize -> validate
/// -> deduplicate -> persist -> alert -> real-time notify. Problems with the
/// message itself (bad payload, failed validation, duplicate) are caught,
/// logged, and recorded on the RawDeviceMessage, so one bad message never
/// stops the host from processing the next one. A transient infrastructure
/// failure (see ITransientFailureClassifier) before the message is durably
/// recorded is NOT swallowed: it propagates, so the caller can retry or
/// leave the message unacknowledged for redelivery. See
/// docs/architecture/data-flow.md.
/// </summary>
public interface IIngestionPipeline
{
    Task ProcessAsync(RawDeviceMessage rawMessage, CancellationToken ct);

    /// <param name="onDurablyRecorded">
    /// Invoked once the message is durably recorded in PostgreSQL (as
    /// Persisted, Rejected, or Duplicate) and before any downstream work —
    /// the point at which the sender may forget it.
    /// </param>
    Task ProcessAsync(RawDeviceMessage rawMessage, Func<CancellationToken, Task>? onDurablyRecorded, CancellationToken ct);
}
