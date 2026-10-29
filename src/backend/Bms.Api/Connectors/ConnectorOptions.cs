namespace Bms.Api.Connectors;

/// <summary>"Connectors" settings.</summary>
public sealed class ConnectorOptions
{
    public const string SectionName = "Connectors";

    /// <summary>
    /// The port range opened for connectors (firewall / AWS security group).
    /// Connectors may only use ports inside it, so adding a vendor never needs
    /// an infrastructure change.
    /// </summary>
    public int PortRangeStart { get; set; } = 12000;
    public int PortRangeEnd { get; set; } = 12099;

    /// <summary>Shared secret the Connector Service sends in the X-Connector-Service-Key header. Empty disables its endpoints.</summary>
    public string ServiceApiKey { get; set; } = string.Empty;

    /// <summary>No heartbeat for this long and the service is shown as offline.</summary>
    public int OfflineAfterSeconds { get; set; } = 180;

    /// <summary>How long "Capture from port" waits for a message — long enough for a sensor that wakes every few minutes.</summary>
    public int CaptureWaitSeconds { get; set; } = 600;
}
