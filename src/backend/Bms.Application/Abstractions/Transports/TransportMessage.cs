using Bms.Domain;

namespace Bms.Application.Abstractions.Transports;

/// <summary>
/// What a transport adapter hands upward once it has a complete message —
/// already framed (for TCP), already one full MQTT publish, or one HTTP
/// request body. No payload interpretation has happened yet.
/// </summary>
public sealed record TransportMessage(
    TransportType Transport,
    string Address,
    byte[] Payload,
    DateTimeOffset ReceivedAt,
    string? ConnectionId = null)
{
    /// <summary>
    /// Tells the sender this message is safely recorded and need not be
    /// redelivered. Idempotent and never throws. Null for transports with no
    /// delivery acknowledgement (TCP, HTTP). For MQTT, the adapter also
    /// acknowledges automatically when the handler returns normally, and
    /// never when it throws.
    /// </summary>
    public Func<CancellationToken, Task>? AcknowledgeAsync { get; init; }

    /// <summary>True when the broker marked this as a redelivery of a message sent before a reconnect.</summary>
    public bool IsRedelivery { get; init; }
}
