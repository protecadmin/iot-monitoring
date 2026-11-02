using System.Globalization;
using Bms.Domain.Connectors;

namespace Bms.Connectors.Core;

/// <summary>What makes a connector unusable — checked before it is saved, so the Connector Service never runs a broken one.</summary>
public static class ConnectorValidator
{
    private static readonly HashSet<int> ByteLengths = [1, 2, 4];

    /// <param name="portMin">The open port range (inclusive); ports outside it would never be reachable.</param>
    public static IReadOnlyList<string> Validate(Connector c, int portMin, int portMax)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(c.Name)) errors.Add("Name is required");
        if (c.Port < portMin || c.Port > portMax) errors.Add($"Port must be between {portMin} and {portMax} (the range opened for connectors)");
        if (c.MaxMessageBytes is < 16 or > 65536) errors.Add("Max message size must be between 16 and 65536 bytes");
        if (string.IsNullOrWhiteSpace(c.DeviceCodePrefix)) errors.Add("Device code prefix is required (e.g. \"fgwm400-\")");
        else if (c.DeviceCodePrefix.Length > 24 || c.DeviceCodePrefix.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            errors.Add("Device code prefix: up to 24 letters, digits, '-' or '_'");

        switch (c.Framing)
        {
            case ConnectorFraming.StartMarker when string.IsNullOrWhiteSpace(c.StartMarker) || c.StartMarker.Length > 64 || c.StartMarker.Any(ch => ch < 32 || ch > 126):
                errors.Add("Start marker must be 1 to 64 printable ASCII characters");
                break;
            case ConnectorFraming.FixedLength when c.FixedLength is not (> 0 and <= 65536):
                errors.Add("Fixed length must be a number of bytes");
                break;
            case ConnectorFraming.EndCharacter when string.IsNullOrEmpty(c.EndCharacter) || c.EndCharacter.Length != 1:
                errors.Add("End character must be exactly one character");
                break;
        }

        if (c.Framing == ConnectorFraming.StartMarker && c.Format != ConnectorFormat.Csv)
            errors.Add("Start marker framing requires CSV format");
        if (c.Framing == ConnectorFraming.StartMarker && c.CsvFieldCount is null)
            errors.Add("Set the expected CSV field count for start marker framing");
        if (c.CsvFieldCount is not null && (c.CsvFieldCount < 2 || c.CsvFieldCount > 128))
            errors.Add("Expected CSV field count must be between 2 and 128");

        if (c.Format == ConnectorFormat.Binary)
        {
            if (c.Framing is not (ConnectorFraming.FixedLength or ConnectorFraming.OnePerConnection))
                errors.Add("Binary messages need Fixed length or One message per connection framing — a newline byte can appear inside binary data");
            if (!string.IsNullOrWhiteSpace(c.BinaryHeaderHex))
            {
                try { _ = Convert.FromHexString(c.BinaryHeaderHex.Replace(" ", "")); }
                catch (FormatException) { errors.Add("Header must be hex bytes, e.g. \"03 10\""); }
            }
        }
        if (c.Format == ConnectorFormat.Csv && string.IsNullOrEmpty(c.CsvSeparator)) errors.Add("CSV separator is required");
        if (c.Format == ConnectorFormat.KeyValue && (string.IsNullOrEmpty(c.PairSeparator) || string.IsNullOrEmpty(c.KeyValueSeparator)))
            errors.Add("Key = value format needs both separators");

        // No mapping at all is a draft: saved so the service listens and "Capture
        // from port" can grab a real message to map from. Every message is
        // rejected ("no sensor ID") until the mapping is added.
        if (c.Mappings.Count == 0)
            return errors;

        var sensorIds = c.Mappings.Count(m => m.Target == ConnectorTarget.SensorId);
        if (sensorIds != 1) errors.Add(sensorIds == 0 ? "Map one part of the message to Sensor ID" : "Only one part of the message can be the Sensor ID");
        if (c.Mappings.Count(m => m.Target == ConnectorTarget.Time) > 1) errors.Add("Only one part of the message can be the time");

        var parameters = c.Mappings.Where(m => m.Target == ConnectorTarget.Parameter).ToList();
        if (parameters.Count == 0) errors.Add("Map at least one value to a parameter");
        if (parameters.Any(m => string.IsNullOrWhiteSpace(m.ParameterKey))) errors.Add("Every value mapped to a parameter needs the parameter chosen");
        foreach (var duplicate in parameters.GroupBy(m => m.ParameterKey, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"\"{duplicate.Key}\" is mapped more than once");
        foreach (var duplicate in c.Mappings.GroupBy(m => m.Source, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"\"{duplicate.Key}\" is mapped more than once");

        foreach (var m in c.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Source)) { errors.Add("Every mapping needs a source"); continue; }
            if (m.Scale == 0) errors.Add($"Scale for \"{m.Source}\" can't be 0");
            switch (c.Format)
            {
                case ConnectorFormat.Csv when !IsIndex(m.Source, 1):
                    errors.Add($"CSV source \"{m.Source}\" must be a column number (1, 2, ...)");
                    break;
                case ConnectorFormat.Binary when !IsIndex(m.Source, 0):
                    errors.Add($"Binary source \"{m.Source}\" must be a start byte (0, 1, ...)");
                    break;
                case ConnectorFormat.Binary when m.Target != ConnectorTarget.Ignore && !ByteLengths.Contains(m.ByteLength ?? 1):
                    errors.Add($"Binary value at byte {m.Source} must be 1, 2 or 4 bytes long");
                    break;
                case ConnectorFormat.Binary when c.FixedLength is { } length && IsIndex(m.Source, 0)
                                                 && int.Parse(m.Source, CultureInfo.InvariantCulture) + (m.ByteLength ?? 1) > length:
                    errors.Add($"Binary value at byte {m.Source} runs past the {length}-byte message");
                    break;
            }
        }
        return errors;
    }

    private static bool IsIndex(string s, int min) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min;
}
