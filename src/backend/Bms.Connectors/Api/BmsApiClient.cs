using System.Net.Http.Json;
using Bms.Domain.Connectors;

namespace Bms.Connectors.Api;

/// <summary>The Connector Service's side of /api/connector-service.</summary>
public sealed class BmsApiClient(HttpClient http)
{
    public const string KeyHeader = "X-Connector-Service-Key";

    public async Task<ServiceConfig> GetConfigAsync(CancellationToken ct)
    {
        var dto = await http.GetFromJsonAsync<ConfigDto>("api/connector-service/config", ct)
                  ?? throw new InvalidOperationException("Empty config from the BMS API");
        return new ServiceConfig(
            dto.Connectors.Select(ToConnector).ToList(),
            dto.Devices.ToDictionary(d => (d.ConnectorId, d.SensorId), d => new DeviceRoute(d.DeviceCode, d.TenantSlug, d.SiteCode)));
    }

    public Task HeartbeatAsync(HeartbeatDto heartbeat, CancellationToken ct) => PostAsync("api/connector-service/heartbeat", heartbeat, ct);
    public Task ReportUnknownDevicesAsync(IReadOnlyList<SightingDto> sightings, CancellationToken ct) => PostAsync("api/connector-service/unknown-devices", sightings, ct);
    public Task ReportUnmappedKeysAsync(IReadOnlyList<SightingDto> sightings, CancellationToken ct) => PostAsync("api/connector-service/unmapped-keys", sightings, ct);

    public async Task<IReadOnlyList<CaptureDto>> PendingCapturesAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<CaptureDto>>("api/connector-service/captures/pending", ct) ?? [];

    public Task CompleteCaptureAsync(Guid id, string sample, bool isHex, CancellationToken ct) =>
        PostAsync($"api/connector-service/captures/{id}", new { sample, sampleIsHex = isHex }, ct);

    private async Task PostAsync<T>(string path, T body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Connector ToConnector(ConnectorDto c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Vendor = c.Vendor,
        Protocol = Enum.Parse<ConnectorProtocol>(c.Protocol, true),
        Port = c.Port,
        Framing = Enum.Parse<ConnectorFraming>(c.Framing, true),
        FixedLength = c.FixedLength,
        EndCharacter = c.EndCharacter,
        MaxMessageBytes = c.MaxMessageBytes,
        Format = Enum.Parse<ConnectorFormat>(c.Format, true),
        CsvSeparator = c.CsvSeparator,
        CsvOptionalPrefix = c.CsvOptionalPrefix,
        PairSeparator = c.PairSeparator,
        KeyValueSeparator = c.KeyValueSeparator,
        BinaryHeaderHex = c.BinaryHeaderHex,
        BinaryHeaderOffset = c.BinaryHeaderOffset,
        BinaryChecksum = Enum.Parse<ConnectorChecksum>(c.BinaryChecksum, true),
        DeviceCodePrefix = c.DeviceCodePrefix,
        IsEnabled = c.IsEnabled,
        UpdatedAt = c.UpdatedAt ?? DateTimeOffset.MinValue,
        Mappings = c.Mappings.Select(m => new ConnectorMapping
        {
            Source = m.Source,
            Target = Enum.Parse<ConnectorTarget>(m.Target, true),
            ParameterKey = m.ParameterKey,
            ByteLength = m.ByteLength,
            Signed = m.Signed,
            LittleEndian = m.LittleEndian,
            Offset = m.Offset,
            Scale = m.Scale
        }).ToList()
    };

    private sealed record ConfigDto(List<ConnectorDto> Connectors, List<DeviceDto> Devices);
    private sealed record DeviceDto(Guid ConnectorId, string SensorId, string DeviceCode, string TenantSlug, string SiteCode);
    private sealed record MappingDto(string Source, string Target, string? ParameterKey, int? ByteLength, bool Signed, bool LittleEndian, double Offset, double Scale);
    private sealed record ConnectorDto(
        Guid Id, string Name, string? Vendor, string Protocol, int Port, string Framing, int? FixedLength, string? EndCharacter, int MaxMessageBytes,
        string Format, string CsvSeparator, bool CsvOptionalPrefix, string PairSeparator, string KeyValueSeparator,
        string? BinaryHeaderHex, int BinaryHeaderOffset, string BinaryChecksum, string DeviceCodePrefix, bool IsEnabled,
        List<MappingDto> Mappings, DateTimeOffset? UpdatedAt);
}

public sealed record DeviceRoute(string DeviceCode, string TenantSlug, string SiteCode)
{
    public string Topic => $"iot/{TenantSlug}/{SiteCode}/{DeviceCode}/telemetry";
}

public sealed record ServiceConfig(IReadOnlyList<Connector> Connectors, IReadOnlyDictionary<(Guid ConnectorId, string SensorId), DeviceRoute> Devices);

public sealed record HeartbeatDto(
    string Version, long MessagesAccepted, long MessagesRejected, long MessagesUnknown, long MessagesPublished, string? LastError, string ConnectorsJson);

public sealed record SightingDto(Guid ConnectorId, string Key, long Count, DateTimeOffset LastSeenAt, string? Sample);

public sealed record CaptureDto(Guid Id, Guid ConnectorId, DateTimeOffset ExpiresAt);
