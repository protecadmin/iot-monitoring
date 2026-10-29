using System.Globalization;
using System.Text;
using System.Text.Json;
using Bms.Domain.Connectors;

namespace Bms.Connectors.Core;

/// <summary>
/// Turns one framed message into a sensor ID and BMS parameter values,
/// following the connector's format and mapping. The same code serves the
/// live Connector Service and the "test with a sample" check in the UI, so a
/// sample decodes exactly as live data will.
/// </summary>
public static class MessageDecoder
{
    public static DecodedMessage Decode(Connector connector, ReadOnlySpan<byte> message)
    {
        if (message.Length > connector.MaxMessageBytes)
            return DecodedMessage.Failed($"Message is {message.Length} bytes; the limit is {connector.MaxMessageBytes}");

        // Every value the message offers, by its source key, in order.
        Dictionary<string, RawValue> values;
        try
        {
            values = connector.Format switch
            {
                ConnectorFormat.Csv => ReadCsv(connector, Text(message)),
                ConnectorFormat.KeyValue => ReadKeyValue(connector, Text(message)),
                ConnectorFormat.Json => ReadJson(message),
                ConnectorFormat.Binary => ReadBinary(connector, message),
                _ => throw new FormatException($"Unsupported format {connector.Format}")
            };
        }
        catch (FormatException ex)
        {
            return DecodedMessage.Failed(ex.Message);
        }
        catch (JsonException ex)
        {
            return DecodedMessage.Failed($"Not valid JSON: {ex.Message}");
        }

        string? sensorId = null;
        DateTimeOffset? time = null;
        var fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in connector.Mappings)
        {
            mapped.Add(mapping.Source);
            if (mapping.Target == ConnectorTarget.Ignore)
                continue;
            if (!values.TryGetValue(mapping.Source, out var raw))
            {
                if (mapping.Target == ConnectorTarget.SensorId)
                    errors.Add($"No sensor ID at {Describe(connector, mapping.Source)}");
                continue; // a reading may simply not carry every field
            }

            switch (mapping.Target)
            {
                case ConnectorTarget.SensorId:
                    sensorId = raw.Text.Trim();
                    // The same rule a device's Sensor ID is registered under — random
                    // traffic must not turn into "unknown devices".
                    if (!IsSensorId(sensorId)) errors.Add($"\"{Shorten(sensorId)}\" at {Describe(connector, mapping.Source)} isn't a sensor ID");
                    break;
                case ConnectorTarget.Time:
                    if (TryTime(raw.Text, out var t)) time = t;
                    else errors.Add($"\"{raw.Text}\" at {Describe(connector, mapping.Source)} is not a time");
                    break;
                case ConnectorTarget.Parameter when !string.IsNullOrWhiteSpace(mapping.ParameterKey):
                    if (raw.Number is { } number || TryNumber(raw.Text, out number))
                        fields[mapping.ParameterKey] = (number - mapping.Offset) * mapping.Scale;
                    else
                        errors.Add($"\"{raw.Text}\" at {Describe(connector, mapping.Source)} is not a number");
                    break;
            }
        }

        if (errors.Count == 0 && fields.Count == 0 && connector.Mappings.Any(m => m.Target == ConnectorTarget.Parameter))
            errors.Add("None of the mapped values are in this message");

        var unmapped = values.Keys.Where(k => !mapped.Contains(k)).ToList();
        return new DecodedMessage(sensorId, time, fields, values.ToDictionary(v => v.Key, v => v.Value.Text), unmapped, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static string Text(ReadOnlySpan<byte> message) => Encoding.UTF8.GetString(message).Trim();

    private static Dictionary<string, RawValue> ReadCsv(Connector connector, string text)
    {
        if (text.Length == 0) throw new FormatException("Empty message");
        var separator = string.IsNullOrEmpty(connector.CsvSeparator) ? "," : connector.CsvSeparator;
        var tokens = text.Split(separator).Select(t => t.Trim()).ToList();
        while (tokens.Count > 0 && tokens[^1].Length == 0) tokens.RemoveAt(tokens.Count - 1);

        // A text label in front of a numeric ID ("BW-NB-WM400,100000005,...") shifts nothing.
        if (connector.CsvOptionalPrefix && tokens.Count > 1 && !IsNumeric(tokens[0]) && IsNumeric(tokens[1]))
            tokens.RemoveAt(0);

        var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        for (var i = 0; i < tokens.Count; i++)
            values[(i + 1).ToString(CultureInfo.InvariantCulture)] = new RawValue(tokens[i]);
        return values;
    }

    private static Dictionary<string, RawValue> ReadKeyValue(Connector connector, string text)
    {
        if (text.Length == 0) throw new FormatException("Empty message");
        var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        foreach (var pair in text.Split(string.IsNullOrEmpty(connector.PairSeparator) ? ";" : connector.PairSeparator))
        {
            var at = pair.IndexOf(string.IsNullOrEmpty(connector.KeyValueSeparator) ? "=" : connector.KeyValueSeparator, StringComparison.Ordinal);
            if (at <= 0) continue;
            values[pair[..at].Trim()] = new RawValue(pair[(at + connector.KeyValueSeparator.Length)..].Trim());
        }
        if (values.Count == 0) throw new FormatException($"No \"key{connector.KeyValueSeparator}value\" pairs found");
        return values;
    }

    private static Dictionary<string, RawValue> ReadJson(ReadOnlySpan<byte> message)
    {
        using var document = JsonDocument.Parse(message.ToArray());
        var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        Flatten(document.RootElement, null, values);
        if (values.Count == 0) throw new FormatException("The JSON has no values");
        return values;
    }

    private static void Flatten(JsonElement element, string? path, Dictionary<string, RawValue> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten(property.Value, path is null ? property.Name : $"{path}.{property.Name}", into);
                break;
            case JsonValueKind.Number:
                into[path ?? ""] = new RawValue(element.GetRawText(), element.GetDouble());
                break;
            case JsonValueKind.String:
                into[path ?? ""] = new RawValue(element.GetString() ?? "");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                into[path ?? ""] = new RawValue(element.GetRawText(), element.GetBoolean() ? 1 : 0);
                break;
        }
    }

