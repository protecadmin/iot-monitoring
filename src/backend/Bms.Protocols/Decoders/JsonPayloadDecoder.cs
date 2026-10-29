using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions.Payloads;
using Bms.Domain;

namespace Bms.Protocols.Decoders;

/// <summary>
/// Parses JSON into a flat field dictionary — nested objects are flattened
/// with dot-separated keys (e.g. Vendor C's <c>data.temperature</c> becomes
/// key "data.temperature") so a flat field-map can address them without a
/// bespoke decoder per vendor. See docs/protocols/payload-decoding.md.
/// </summary>
public sealed class JsonPayloadDecoder : IPayloadDecoder
{
    private static readonly string[] SequenceKeys = ["seq", "sequence", "sequenceNumber"];
    private static readonly string[] TimestampKeys = ["timestamp", "ts", "time"];

    public PayloadFormat Format => PayloadFormat.Json;

    public DecodedPayload Decode(ReadOnlyMemory<byte> raw)
    {
        using var document = JsonDocument.Parse(raw);
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        Flatten(document.RootElement, prefix: null, fields);

        long? sequence = null;
        foreach (var key in SequenceKeys)
        {
            if (fields.TryGetValue(key, out var v) && v is double d) { sequence = (long)d; break; }
        }

        DateTimeOffset? timestamp = null;
        foreach (var key in TimestampKeys)
        {
            if (fields.TryGetValue(key, out var v) && v is string s && DateTimeOffset.TryParse(s, out var parsed))
            {
                timestamp = parsed;
                break;
            }
        }

        return new DecodedPayload
        {
            Fields = fields,
            SequenceNumber = sequence,
            DeviceTimestamp = timestamp,
            TimestampIsArrival = fields.TryGetValue("tsSource", out var source) && source is string text
                                 && string.Equals(text, "received", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, object?> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    Flatten(property.Value, key, into);
                }
                break;

            case JsonValueKind.Number:
                into[prefix ?? string.Empty] = element.GetDouble();
                break;

            case JsonValueKind.String:
                into[prefix ?? string.Empty] = element.GetString();
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                into[prefix ?? string.Empty] = element.GetBoolean();
                break;

            default:
                // Arrays and null are not part of the flat telemetry field
                // set this platform maps from; ignored rather than thrown,
                // consistent with "a malformed/unexpected shape must not
                // crash ingestion."
                break;
        }
    }
}
