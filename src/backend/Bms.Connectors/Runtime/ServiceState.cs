using System.Collections.Concurrent;
using Bms.Connectors.Api;
using Bms.Domain.Connectors;

namespace Bms.Connectors.Runtime;

/// <summary>What the service currently knows: connectors and device routes from BMS, and the captures waiting for a message.</summary>
public sealed class ServiceState
{
    private volatile ServiceConfig _config = new([], new Dictionary<(Guid, string), DeviceRoute>());

    public ServiceConfig Config => _config;
    public void Apply(ServiceConfig config) => _config = config;

    public DeviceRoute? Route(Guid connectorId, string sensorId) =>
        _config.Devices.TryGetValue((connectorId, sensorId), out var route) ? route : null;

    /// <summary>Connector id → capture id waiting for that connector's next message.</summary>
    public ConcurrentDictionary<Guid, Guid> PendingCaptures { get; } = new();
}

/// <summary>Counters for the heartbeat and the sightings reported back to BMS.</summary>
public sealed class ServiceStats
{
    public long Accepted;
    public long Rejected;
    public long Unknown;
    public long Published;
    public long Dropped;
    public volatile string? LastError;

    public ConcurrentDictionary<Guid, ConnectorCounters> PerConnector { get; } = new();

    // (connector, key) → (count, last seen, sample) since the last report.
    public ConcurrentDictionary<(Guid, string), Sighting> UnknownDevices { get; } = new();
    public ConcurrentDictionary<(Guid, string), Sighting> UnmappedKeys { get; } = new();

    public ConnectorCounters For(Connector c) => PerConnector.GetOrAdd(c.Id, _ => new ConnectorCounters { Name = c.Name, Port = c.Port });

    public void Reject(Connector c, string reason)
    {
        Interlocked.Increment(ref Rejected);
        Interlocked.Increment(ref For(c).Rejected);
        LastError = $"{c.Name}: {reason}";
        For(c).LastError = reason;
    }

    public static void Sight(ConcurrentDictionary<(Guid, string), Sighting> into, Guid connectorId, string key, string? sample, DateTimeOffset at) =>
        into.AddOrUpdate((connectorId, key), _ => new Sighting(1, at, sample), (_, s) => new Sighting(s.Count + 1, at, sample ?? s.Sample));
}

public sealed class ConnectorCounters
{
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public volatile bool Listening;
    public long Accepted;
    public long Rejected;
    public long Unknown;
    public long Connections;
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? LastError { get; set; }
}

public sealed record Sighting(long Count, DateTimeOffset LastSeenAt, string? Sample);