    private static Dictionary<string, RawValue> ReadBinary(Connector connector, ReadOnlySpan<byte> message)
    {
        if (connector.Framing == ConnectorFraming.FixedLength && connector.FixedLength is { } length && message.Length != length)
            throw new FormatException($"Expected {length} bytes, got {message.Length}");

        if (!string.IsNullOrWhiteSpace(connector.BinaryHeaderHex))
        {
            var header = Convert.FromHexString(connector.BinaryHeaderHex.Replace(" ", ""));
            if (message.Length < connector.BinaryHeaderOffset + header.Length
                || !message.Slice(connector.BinaryHeaderOffset, header.Length).SequenceEqual(header))
                throw new FormatException($"Header {connector.BinaryHeaderHex} not found at byte {connector.BinaryHeaderOffset}");
        }

        if (connector.BinaryChecksum == ConnectorChecksum.Crc16Modbus)
        {
            if (message.Length < 3) throw new FormatException("Too short for a checksum");
            var crc = Crc16Modbus(message[..^2]);
            if (message[^2] != (byte)(crc & 0xFF) || message[^1] != (byte)(crc >> 8))
                throw new FormatException("Checksum (CRC-16/Modbus) does not match — the message is corrupted");
        }

        var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        foreach (var mapping in connector.Mappings)
        {
            if (!int.TryParse(mapping.Source, NumberStyles.None, CultureInfo.InvariantCulture, out var start)) continue;
            var size = mapping.ByteLength ?? 1;
            if (start < 0 || start + size > message.Length) continue;
            var raw = ReadInteger(message.Slice(start, size), mapping.Signed, mapping.LittleEndian);
            values[mapping.Source] = new RawValue(raw.ToString(CultureInfo.InvariantCulture), raw);
        }
        return values;
    }

    private static long ReadInteger(ReadOnlySpan<byte> bytes, bool signed, bool littleEndian)
    {
        ulong value = 0;
        for (var i = 0; i < bytes.Length; i++)
            value = (value << 8) | bytes[littleEndian ? bytes.Length - 1 - i : i];
        if (!signed) return (long)value;
        var bits = bytes.Length * 8;
        var sign = 1UL << (bits - 1);
        return (value & sign) != 0 ? (long)value - (1L << bits) : (long)value;
    }

    public static ushort Crc16Modbus(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) == 1 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }

    /// <summary>Letters, digits, '-', '_' or '.', up to 64 — what a device's Sensor ID may be.</summary>
    public static bool IsSensorId(string s) =>
        s.Length is > 0 and <= 64 && s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string Shorten(string s) => s.Length > 24 ? s[..24] + "…" : s;

    private static bool IsNumeric(string s) => TryNumber(s, out _);

    private static bool TryNumber(string s, out double number) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    /// <summary>ISO-8601, or Unix time in seconds or milliseconds.</summary>
    private static bool TryTime(string s, out DateTimeOffset time)
    {
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time))
            return true;
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
        {
            time = unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }
        return false;
    }

    private static string Describe(Connector connector, string source) => connector.Format switch
    {
        ConnectorFormat.Csv => $"column {source}",
        ConnectorFormat.Binary => $"byte {source}",
        _ => $"\"{source}\""
    };

    private readonly record struct RawValue(string Text, double? Number = null);
}

/// <param name="Values">Every value found in the message by its source (column, key, path, byte), as text — for the mapping preview.</param>
/// <param name="UnmappedSources">Sources present in the message that no mapping mentions.</param>
/// <param name="Error">Why the message can't be used, or null.</param>
public sealed record DecodedMessage(
    string? SensorId,
    DateTimeOffset? Time,
    IReadOnlyDictionary<string, double> Fields,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> UnmappedSources,
    string? Error)
{
    public static DecodedMessage Failed(string error) =>
        new(null, null, new Dictionary<string, double>(), new Dictionary<string, string>(), [], error);

    public bool Usable => Error is null && !string.IsNullOrEmpty(SensorId);
}
