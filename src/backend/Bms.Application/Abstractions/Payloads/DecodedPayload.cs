namespace Bms.Application.Abstractions.Payloads;

/// <summary>
/// Structured-but-still-vendor-shaped data produced by an IPayloadDecoder.
/// Nested structures (e.g. Vendor C's <c>data.temperature</c>) are
/// flattened with dot-separated keys so a flat field-map can address them.
/// </summary>
public sealed class DecodedPayload
{
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
    public string? DeviceIdentifierHint { get; init; }
    public long? SequenceNumber { get; init; }
    public DateTimeOffset? DeviceTimestamp { get; init; }

    /// <summary>
    /// True when the sender says the timestamp is when the reading arrived,
    /// not when it was measured ("tsSource": "received") — a connector
    /// stamping a device that has no clock.
    /// </summary>
    public bool TimestampIsArrival { get; init; }
}
