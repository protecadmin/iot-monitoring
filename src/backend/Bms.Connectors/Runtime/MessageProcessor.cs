using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Connectors.Api;
using Bms.Connectors.Core;
using Bms.Connectors.Workers;
using Bms.Domain.Connectors;
using Microsoft.Extensions.Options;

namespace Bms.Connectors.Runtime;

/// <summary>
/// One framed message, start to finish: offer it to a waiting capture, decode
/// it, find the device it belongs to, drop an immediate repeat, and queue it
/// for MQTT as a normal BMS reading.
/// </summary>
public sealed class MessageProcessor(
    ServiceState state,
    ServiceStats stats,
    MqttPublisher publisher,
    BmsApiClient api,
    IOptions<ConnectorServiceOptions> options,
    TimeProvider clock,
    ILogger<MessageProcessor> logger)
{
    private readonly ConcurrentDictionary<(Guid, string), (byte[] Hash, DateTimeOffset At)> _lastMessage = new();
    private readonly ConcurrentDictionary<string, long> _sequence = new();
    // Starts from the clock so a restart never reuses a sequence number.
    private readonly long _sequenceBase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Process(Connector connector, FramedMessage message, string remote)
    {
        var at = clock.GetUtcNow();
        var counters = stats.For(connector);
        counters.LastMessageAt = at;
        OfferToCapture(connector, message);

        if (message.Oversize) { stats.Reject(connector, $"message over {connector.MaxMessageBytes} bytes from {remote}"); return; }
        if (message.Incomplete) { stats.Reject(connector, $"incomplete message from {remote}"); return; }

        var decoded = MessageDecoder.Decode(connector, message.Bytes);
        foreach (var source in decoded.UnmappedSources)
            ServiceStats.Sight(stats.UnmappedKeys, connector.Id, source, decoded.Values.GetValueOrDefault(source), at);

        if (!decoded.Usable)
        {
            stats.Reject(connector, decoded.Error ?? "no sensor ID");
            logger.LogDebug("Rejected message on {Connector} from {Remote}: {Reason}", connector.Name, remote, decoded.Error);
            return;
        }

        var sensorId = decoded.SensorId!;
        if (state.Route(connector.Id, sensorId) is not { } route)
        {
            Interlocked.Increment(ref stats.Unknown);
            Interlocked.Increment(ref counters.Unknown);
            ServiceStats.Sight(stats.UnknownDevices, connector.Id, sensorId, Preview(connector, message), at);
            return;
        }

        if (IsRepeat(connector.Id, sensorId, message.Bytes, at))
            return;

        Interlocked.Increment(ref stats.Accepted);
        Interlocked.Increment(ref counters.Accepted);
        publisher.Enqueue(route.Topic, Payload(decoded, route.DeviceCode, at));
    }

    /// <summary>The reading as BMS expects it: parameter keys, the time, where that time came from, and a sequence number.</summary>
    private byte[] Payload(DecodedMessage decoded, string deviceCode, DateTimeOffset arrivedAt)
    {
        var body = new Dictionary<string, object>(decoded.Fields.Count + 3);
        foreach (var (key, value) in decoded.Fields) body[key] = value;
        body["ts"] = (decoded.Time ?? arrivedAt).ToString("O", CultureInfo.InvariantCulture);
        if (decoded.Time is null) body["tsSource"] = "received"; // the device sends no time of its own
        body["seq"] = _sequenceBase + _sequence.AddOrUpdate(deviceCode, 1, (_, n) => n + 1);
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    private bool IsRepeat(Guid connectorId, string sensorId, byte[] bytes, DateTimeOffset at)
    {
        var hash = SHA256.HashData(bytes);
        var window = TimeSpan.FromSeconds(options.Value.DuplicateWindowSeconds);
        var repeat = false;
        _lastMessage.AddOrUpdate((connectorId, sensorId), _ => (hash, at), (_, last) =>
        {
            repeat = at - last.At < window && last.Hash.AsSpan().SequenceEqual(hash);
            return (hash, at);
        });
        return repeat;
    }

    private void OfferToCapture(Connector connector, FramedMessage message)
    {
        if (!state.PendingCaptures.TryRemove(connector.Id, out var captureId))
            return;
        var isHex = connector.Format == ConnectorFormat.Binary;
        var sample = isHex ? Convert.ToHexString(message.Bytes) : Encoding.UTF8.GetString(message.Bytes);
        _ = Task.Run(async () =>
        {
            try { await api.CompleteCaptureAsync(captureId, sample, isHex, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Couldn't hand the captured message to BMS"); }
        });
    }

    /// <summary>Enough of the message to recognise the device, not to store its data.</summary>
    private static string Preview(Connector connector, FramedMessage message)
    {
        var text = connector.Format == ConnectorFormat.Binary ? Convert.ToHexString(message.Bytes) : Encoding.UTF8.GetString(message.Bytes);
        return text.Length > 200 ? text[..200] + "…" : text;
    }
}
