using System.Text.Json;
using Bms.Application.Abstractions.DeviceManagement;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Protocols;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Transports;
using Bms.Application.Ingestion;
using Bms.Domain;
using Bms.Protocols.Configuration;
using Bms.Protocols.Protocols;
using Bms.Protocols.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bms.Ingestion.Workers;

/// <summary>
/// Subscribes to every device's telemetry/heartbeat/status/events/
/// commands-ack topics on one broker connection (see
/// docs/mqtt/topic-design.md wildcards) and routes each message type to
/// the right handling — only "telemetry" runs the full decode/map/
/// normalize pipeline; the others are lighter-weight state updates.
///
/// Reliability boundary (docs/mqtt/reliability.md): a message is
/// acknowledged to the broker only once it is durably recorded in
/// PostgreSQL. A transient failure (PostgreSQL unreachable) is retried here
/// with backoff — holding the message, and, through the broker's receive
/// window, the ones behind it — until it succeeds, the connection drops
/// (the broker then redelivers it from the persistent session), or the host
/// stops. A failure that is about the message itself is logged and
/// acknowledged, never retried forever, so it can't block other devices.
/// </summary>
public sealed class MqttIngestionWorker : BackgroundService
{
    private readonly MqttTransportAdapter _mqtt;
    private readonly MqttOptions _options;
    private readonly IngestionReliabilityOptions _reliability;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITransientFailureClassifier _transientFailures;
    private readonly IngestionHealthState _health;
    private readonly ILogger<MqttIngestionWorker> _logger;
    private CancellationToken _stoppingToken;

