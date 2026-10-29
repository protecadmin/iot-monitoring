using Bms.Protocols.Configuration;
using Bms.Protocols.Realtime;
using Bms.Protocols.Transports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// Publishes this service's liveness as a retained message on
/// Mqtt:StatusTopic, so Bms.Api can tell "every device went quiet" apart
/// from "ingestion is down" (see IIngestionHealthMonitor). The Last Will on
/// the same topic covers a crash; this covers a clean stop and a connected
/// but degraded service.
/// Registered after MqttIngestionWorker: hosted services stop in reverse
/// order, so the "offline" below is published before that worker disconnects.
/// </summary>
public sealed class IngestionStatusPublisher(
    MqttTransportAdapter mqtt,
    IngestionHealthState health,
    IOptions<MqttOptions> mqttOptions,
    IOptions<IngestionStatusOptions> statusOptions,
    TimeProvider clock,
    ILogger<IngestionStatusPublisher> logger) : BackgroundService
{
    private readonly string _topic = mqttOptions.Value.EffectiveStatusTopic;
    private string? _lastPublishedStatus;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        mqtt.Connected += OnConnectedAsync;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, statusOptions.Value.HeartbeatSeconds)));

        try
        {
            do
            {
                await PublishCurrentAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            mqtt.Connected -= OnConnectedAsync;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await PublishAsync(new IngestionStatusMessage(IngestionStatusMessage.Offline, clock.GetUtcNow(), "stopped"), cancellationToken);
    }

    private Task OnConnectedAsync(bool sessionPresent, CancellationToken ct) => PublishCurrentAsync(ct);

    private Task PublishCurrentAsync(CancellationToken ct)
    {
        var (degraded, reason, healthySince) = health.Snapshot();
        var message = degraded
            ? new IngestionStatusMessage(IngestionStatusMessage.Degraded, clock.GetUtcNow(), reason)
            : new IngestionStatusMessage(IngestionStatusMessage.Online, clock.GetUtcNow(), null, healthySince);
        return PublishAsync(message, ct);
    }

    private async Task PublishAsync(IngestionStatusMessage message, CancellationToken ct)
    {
        if (!mqtt.IsConnected)
            return;

        try
        {
            await mqtt.PublishAsync(_topic, message.ToUtf8Bytes(), retain: true, ct);
            if (message.Status != _lastPublishedStatus)
                logger.LogInformation("Ingestion status is now {Status} ({Detail})", message.Status, message.Detail ?? "ok");
            _lastPublishedStatus = message.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Publishing ingestion status to {Topic} failed", _topic);
        }
    }
}

public sealed class IngestionStatusOptions
{
    public const string SectionName = "IngestionStatus";

    public int HeartbeatSeconds { get; set; } = 15;
}
