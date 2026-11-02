using Bms.Api.Common;
using Bms.Api.Dtos;
using Bms.Domain.Connectors;

namespace Bms.Api.Connectors;

/// <summary>Connector entity ⇄ API shape. Enum names travel as their C# names ("NewLine", "Csv").</summary>
public static class ConnectorMapper
{
    public static ConnectorDto ToDto(Connector c, int deviceCount = 0, int unmappedKeyCount = 0) => new(
        c.Id, c.Name, c.Vendor, c.DefaultDeviceTypeId,
        c.Protocol.ToString(), c.Port, c.Framing.ToString(), c.FixedLength, c.EndCharacter, c.MaxMessageBytes,
        c.Format.ToString(), c.CsvSeparator, c.CsvOptionalPrefix, c.PairSeparator, c.KeyValueSeparator,
        c.BinaryHeaderHex, c.BinaryHeaderOffset, c.BinaryChecksum.ToString(),
        c.DeviceCodePrefix, c.IsEnabled,
        c.Mappings.Select(m => new ConnectorMappingDto(m.Source, m.Target.ToString(), m.ParameterKey, m.ByteLength, m.Signed, m.LittleEndian, m.Offset, m.Scale)).ToList(),
        deviceCount, unmappedKeyCount, c.UpdatedAt, c.StartMarker, c.CsvFieldCount);

    /// <summary>Copies a request onto a connector; unknown enum names are a 400, not a silent default.</summary>
    public static void Apply(SaveConnectorRequest r, Connector c)
    {
        c.Name = r.Name?.Trim() ?? string.Empty;
        c.Vendor = string.IsNullOrWhiteSpace(r.Vendor) ? null : r.Vendor.Trim();
        c.DefaultDeviceTypeId = r.DefaultDeviceTypeId;
        c.Protocol = Parse<ConnectorProtocol>(r.Protocol, "protocol");
        c.Port = r.Port;
        c.Framing = Parse<ConnectorFraming>(r.Framing, "framing");
        c.FixedLength = r.FixedLength;
        c.EndCharacter = string.IsNullOrEmpty(r.EndCharacter) ? null : r.EndCharacter;
        c.StartMarker = string.IsNullOrEmpty(r.StartMarker) ? null : r.StartMarker;
        c.CsvFieldCount = r.CsvFieldCount;
        c.MaxMessageBytes = r.MaxMessageBytes ?? 4096;
        c.Format = Parse<ConnectorFormat>(r.Format, "format");
        c.CsvSeparator = string.IsNullOrEmpty(r.CsvSeparator) ? "," : r.CsvSeparator;
        c.CsvOptionalPrefix = r.CsvOptionalPrefix;
        c.PairSeparator = string.IsNullOrEmpty(r.PairSeparator) ? ";" : r.PairSeparator;
        c.KeyValueSeparator = string.IsNullOrEmpty(r.KeyValueSeparator) ? "=" : r.KeyValueSeparator;
        c.BinaryHeaderHex = string.IsNullOrWhiteSpace(r.BinaryHeaderHex) ? null : r.BinaryHeaderHex.Trim();
        c.BinaryHeaderOffset = r.BinaryHeaderOffset;
        c.BinaryChecksum = string.IsNullOrEmpty(r.BinaryChecksum) ? ConnectorChecksum.None : Parse<ConnectorChecksum>(r.BinaryChecksum, "checksum");
        c.DeviceCodePrefix = r.DeviceCodePrefix?.Trim() ?? string.Empty;
        c.IsEnabled = r.IsEnabled;
        c.Mappings = (r.Mappings ?? []).Select(m => new ConnectorMapping
        {
            Source = m.Source?.Trim() ?? string.Empty,
            Target = Parse<ConnectorTarget>(m.Target, "mapping target"),
            ParameterKey = string.IsNullOrWhiteSpace(m.ParameterKey) ? null : m.ParameterKey.Trim(),
            ByteLength = m.ByteLength,
            Signed = m.Signed,
            LittleEndian = m.LittleEndian,
            Offset = m.Offset,
            Scale = m.Scale
        }).ToList();
    }

    private static T Parse<T>(string? value, string what) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw ApiException.BadRequest($"Unknown {what} \"{value}\"", "CONNECTOR_INVALID");
}
