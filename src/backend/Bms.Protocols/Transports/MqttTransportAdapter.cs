using System.Buffers;
using System.Collections.Concurrent;
using Bms.Application.Abstractions.Transports;
using Bms.Domain;
using Bms.Protocols.Configuration;
using Bms.Protocols.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Bms.Protocols.Transports;

/// <summary>
/// Connection lifecycle for an MQTT broker connection —
/// connect/reconnect/keep-alive/LWT/TLS/session/acknowledgement only.
/// Deliberately knows nothing about which topics matter: subscribing is a
/// protocol/business concern (see docs/mqtt/reliability.md and
/// system-architecture.md), so callers call <see cref="SubscribeAsync"/> for
/// whatever filters they care about; this class only remembers them so a
/// reconnect re-subscribes correctly.
///
/// Received QoS 1 messages are acknowledged manually: when the handler calls
/// <see cref="TransportMessage.AcknowledgeAsync"/>, or when it returns
/// normally — never when it throws. An unacknowledged message stays in the
/// broker's session and is redelivered on the next connection.
/// </summary>
public sealed class MqttTransportAdapter : ITransportAdapter
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttTransportAdapter> _logger;
    private readonly IMqttClient _client;
    private readonly ConcurrentDictionary<string, byte> _subscribedFilters = new();

    // Held for the whole of a connect/retry loop, so a disconnect during an
    // initial connect (or a second caller) can never start a parallel loop.
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private CancellationTokenSource? _reconnectCts;
    private CancellationTokenSource _connectionCts = new();
    private long _connectionGeneration;
    private volatile bool _stopping;

    public event Func<TransportMessage, CancellationToken, Task>? MessageReceived;

    /// <summary>Raised after every successful (re)connect and re-subscribe.</summary>
    public event Func<bool, CancellationToken, Task>? Connected;

    public bool IsConnected => _client.IsConnected;

    public MqttTransportAdapter(IOptions<MqttOptions> options, ILogger<MqttTransportAdapter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        args.AutoAcknowledge = false;

        var generation = Interlocked.Read(ref _connectionGeneration);
        var connectionToken = _connectionCts.Token;
        var acknowledged = 0;

        async Task AcknowledgeAsync(CancellationToken _)
        {
            if (Interlocked.Exchange(ref acknowledged, 1) == 1)
                return;

            // A PUBACK carries the packet id of the connection it arrived on;
            // after a reconnect that id means nothing (or something else), and
            // the broker redelivers the message anyway.
            if (generation != Interlocked.Read(ref _connectionGeneration) || !_client.IsConnected)
            {
                _logger.LogDebug("Not acknowledging message on {Topic}: its connection is gone; the broker will redeliver it",
                    args.ApplicationMessage.Topic);
                return;
            }

            try
            {
                await args.AcknowledgeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Acknowledging message on {Topic} failed; the broker will redeliver it", args.ApplicationMessage.Topic);
            }
        }

        var message = new TransportMessage(
            TransportType.Mqtt,
            args.ApplicationMessage.Topic,
            ToByteArray(args.ApplicationMessage.Payload),
            DateTimeOffset.UtcNow)
        {
            AcknowledgeAsync = AcknowledgeAsync,
            IsRedelivery = args.ApplicationMessage.Dup
        };

        var handler = MessageReceived;
        if (handler is null)
        {
            await AcknowledgeAsync(CancellationToken.None);
            return;
        }

        try
        {
            await handler.Invoke(message, connectionToken);
            await AcknowledgeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Message on {Topic} was not processed and is NOT acknowledged; the broker redelivers it on the next connection",
                args.ApplicationMessage.Topic);
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        await _connectionCts.CancelAsync();

        // MQTTnet raises this for a failed CONNECT attempt too, not
        // just a drop after a successful connection. When our own
        // ConnectAsync retry loop is already running (initial connect,
        // or a reconnect already in progress), starting a second,
        // independent retry loop here would just duplicate attempts.
        if (_stopping || _connectLock.CurrentCount == 0)
            return;

        _logger.LogWarning("MQTT disconnected from {Host}:{Port} (reason: {Reason}, client {ClientId}). Reconnecting...",
            _options.Host, _options.Port, args.Reason, _options.ClientId);
        await ReconnectWithBackoffAsync();
    }

    /// <summary>
    /// Connects, retrying with backoff, until it succeeds or
    /// <paramref name="cancellationToken"/> is cancelled — a broker that
    /// isn't up yet at process startup (e.g. Compose still starting
    /// Mosquitto) must not crash the caller. See docs/mqtt/reliability.md.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            var delay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectMinDelaySeconds));
            var maxDelay = TimeSpan.FromSeconds(Math.Max(delay.TotalSeconds, _options.ReconnectMaxDelaySeconds));

            for (var attempt = 1; !_client.IsConnected; attempt++)
            {
                try
                {
                    await ConnectOnceAsync(cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MQTT connect attempt {Attempt} to {Host}:{Port} failed, retrying in {Delay}s",
                        attempt, _options.Host, _options.Port, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
                }
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ConnectOnceAsync(CancellationToken cancellationToken)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Username, _options.Password)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
            // MQTT 5 explicitly: Clean Start = false resumes an existing
            // session, and the Session Expiry Interval is what makes the
            // broker keep (and queue for) that session while we're away.
            // CleanStart(false) alone, with the MQTT 5 default expiry of 0,
            // is NOT a persistent session.
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(false)
            .WithSessionExpiryInterval(_options.SessionExpirySeconds)
            .WithWillTopic(_options.EffectiveStatusTopic)
            .WithWillPayload(new IngestionStatusMessage(IngestionStatusMessage.Offline, DateTimeOffset.UtcNow, "connection lost").ToUtf8Bytes())
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true);

        if (_options.ReceiveMaximum > 0)
            builder = builder.WithReceiveMaximum(_options.ReceiveMaximum);

        if (_options.UseTls)
            builder = builder.WithTlsOptions(o => o.UseTls());

        var result = await _client.ConnectAsync(builder.Build(), cancellationToken);

        Interlocked.Increment(ref _connectionGeneration);
        var previous = _connectionCts;
        _connectionCts = new CancellationTokenSource();
        previous.Dispose();

        if (_options.SessionExpirySeconds > 0)
        {
            if (result.IsSessionPresent)
                _logger.LogInformation(
                    "MQTT connected to {Host}:{Port} as {ClientId}: resumed persistent session — messages queued while disconnected are now being delivered",
                    _options.Host, _options.Port, _options.ClientId);
            else
                _logger.LogWarning(
                    "MQTT connected to {Host}:{Port} as {ClientId}: NEW session (expiry {Expiry}s) — no earlier session existed or it expired, so nothing was queued for this client",
                    _options.Host, _options.Port, _options.ClientId, _options.SessionExpirySeconds);
        }
        else
        {
            _logger.LogInformation("MQTT connected to {Host}:{Port} as {ClientId} (non-persistent session)",
                _options.Host, _options.Port, _options.ClientId);
        }

        // Re-establish whatever this process had subscribed to before a
        // disconnect. A resumed session still has them, and re-subscribing
        // replaces rather than duplicates a subscription; a new session
        // (expired, or broker state lost) has none.
        foreach (var filter in _subscribedFilters.Keys)
            await SubscribeCoreAsync(filter, cancellationToken);

        if (Connected is { } connected)
        {
            try
            {
                await connected.Invoke(result.IsSessionPresent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT Connected handler failed");
            }
        }
    }

    /// <summary>Subscribes to a topic filter and remembers it across reconnects.</summary>
    public async Task SubscribeAsync(string filter, CancellationToken ct)
    {
        _subscribedFilters[filter] = 0;
        if (_client.IsConnected)
            await SubscribeCoreAsync(filter, ct);
    }

    private async Task SubscribeCoreAsync(string filter, CancellationToken ct)
    {
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(filter)
                .WithAtLeastOnceQoS()
                // Retained messages only on a genuinely new subscription, not
                // again on every reconnect's re-subscribe.
                .WithRetainHandling(MqttRetainHandling.SendAtSubscribeIfNewSubscriptionOnly))
            .Build();
        var result = await _client.SubscribeAsync(subscribeOptions, ct);
        if (result.Items.Any(item => (int)item.ResultCode >= 128))
            throw new InvalidOperationException($"MQTT subscription to '{filter}' was rejected: {string.Join(", ", result.Items.Select(item => item.ResultCode))}");
    }

    private async Task ReconnectWithBackoffAsync()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        try
        {
            await ConnectAsync(_reconnectCts.Token);
            _logger.LogInformation("MQTT reconnected successfully");
        }
        catch (OperationCanceledException)
        {
            // disconnect requested during reconnect attempts — fine
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _reconnectCts?.Cancel();
        if (_client.IsConnected)
        {
            // Clean disconnect so the broker does NOT fire our LWT — see
            // docs/mqtt/reliability.md. With a non-zero session expiry the
            // broker keeps the session and queues for it after this.
            await _client.DisconnectAsync(MqttClientDisconnectOptionsReason.NormalDisconnection, cancellationToken: cancellationToken);
        }
    }

    public Task SendAsync(string address, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        PublishAsync(address, payload, retain: false, cancellationToken);

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, bool retain, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _reconnectCts?.Cancel();
        await DisconnectAsync(CancellationToken.None);
        _client.Dispose();
    }

    private static byte[] ToByteArray(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
            return sequence.FirstSpan.ToArray();

        var array = new byte[sequence.Length];
        sequence.CopyTo(array);
        return array;
    }
}
