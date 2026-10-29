namespace Bms.Domain.Telemetry;

/// <summary>
/// The single internal telemetry shape every transport/protocol/payload
/// format converges into. Nothing downstream of this (validation, storage,
/// alerting, dashboard) should ever need to know which vendor or wire
/// format a reading originated from. See docs/protocols/normalization.md.
///
/// Fields is a dynamic parameter-name -> value bag rather than fixed
/// properties (temperature/humidity/...) — which parameter names are
/// meaningful for a device is defined per Device Type
/// (<see cref="Entities.DeviceTypeParameter"/>), not hardcoded here.
/// </summary>
public sealed class NormalizedTelemetry
{
    public required Guid TenantId { get; init; }
    public required Guid SiteId { get; init; }
    public required Guid DeviceId { get; init; }

    /// <summary>Event time — when the device says the reading happened.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Ingestion time — when the backend received the raw message.</summary>
    public required DateTimeOffset IngestedAt { get; init; }

    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public long? SequenceNumber { get; init; }

    public required IReadOnlyDictionary<string, double> Fields { get; init; }

    /// <summary>The same reading with <paramref name="calculated"/> values added; a value the device sent itself always wins.</summary>
    public NormalizedTelemetry WithCalculated(IEnumerable<KeyValuePair<string, double>> calculated)
    {
        var fields = new Dictionary<string, double>(Fields, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in calculated)
            fields.TryAdd(key, value);
        return new NormalizedTelemetry
        {
            TenantId = TenantId, SiteId = SiteId, DeviceId = DeviceId, Timestamp = Timestamp, IngestedAt = IngestedAt,
            CorrelationId = CorrelationId, SequenceNumber = SequenceNumber, Fields = fields
        };
    }
}
