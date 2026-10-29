using Bms.Application.Abstractions.DeviceManagement;
using Bms.Protocols.Transports;
using Microsoft.Extensions.Options;

namespace Bms.Protocols.Realtime;

public sealed class IngestionHealthOptions
{
    public const string SectionName = "IngestionHealth";

    /// <summary>False restores the old behaviour: device silence is always trusted.</summary>
    public bool PauseDeviceSilenceChecksWhenUnhealthy { get; set; } = true;

    /// <summary>The ingestion service's retained status topic (its Mqtt:StatusTopic).</summary>
    public string StatusTopic { get; set; } = "bms/ingestion/bms-ingestion/status";

    /// <summary>No heartbeat newer than this means ingestion is not running (its heartbeat is every 15 s).</summary>
    public int StaleAfterSeconds { get; set; } = 60;

    /// <summary>
    /// After ingestion becomes healthy again, how long to keep distrusting
    /// device silence while it drains what the broker queued during the
    /// outage. Size it to the longest backlog drain you expect.
    /// </summary>
    public int RecoveryGraceSeconds { get; set; } = 180;
}

/// <summary>
/// Bms.Api's view of the ingestion service, fed by the retained status
/// messages RealtimeBridgeService receives (see IngestionStatusPublisher).
/// Timestamps come from the ingestion host's clock; both services normally
/// share a host, so skew is negligible next to the thresholds above.
/// </summary>
public sealed class IngestionHealthTracker(Func<bool> brokerConnected, IOptions<IngestionHealthOptions> options) : IIngestionHealthMonitor
{
    public IngestionHealthTracker(MqttTransportAdapter mqtt, IOptions<IngestionHealthOptions> options)
        : this(() => mqtt.IsConnected, options)
    {
    }

    private volatile IngestionStatusMessage? _last;

    public void Update(IngestionStatusMessage message) => _last = message;

    public string? DeviceSilenceUntrustedReason(DateTimeOffset now)
    {
        var settings = options.Value;
        if (!settings.PauseDeviceSilenceChecksWhenUnhealthy)
            return null;

        if (!brokerConnected())
            return "Bms.Api is not connected to the MQTT broker, so ingestion health is unknown (and devices cannot reach the broker either if it is down)";

        var last = _last;
        if (last is null)
            return $"no ingestion status received yet on {settings.StatusTopic}";

        if (last.Status != IngestionStatusMessage.Online)
            return $"ingestion reported '{last.Status}'{(last.Detail is null ? "" : $": {last.Detail}")}";

        if (now - last.Timestamp > TimeSpan.FromSeconds(settings.StaleAfterSeconds))
            return $"last ingestion heartbeat is from {last.Timestamp:O} — ingestion is not running";

        if (last.OnlineSince is { } since && now - since < TimeSpan.FromSeconds(settings.RecoveryGraceSeconds))
            return $"ingestion recovered at {since:O} and may still be draining queued messages";

        return null;
    }
}
