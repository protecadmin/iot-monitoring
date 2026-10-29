using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// The untouched message as received, before any decoding/mapping. Always
/// persisted, even when processing later fails, so it's available for
/// debugging, vendor troubleshooting, and replay. See
/// docs/protocols/payload-decoding.md.
/// </summary>
public class RawDeviceMessage : Entity
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid DeviceId { get; set; }

    public TransportType Transport { get; set; }
    public ProtocolType Protocol { get; set; }
    public string? Topic { get; set; }
    public string? Endpoint { get; set; }
    public PayloadFormat PayloadFormat { get; set; }

    public byte[] RawPayload { get; set; } = Array.Empty<byte>();

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeviceTimestamp { get; set; }
    public long? SequenceNumber { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public MessageProcessingStatus ProcessingStatus { get; set; } = MessageProcessingStatus.Received;
    public string? ProcessingError { get; set; }

    /// <summary>
    /// Stable identity of this reading as the device sent it, built only
    /// from values the device itself supplies (sequence number and/or its
    /// own timestamp), so a redelivery of the same reading produces the
    /// same key. Null when the device supplies no timestamp — such a
    /// message has no identity that survives a redelivery without also
    /// matching legitimate later readings. Unique per device among
    /// Persisted rows.
    /// </summary>
    public string? DedupKey { get; set; }

    /// <summary>
    /// When the normalized reading reached the time-series store. Null on a
    /// Persisted row means it has not (yet) — the backfill worker retries it.
    /// </summary>
    public DateTimeOffset? TimeSeriesWrittenAt { get; set; }

    public int TimeSeriesWriteAttempts { get; set; }
}
