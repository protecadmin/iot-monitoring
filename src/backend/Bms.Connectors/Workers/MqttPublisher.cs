using System.Threading.Channels;
using Bms.Connectors.Runtime;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Bms.Connectors.Workers;

/// <summary>
/// Publishes readings to the broker as QoS 1, in order. While the broker is
/// unreachable readings wait in a bounded queue; when it is full the oldest
/// are dropped (and counted) rather than the service running out of memory.
/// </summary>
public sealed class MqttPublisher(IOptions<ConnectorServiceOptions> options, ServiceStats stats, ILogger<MqttPublisher> logger) : BackgroundService
{
    private readonly Channel<(string Topic, byte[] Payload)> _queue = Channel.CreateBounded<(string, byte[])>(
        new BoundedChannelOptions(Math.Max(100, options.Value.PublishQueueSize)) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
        _ => Interlocked.Increment(ref stats.Dropped));

    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();

    public void Enqueue(string topic, byte[] payload) => _queue.Writer.TryWrite((topic, payload));

    /// <summary>Takes the next queued reading without publishing it — for tests.</summary>
    internal bool TryDequeue(out (string Topic, byte[] Payload) item) => _queue.Reader.TryRead(out item);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Mqtt;
        await foreach (var (topic, payload) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                        await ConnectAsync(settings, stoppingToken);

                    var message = new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(payload)
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build();
                    var result = await _client.PublishAsync(message, stoppingToken);
                    if (!result.IsSuccess)
                        throw new InvalidOperationException($"Broker refused the publish: {result.ReasonCode} {result.ReasonString}");
                    Interlocked.Increment(ref stats.Published);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    stats.LastError = $"MQTT: {ex.Message}";
                    logger.LogWarning("Publishing to {Host}:{Port} failed ({Message}); retrying in 5 s", settings.Host, settings.Port, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
    }

    private async Task ConnectAsync(ConnectorServiceOptions.MqttSettings s, CancellationToken ct)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(s.ClientId)
            .WithTcpServer(s.Host, s.Port)
            .WithCredentials(s.Username, s.Password)
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));
        if (s.UseTls)
            builder = builder.WithTlsOptions(o => o.UseTls());
        await _client.ConnectAsync(builder.Build(), ct);
        logger.LogInformation("Connected to MQTT broker {Host}:{Port} as {User}", s.Host, s.Port, s.Username);
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}
