namespace Bms.Connectors;

/// <summary>"ConnectorService" settings.</summary>
public sealed class ConnectorServiceOptions
{
    public const string SectionName = "ConnectorService";

    /// <summary>The BMS API, e.g. http://api:8080. Connectors and devices are read from it.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";
    /// <summary>Must equal the API's Connectors:ServiceApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int ConfigRefreshSeconds { get; set; } = 60;
    public int HeartbeatSeconds { get; set; } = 60;
    public int CapturePollSeconds { get; set; } = 5;

    /// <summary>Listen address for connector ports; 0.0.0.0 for devices on the internet.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";
    public int MaxConnections { get; set; } = 500;
    /// <summary>Per source IP — one misbehaving client can't take every slot.</summary>
    public int MaxConnectionsPerIp { get; set; } = 20;
    /// <summary>A connection that sends nothing this long after opening is closed.</summary>
    public int FirstByteTimeoutSeconds { get; set; } = 10;
    /// <summary>Silence after which a connection is closed (flushing a last line sent without a newline).</summary>
    public int IdleTimeoutSeconds { get; set; } = 30;
    /// <summary>The same message from the same sensor within this window is a repeat, not a new reading.</summary>
    public int DuplicateWindowSeconds { get; set; } = 2;
    /// <summary>Readings held while the broker is unreachable; beyond this the oldest wait is dropped and counted.</summary>
    public int PublishQueueSize { get; set; } = 10000;

    public MqttSettings Mqtt { get; set; } = new();

    public sealed class MqttSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string Username { get; set; } = "bms-connectors";
        public string Password { get; set; } = string.Empty;
        public string ClientId { get; set; } = "bms-connectors";
        public bool UseTls { get; set; }
    }
}
