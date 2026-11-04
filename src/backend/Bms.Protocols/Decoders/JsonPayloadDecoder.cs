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
    private static readonly string[] TimestampKeys = ["timestamp", "ts", "time", "timestamp_utc"];

    public PayloadFormat Format => PayloadFormat.Json;

    public DecodedPayload Decode(ReadOnlyMemory<byte> raw)
    {
        using var document = JsonDocument.Parse(raw);
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        Flatten(document.RootElement, prefix: null, fields);

        // Protec's standard MQTT envelope keeps measurements under `values`.
        // Preserve the flattened keys for generic mappings, while also exposing
        // the measurement names at the top level for device parameters such as
        // x/y/z, temperature, and batteryLevel.
        if (document.RootElement.TryGetProperty("values", out var values)
            && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Number)
                    fields.TryAdd(property.Name, property.Value.GetDouble());
            }
        }

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

            if (fields.TryGetValue(key, out v) && v is double unix)
            {
                try
                {
                    timestamp = unix > 10_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)unix)
                        : DateTimeOffset.FromUnixTimeSeconds((long)unix);
                    break;
                }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        return new DecodedPayload
        {
            Fields = fields,
            DeviceIdentifierHint = GetString(fields, "device_id") ?? GetString(fields, "deviceId") ?? GetString(fields, "id"),
            SequenceNumber = sequence,
            DeviceTimestamp = timestamp,
            TimestampIsArrival = fields.TryGetValue("tsSource", out var source) && source is string text
                                 && string.Equals(text, "received", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value as string : null;

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
