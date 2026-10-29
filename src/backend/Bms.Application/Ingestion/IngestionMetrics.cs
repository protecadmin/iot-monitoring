using System.Diagnostics.Metrics;

namespace Bms.Application.Ingestion;

/// <summary>
/// Ingestion reliability counters, published on the "Bms.Ingestion" meter
/// (System.Diagnostics.Metrics — readable with `dotnet-counters monitor
/// --counters Bms.Ingestion` or any OpenTelemetry exporter added later).
/// </summary>
public static class IngestionMetrics
{
    public const string MeterName = "Bms.Ingestion";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>("bms.ingestion.messages_received");
    public static readonly Counter<long> MessagesRedelivered = Meter.CreateCounter<long>("bms.ingestion.messages_redelivered");
    public static readonly Counter<long> MessagesPersisted = Meter.CreateCounter<long>("bms.ingestion.messages_persisted");
    public static readonly Counter<long> MessagesRejected = Meter.CreateCounter<long>("bms.ingestion.messages_rejected");
    public static readonly Counter<long> MessagesDuplicate = Meter.CreateCounter<long>("bms.ingestion.messages_duplicate");
    public static readonly Counter<long> MessagesAcknowledged = Meter.CreateCounter<long>("bms.ingestion.messages_acknowledged");
    public static readonly Counter<long> TransientFailures = Meter.CreateCounter<long>("bms.ingestion.transient_failures");
    public static readonly Counter<long> PermanentFailures = Meter.CreateCounter<long>("bms.ingestion.permanent_failures");
    public static readonly Counter<long> TimeSeriesWriteFailures = Meter.CreateCounter<long>("bms.ingestion.timeseries_write_failures");
    public static readonly Counter<long> TimeSeriesBackfilled = Meter.CreateCounter<long>("bms.ingestion.timeseries_backfilled");
    public static readonly Counter<long> BrokerReconnects = Meter.CreateCounter<long>("bms.ingestion.broker_reconnects");
    public static readonly Counter<long> RawMessagesDeleted = Meter.CreateCounter<long>("bms.ingestion.raw_messages_deleted");
    public static readonly Histogram<double> ProcessingLatencyMs = Meter.CreateHistogram<double>("bms.ingestion.processing_latency", "ms");
}
