namespace Bms.Protocols.Configuration;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; }
    public string ClientId { get; set; } = "bms-ingestion";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int KeepAliveSeconds { get; set; } = 30;
    public string TelemetryTopicFilter { get; set; } = "iot/+/+/+/telemetry";
    public string HeartbeatTopicFilter { get; set; } = "iot/+/+/+/heartbeat";
    public string CommandAckTopicFilter { get; set; } = "iot/+/+/+/commands/ack";

    /// <summary>
    /// MQTT 5 Session Expiry Interval. Non-zero makes the broker keep this
    /// client's session — its subscriptions and every QoS 1 message published
    /// to them — while the client is disconnected, for up to this long. 0 (the
    /// MQTT 5 default) discards the session the moment the connection drops,
    /// so anything published during the outage is lost. Bms.Ingestion sets
    /// 86400; Bms.Api's real-time bridge keeps 0 (stale UI events are useless).
    /// </summary>
    public uint SessionExpirySeconds { get; set; }

    /// <summary>
    /// How many un-acknowledged QoS 1 messages the broker may push before
    /// waiting for acks — bounds what sits in this process's memory while a
    /// slow dependency holds processing up. 0 leaves the protocol default (65535).
    /// </summary>
    public ushort ReceiveMaximum { get; set; } = 100;

    public int ReconnectMinDelaySeconds { get; set; } = 1;
    public int ReconnectMaxDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Retained topic carrying this client's liveness: a periodic "online"
    /// heartbeat, and the Last Will "offline" if the connection dies.
    /// Defaults to bms/ingestion/{ClientId}/status.
    /// </summary>
    public string? StatusTopic { get; set; }

    public string EffectiveStatusTopic => string.IsNullOrWhiteSpace(StatusTopic) ? $"bms/ingestion/{ClientId}/status" : StatusTopic;
}
