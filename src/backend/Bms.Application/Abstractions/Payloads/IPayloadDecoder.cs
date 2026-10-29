using Bms.Domain;

namespace Bms.Application.Abstractions.Payloads;

/// <summary>
/// Turns raw bytes into a structured shape. Knows nothing about what any
/// field means — which fields are kept is the device type's parameter list
/// (see TelemetryNormalizer). See
/// docs/protocols/payload-decoding.md.
/// </summary>
public interface IPayloadDecoder
{
    PayloadFormat Format { get; }

    DecodedPayload Decode(ReadOnlyMemory<byte> raw);
}
