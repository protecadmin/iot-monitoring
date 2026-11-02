using Bms.Domain.Common;

namespace Bms.Domain.Connectors;

/// <summary>
/// How one kind of device sends data that isn't in BMS's MQTT format — the
/// port it reaches, how its byte stream splits into messages, how a message
/// is laid out, and which part of it becomes which BMS parameter. The
/// Connector Service turns each message into a normal MQTT reading, so
/// ingestion never sees the difference. Platform-wide: one vendor's devices
/// can belong to any company.
/// </summary>
public class Connector : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Vendor { get; set; }

    /// <summary>The type its devices usually are — preselected in the device form and used to offer parameters in the mapping.</summary>
    public Guid? DefaultDeviceTypeId { get; set; }

    public ConnectorProtocol Protocol { get; set; } = ConnectorProtocol.Tcp;
    public int Port { get; set; }

    public ConnectorFraming Framing { get; set; } = ConnectorFraming.NewLine;
    /// <summary>Message length in bytes, for <see cref="ConnectorFraming.FixedLength"/>.</summary>
    public int? FixedLength { get; set; }
    /// <summary>The character that ends a message, for <see cref="ConnectorFraming.EndCharacter"/>.</summary>
    public string? EndCharacter { get; set; }
    /// <summary>Text marking the start of each record in a joined stream.</summary>
    public string? StartMarker { get; set; }
    public int MaxMessageBytes { get; set; } = 4096;

    public ConnectorFormat Format { get; set; } = ConnectorFormat.Csv;

    /// <summary>CSV: the separator between values.</summary>
    public string CsvSeparator { get; set; } = ",";
    /// <summary>CSV: skip a leading text label (e.g. "BW-NB-WM400,") when the next value is the numeric ID.</summary>
    public bool CsvOptionalPrefix { get; set; }
    /// <summary>Optional exact number of CSV fields after removing the text prefix.</summary>
    public int? CsvFieldCount { get; set; }

    /// <summary>Key = value: between pairs (";") and between a key and its value ("=").</summary>
    public string PairSeparator { get; set; } = ";";
    public string KeyValueSeparator { get; set; } = "=";

    /// <summary>Binary: bytes every message must carry at <see cref="BinaryHeaderOffset"/>, in hex ("03 10"); empty for none.</summary>
    public string? BinaryHeaderHex { get; set; }
    public int BinaryHeaderOffset { get; set; }
    public ConnectorChecksum BinaryChecksum { get; set; } = ConnectorChecksum.None;

    /// <summary>Device code = prefix + the sensor ID in the message, e.g. "fgwm400-" + "100000005".</summary>
    public string DeviceCodePrefix { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public List<ConnectorMapping> Mappings { get; set; } = [];
}

/// <summary>One part of a message and where it goes.</summary>
public class ConnectorMapping
{
    /// <summary>
    /// Where the value is: a 1-based column (CSV), a key (key = value), a
    /// dot path (JSON, e.g. "data.temp"), or a 0-based start byte (binary).
    /// Matched exactly, case included.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    public ConnectorTarget Target { get; set; } = ConnectorTarget.Parameter;
    /// <summary>The BMS parameter key, for <see cref="ConnectorTarget.Parameter"/>.</summary>
    public string? ParameterKey { get; set; }

    /// <summary>Binary: 1, 2 or 4 bytes, signed or not, big-endian unless little.</summary>
    public int? ByteLength { get; set; }
    public bool Signed { get; set; }
    public bool LittleEndian { get; set; }

    /// <summary>value = (raw - Offset) * Scale — e.g. (raw - 20000) * 0.01 for an FG-WM400 register.</summary>
    public double Offset { get; set; }
    public double Scale { get; set; } = 1;
}

public enum ConnectorProtocol
{
    Tcp
}

public enum ConnectorFraming
{
    /// <summary>One message per line (\n or \r\n).</summary>
    NewLine,
    /// <summary>Every message is exactly FixedLength bytes.</summary>
    FixedLength,
    /// <summary>A message ends at EndCharacter.</summary>
    EndCharacter,
    /// <summary>The device connects, sends one message, and disconnects.</summary>
    OnePerConnection,
    /// <summary>A new marker starts each record; the last record ends when the connection closes.</summary>
    StartMarker
}

public enum ConnectorFormat
{
    Csv,
    KeyValue,
    Json,
    Binary
}

public enum ConnectorChecksum
{
    None,
    /// <summary>CRC-16/Modbus over every byte but the last two, stored little-endian at the end.</summary>
    Crc16Modbus
}

public enum ConnectorTarget
{
    SensorId,
    Time,
    Parameter,
    Ignore
}
