using Bms.Application.Abstractions.Transports;
using Bms.Domain;
using Bms.Domain.Entities;

namespace Bms.Application.Abstractions.Protocols;

/// <summary>
/// Turns a transport-level message into a <see cref="RawDeviceMessage"/> by
/// resolving *identity* (tenant/site/device) from the transport's own
/// addressing (MQTT topic segments, a TCP frame header, an HTTP route
/// value) — never from the payload. Payload interpretation is a separate,
/// later stage (see IPayloadDecoder / TelemetryNormalizer).
/// </summary>
public interface IProtocolHandler
{
    ProtocolType Protocol { get; }

    /// <summary>
    /// Returns null when the message's identity can't be resolved (unknown
    /// device, malformed addressing) — the caller logs and drops it rather
    /// than guessing.
    /// </summary>
    Task<RawDeviceMessage?> TryBuildRawMessageAsync(TransportMessage message, CancellationToken cancellationToken);
}
