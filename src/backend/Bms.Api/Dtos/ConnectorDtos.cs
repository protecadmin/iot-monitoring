namespace Bms.Api.Dtos;

public sealed record ConnectorMappingDto(
    string Source, string Target, string? ParameterKey,
    int? ByteLength = null, bool Signed = false, bool LittleEndian = false, double Offset = 0, double Scale = 1);

/// <summary>A connector as edited in the UI and as sent to the Connector Service.</summary>
public sealed record ConnectorDto(
    Guid Id, string Name, string? Vendor, Guid? DefaultDeviceTypeId,
    string Protocol, int Port, string Framing, int? FixedLength, string? EndCharacter, int MaxMessageBytes,
    string Format, string CsvSeparator, bool CsvOptionalPrefix, string PairSeparator, string KeyValueSeparator,
    string? BinaryHeaderHex, int BinaryHeaderOffset, string BinaryChecksum,
    string DeviceCodePrefix, bool IsEnabled, IReadOnlyList<ConnectorMappingDto> Mappings,
    int DeviceCount = 0, int UnmappedKeyCount = 0, DateTimeOffset? UpdatedAt = null,
    string? StartMarker = null, int? CsvFieldCount = null);

public sealed record SaveConnectorRequest(
    string Name, string? Vendor, Guid? DefaultDeviceTypeId,
    string Protocol, int Port, string Framing, int? FixedLength, string? EndCharacter, int? MaxMessageBytes,
    string Format, string? CsvSeparator, bool CsvOptionalPrefix, string? PairSeparator, string? KeyValueSeparator,
    string? BinaryHeaderHex, int BinaryHeaderOffset, string? BinaryChecksum,
    string DeviceCodePrefix, bool IsEnabled, IReadOnlyList<ConnectorMappingDto> Mappings,
    string? StartMarker = null, int? CsvFieldCount = null);

/// <param name="SampleIsHex">The sample is binary, written as hex ("01 03 10 4E DF ...").</param>
public sealed record TestConnectorRequest(SaveConnectorRequest Connector, string Sample, bool SampleIsHex);

/// <param name="Values">Everything found in the sample, by source (column, key, path, byte) — the rows to map.</param>
/// <param name="ValidationErrors">What would stop the connector from being saved.</param>
public sealed record ConnectorTestResult(
    IReadOnlyList<ConnectorSampleValue> Values, string? SensorId, string? DeviceCode,
    IReadOnlyDictionary<string, double> Fields, DateTimeOffset? Time,
    IReadOnlyList<string> UnmappedSources, string? Error, IReadOnlyList<string> ValidationErrors);

public sealed record ConnectorSampleValue(string Source, string Value);

public sealed record ConnectorCaptureDto(Guid Id, Guid ConnectorId, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DateTimeOffset? CapturedAt, string? Sample, bool SampleIsHex);

public sealed record ConnectorUnknownDeviceDto(Guid ConnectorId, string ConnectorName, string SensorId, string DeviceCode, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, long MessageCount, string? LastSample);

public sealed record ConnectorUnmappedKeyDto(Guid ConnectorId, string Source, long MessageCount, DateTimeOffset LastSeenAt, string? LastValue);

public sealed record ConnectorServiceStatusDto(
    bool Online, DateTimeOffset? LastHeartbeatAt, string? Version,
    long MessagesAccepted, long MessagesRejected, long MessagesUnknown, long MessagesPublished,
    string? LastError, string? ConnectorsJson, int PortRangeStart, int PortRangeEnd);

// ── Connector Service contract ────────────────────────────────────────────

public sealed record ConnectorServiceDeviceDto(Guid ConnectorId, string SensorId, string DeviceCode, string TenantSlug, string SiteCode);

public sealed record ConnectorServiceConfigDto(IReadOnlyList<ConnectorDto> Connectors, IReadOnlyList<ConnectorServiceDeviceDto> Devices, DateTimeOffset GeneratedAt);

public sealed record ConnectorHeartbeatRequest(
    string? Version, long MessagesAccepted, long MessagesRejected, long MessagesUnknown, long MessagesPublished,
    string? LastError, string? ConnectorsJson);

public sealed record ConnectorSightingDto(Guid ConnectorId, string Key, long Count, DateTimeOffset LastSeenAt, string? Sample);

public sealed record ConnectorCaptureResultRequest(string Sample, bool SampleIsHex);
