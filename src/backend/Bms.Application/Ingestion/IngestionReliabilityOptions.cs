namespace Bms.Application.Ingestion;

public sealed class IngestionReliabilityOptions
{
    public const string SectionName = "IngestionReliability";

    /// <summary>
    /// For devices that send a sequence number but no timestamp: how far back
    /// a byte-identical message with the same sequence number counts as a
    /// duplicate. Must cover the longest broker-queued outage (the ingestion
    /// MQTT session expiry), but stay short enough that a wrapped or reset
    /// sequence counter isn't matched against unrelated old readings.
    /// </summary>
    public int DuplicateWindowHours { get; set; } = 24;

    /// <summary>First delay before retrying a message that failed for a transient (infrastructure) reason.</summary>
    public int TransientRetryInitialDelayMs { get; set; } = 500;

    /// <summary>Retry delay cap for transient failures; retries continue until success, disconnect, or shutdown.</summary>
    public int TransientRetryMaxDelaySeconds { get; set; } = 30;
}

public sealed class TimeSeriesBackfillOptions
{
    public const string SectionName = "TimeSeriesBackfill";

    public bool Enabled { get; set; } = true;

    /// <summary>Pause between backfill passes while nothing is failing.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Backoff cap between passes while the time-series store keeps failing.</summary>
    public int MaxBackoffSeconds { get; set; } = 600;

    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Only rows older than this are picked up, so the backfill never races
    /// the live pipeline's own write of a just-received reading.
    /// </summary>
    public int MinAgeSeconds { get; set; } = 120;

    /// <summary>A row that fails this many backfill attempts is left for an operator (it stays Persisted in PostgreSQL).</summary>
    public int MaxAttempts { get; set; } = 50;
}

/// <summary>
/// Bounds the growth of RawDeviceMessages. The table holds the full payload
/// of every message ever received, and the acknowledgement path writes to it
/// before anything else — so a disk filled by old rows does not merely lose
/// history, it stops ingestion. See docs/architecture/data-flow.md.
/// </summary>
public sealed class RawMessageRetentionOptions
{
    public const string SectionName = "RawMessageRetention";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a raw message is kept. Long enough to debug a vendor's
    /// payload and replay it after a decoder fix; short enough that the
    /// table has a ceiling.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Pause between passes once nothing is left to delete.</summary>
    public int IntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Rows removed per statement. Deletes run in batches so the hot insert
    /// path is never blocked behind one long-running statement; a pass keeps
    /// batching until it finds nothing more to delete.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Upper bound on batches per pass, so one pass cannot run unbounded.</summary>
    public int MaxBatchesPerPass { get; set; } = 50;
}