    public MqttIngestionWorker(
        MqttTransportAdapter mqtt,
        IOptions<MqttOptions> options,
        IOptions<IngestionReliabilityOptions> reliability,
        IServiceScopeFactory scopeFactory,
        ITransientFailureClassifier transientFailures,
        IngestionHealthState health,
        ILogger<MqttIngestionWorker> logger)
    {
        _mqtt = mqtt;
        _options = options.Value;
        _reliability = reliability.Value;
        _scopeFactory = scopeFactory;
        _transientFailures = transientFailures;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        _mqtt.MessageReceived += OnMessageAsync;
        _mqtt.Connected += OnConnectedAsync;

        if (_options.SessionExpirySeconds == 0)
            _logger.LogWarning(
                "Mqtt:SessionExpirySeconds is 0 — the broker will NOT queue messages while this service is disconnected; " +
                "telemetry published during any ingestion outage will be lost");

        try
        {
            await _mqtt.ConnectAsync(stoppingToken);
            await _mqtt.SubscribeAsync(_options.TelemetryTopicFilter, stoppingToken);
            await _mqtt.SubscribeAsync(_options.HeartbeatTopicFilter, stoppingToken);
            await _mqtt.SubscribeAsync(_options.CommandAckTopicFilter, stoppingToken);
            await _mqtt.SubscribeAsync("protec/gateway/+/measurement", stoppingToken);
            await _mqtt.SubscribeAsync("protec/gateway/+/health", stoppingToken);
            await _mqtt.SubscribeAsync("protec/gateway/+/status", stoppingToken);
            await _mqtt.SubscribeAsync("protec/gateway/+/ack", stoppingToken);

            var customTopics = new HashSet<string>(StringComparer.Ordinal);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
                    foreach (var address in await devices.ListMqttTopicsAsync(stoppingToken))
                    {
                        if (customTopics.Contains(address)) continue;
                        await _mqtt.SubscribeAsync(address, stoppingToken);
                        customTopics.Add(address);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not refresh registered MQTT topics; retrying in 30 seconds");
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            _mqtt.MessageReceived -= OnMessageAsync;
            _mqtt.Connected -= OnConnectedAsync;
            await _mqtt.DisconnectAsync(CancellationToken.None);
        }
    }

    private Task OnConnectedAsync(bool sessionPresent, CancellationToken ct)
    {
        IngestionMetrics.BrokerReconnects.Add(1);
        _health.MarkReconnected();
        return Task.CompletedTask;
    }

    /// <param name="connectionToken">Cancelled when the broker connection this message arrived on drops.</param>
    private async Task OnMessageAsync(TransportMessage message, CancellationToken connectionToken)
    {
        IngestionMetrics.MessagesReceived.Add(1);
        if (message.IsRedelivery)
            IngestionMetrics.MessagesRedelivered.Add(1);

        MqttTopic.TryParse(message.Address, out var topic);

        using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(connectionToken, _stoppingToken);
        var delay = TimeSpan.FromMilliseconds(Math.Max(50, _reliability.TransientRetryInitialDelayMs));
        var maxDelay = TimeSpan.FromSeconds(Math.Max(1, _reliability.TransientRetryMaxDelaySeconds));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gatewayCode = ExtractProtecGatewayCode(message.Address);
                if (gatewayCode is not null)
                {
                    var gatewayRepository = scope.ServiceProvider.GetRequiredService<IGatewayRepository>();
                    await gatewayRepository.TouchAsync(gatewayCode, DateTimeOffset.UtcNow, message.Address.EndsWith("/measurement", StringComparison.OrdinalIgnoreCase), _stoppingToken);
                }
                // Processing runs on the host's stopping token, not the
                // connection's: a broker blip mid-insert must not abort a
                // write that may already have committed.
                await DispatchAsync(scope.ServiceProvider, topic, message, _stoppingToken);
                _health.MarkHealthy();
                IngestionMetrics.MessagesAcknowledged.Add(1);
                if (attempt > 1)
                    _logger.LogInformation("MQTT message on {Topic} processed after {Attempts} attempts", message.Address, attempt);
                return;
            }
            catch (Exception ex) when (_transientFailures.IsTransient(ex) && !_stoppingToken.IsCancellationRequested)
            {
                IngestionMetrics.TransientFailures.Add(1);
                _health.MarkDegraded($"{ex.GetType().Name}: {ex.Message}");

                if (connectionToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "MQTT message on {Topic} still failing when the broker connection dropped; " +
                                           "left unacknowledged for redelivery", message.Address);
                    throw;
                }

                _logger.LogWarning(ex,
                    "Transient failure processing MQTT message on {Topic} (attempt {Attempt}); NOT acknowledged, retrying in {Delay}ms",
                    message.Address, attempt, delay.TotalMilliseconds);

                // Throws if the connection drops or the host stops while
                // waiting — the message then stays unacknowledged.
                await Task.Delay(delay, retryCts.Token);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
            catch (Exception ex) when (!_stoppingToken.IsCancellationRequested)
            {
                // Nothing about retrying would make this message succeed (bad
                // JSON on an events/ack topic, an unexpected bug), so it is
                // acknowledged rather than allowed to block everything behind it.
                IngestionMetrics.PermanentFailures.Add(1);
                _logger.LogError(ex, "Permanent failure handling MQTT message on {Topic}; acknowledging and skipping it. Payload (first 512 bytes): {Payload}",
                    message.Address, PayloadPreview(message.Payload));
                return;
            }
        }
    }

    private static string? ExtractProtecGatewayCode(string address)
    {
        var parts = address.Split('/');
        return parts.Length == 4 && parts[0] == "protec" && parts[1] == "gateway" ? parts[2] : null;
    }

    private async Task DispatchAsync(IServiceProvider services, MqttTopic? topic, TransportMessage message, CancellationToken ct)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        if (await devices.GetByMqttTopicAsync(message.Address, ct) is not null)
        {
            await HandleTelemetryAsync(services, message, ct);
            return;
        }
        if (topic is null)
        {
            _logger.LogWarning("Ignoring MQTT topic with no registered device: {Topic}", message.Address);
            return;
        }
        switch (topic.MessageType)
        {
            case "telemetry":
                await HandleTelemetryAsync(services, message, ct);
                break;
            case "ack":
                _logger.LogInformation("Command acknowledgement from gateway {Gateway}: {Payload}", topic.DeviceCode,
                    PayloadPreview(message.Payload));
                await services.GetRequiredService<IRealtimeNotifier>().GatewayMessageReceivedAsync(
                    new GatewayMessageUpdate(topic.DeviceCode, message.Address, DateTimeOffset.UtcNow, PayloadPreview(message.Payload)), ct);
                break;
            case "heartbeat":
                await HandleHeartbeatAsync(services, topic, ct);
                break;
            case "events":
                await HandleEventAsync(services, topic, message, ct);
                break;
            case "commands/ack":
            await HandleCommandAckAsync(services, message, topic.DeviceCode, ct);
                break;
            case "status":
                await HandleGatewayStatusAsync(services, topic.DeviceCode, message, ct);
                break;
            default:
                _logger.LogWarning("Unhandled MQTT message type '{MessageType}' on topic {Topic}", topic.MessageType, message.Address);
                break;
        }
    }

    private static string PayloadPreview(byte[] payload) =>
        System.Text.Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 512));

    private static async Task HandleGatewayStatusAsync(IServiceProvider services, string gatewayCode, TransportMessage message, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;
        string? firmware = null;
        if (root.TryGetProperty("firmware", out var fw) && fw.ValueKind == JsonValueKind.Object)
            firmware = fw.EnumerateObject().Select(p => p.Value.GetString()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        string? ip = null;
        int? rssi = null;
        if (root.TryGetProperty("network", out var network) && network.ValueKind == JsonValueKind.Object)
        {
            if (network.TryGetProperty("ip", out var ipValue)) ip = ipValue.GetString();
            if (network.TryGetProperty("rssi_dbm", out var rssiValue) && rssiValue.TryGetInt32(out var parsedRssi)) rssi = parsedRssi;
        }
        await services.GetRequiredService<IGatewayRepository>().UpdateHealthAsync(gatewayCode, ip, firmware, rssi, ct);
        await services.GetRequiredService<IRealtimeNotifier>().GatewayMessageReceivedAsync(
            new GatewayMessageUpdate(gatewayCode, message.Address, DateTimeOffset.UtcNow, PayloadPreview(message.Payload)), ct);
    }

    private static async Task HandleTelemetryAsync(IServiceProvider services, TransportMessage message, CancellationToken ct)
    {
        var handler = services.GetServices<IProtocolHandler>().First(h => h.Protocol == ProtocolType.Mqtt);
        var pipeline = services.GetRequiredService<IIngestionPipeline>();

        var rawMessage = await handler.TryBuildRawMessageAsync(message, ct);
        if (rawMessage is null)
            return; // unknown device / bad topic: logged by the handler, nothing to retry

        // Acknowledge the moment the raw message is safely in PostgreSQL —
        // before InfluxDB, calculations, and alerts, none of which may hold
        // the broker's copy hostage.
        await pipeline.ProcessAsync(rawMessage, message.AcknowledgeAsync, ct);
    }

    private static async Task HandleHeartbeatAsync(IServiceProvider services, MqttTopic topic, CancellationToken ct)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        var device = await devices.GetByCodesAsync(topic.TenantSlug, topic.SiteCode, topic.DeviceCode, ct);
        if (device is null)
            return;

        var lifecycle = services.GetRequiredService<IDeviceLifecycleService>();
        await lifecycle.RecordHeartbeatAsync(device, DateTimeOffset.UtcNow, ct);
    }

    private async Task HandleEventAsync(IServiceProvider services, MqttTopic topic, TransportMessage message, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("eventType", out var eventTypeProp) || eventTypeProp.GetString() != "fota")
        {
            _logger.LogInformation("Unrecognized event from {Device}: {Payload}", topic.DeviceCode, root.ToString());
            return;
        }

        var deploymentId = root.GetProperty("deploymentId").GetGuid();
        var statusText = root.GetProperty("status").GetString() ?? "FAILED";
        var reportedFirmwareVersion = root.TryGetProperty("firmwareVersion", out var v) ? v.GetString() : null;
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;

        var deployments = services.GetRequiredService<IFirmwareDeploymentRepository>();
        var deployment = await deployments.GetByIdAsync(deploymentId, ct);
        if (deployment is null)
        {
            _logger.LogWarning("FOTA event for unknown deployment {DeploymentId}", deploymentId);
            return;
        }

        if (!Enum.TryParse<FirmwareDeploymentStatus>(statusText, ignoreCase: true, out var status))
        {
            _logger.LogWarning("Unknown FOTA status '{Status}' for deployment {DeploymentId}", statusText, deploymentId);
            return;
        }

        deployment.Status = status;
        deployment.Error = error;
        if (status is FirmwareDeploymentStatus.Success or FirmwareDeploymentStatus.Failed or FirmwareDeploymentStatus.Timeout)
            deployment.CompletedAt = DateTimeOffset.UtcNow;
        await deployments.UpdateAsync(deployment, ct);

        if (status == FirmwareDeploymentStatus.Success && reportedFirmwareVersion is not null)
        {
            var devices = services.GetRequiredService<IDeviceRepository>();
            var device = await devices.GetByIdAsync(deployment.DeviceId, ct);
            if (device is not null)
            {
                device.FirmwareVersion = reportedFirmwareVersion;
                await devices.UpdateAsync(device, ct);
            }
        }

        var notifier = services.GetRequiredService<IRealtimeNotifier>();
        await notifier.FirmwareDeploymentUpdatedAsync(deployment, ct);
    }

    private static async Task HandleCommandAckAsync(IServiceProvider services, TransportMessage message, string gatewayCode, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;
        var raw = PayloadPreview(message.Payload);
        if (!root.TryGetProperty("commandId", out var commandIdProp) || !commandIdProp.TryGetGuid(out var commandId))
            return;
        var statusText = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "FAILED" : "FAILED";

        var commands = services.GetRequiredService<IDeviceCommandRepository>();
        var command = await commands.GetByIdAsync(commandId, ct);
        if (command is null)
            return;

        command.Status = Enum.TryParse<CommandStatus>(statusText, ignoreCase: true, out var status) ? status : CommandStatus.Failed;
        command.CompletedAt = DateTimeOffset.UtcNow;
        await commands.UpdateAsync(command, ct);

        var notifier = services.GetRequiredService<IRealtimeNotifier>();
        await notifier.CommandStatusChangedAsync(command, ct);
    }
}
