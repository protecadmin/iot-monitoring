using System.Text.Json;

namespace Bms.Protocols.Realtime;

/// <summary>
/// Retained liveness message for the ingestion service, published on
/// <see cref="Configuration.MqttOptions.EffectiveStatusTopic"/>: "online"
/// heartbeats while it is processing normally, "degraded" while it is
/// connected but cannot record messages (e.g. PostgreSQL down), "offline"
/// on a clean stop or — as the MQTT Last Will — when the connection dies.
/// </summary>
public sealed record IngestionStatusMessage(string Status, DateTimeOffset Timestamp, string? Detail = null, DateTimeOffset? OnlineSince = null)
{
    public const string Online = "online";
    public const string Degraded = "degraded";
    public const string Offline = "offline";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static IngestionStatusMessage? TryParse(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<IngestionStatusMessage>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Pre-JSON versions published a bare "offline" Last Will.
            return payload.SequenceEqual("offline"u8) ? new IngestionStatusMessage(Offline, DateTimeOffset.MinValue) : null;
        }
    }
}
