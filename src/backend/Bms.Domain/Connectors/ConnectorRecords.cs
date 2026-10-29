using Bms.Domain.Common;

namespace Bms.Domain.Connectors;

/// <summary>
/// A sensor ID that sent messages through a connector but matches no device —
/// usually a device installed before it was registered, or a typo. Shown on
/// the Connectors page with an "Add device" shortcut.
/// </summary>
public class ConnectorUnknownDevice : Entity
{
    public Guid ConnectorId { get; set; }
    public string SensorId { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public long MessageCount { get; set; }
    /// <summary>The latest message, shortened — enough to recognise the device, not to store its data.</summary>
    public string? LastSample { get; set; }
}

/// <summary>A part of incoming messages no mapping uses — a field the device started sending, or one not mapped yet.</summary>
public class ConnectorUnmappedKey : Entity
{
    public Guid ConnectorId { get; set; }
    public string Source { get; set; } = string.Empty;
    public long MessageCount { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? LastValue { get; set; }
}

/// <summary>The Connector Service's latest heartbeat — one row; its age says whether the service is running.</summary>
public class ConnectorServiceStatus : Entity
{
    public DateTimeOffset LastHeartbeatAt { get; set; }
    public string? Version { get; set; }
    public long MessagesAccepted { get; set; }
    public long MessagesRejected { get; set; }
    public long MessagesUnknown { get; set; }
    public long MessagesPublished { get; set; }
    public string? LastError { get; set; }
    /// <summary>Per connector, as JSON: listening state and counters.</summary>
    public string? ConnectorsJson { get; set; }
}

/// <summary>
/// "Capture from port": the UI asks for the next real message on a
/// connector's port; the Connector Service fills it in when one arrives.
/// </summary>
public class ConnectorCapture : Entity
{
    public Guid ConnectorId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    /// <summary>The captured message: text as is, binary as hex.</summary>
    public string? Sample { get; set; }
    public bool SampleIsHex { get; set; }
}
