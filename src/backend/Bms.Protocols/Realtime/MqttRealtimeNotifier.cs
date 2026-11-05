using System.Text.Json;
using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Realtime;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Bms.Protocols.Transports;
using Microsoft.Extensions.Logging;

namespace Bms.Protocols.Realtime;

/// <summary>
/// The Ingestion worker runs in its own process/container, with no access
/// to Bms.Api's in-memory SignalR hub. Rather than adding a dedicated
/// pub/sub backplane (Redis etc.) purely to bridge two processes, this
/// publishes real-time events to a small internal MQTT topic
/// ("bms/realtime/events") on the broker the platform already runs —
/// Bms.Api's RealtimeBridgeService subscribes to it and re-broadcasts over
/// SignalR. See docs/architecture/system-architecture.md for why this
/// doesn't warrant new infrastructure. Best-effort (QoS 0): losing an
/// occasional UI-freshness ping is acceptable — Postgres/InfluxDB, not
/// this channel, are the source of truth.
/// </summary>
public sealed class MqttRealtimeNotifier : IRealtimeNotifier
{
    public const string BridgeTopic = "bms/realtime/events";

    // The frontend's TypeScript types (and Bms.Api's own SignalR JSON
    // protocol, configured for camelCase in Program.cs) expect camelCase
    // property names. This envelope is built with plain
    // JsonSerializer.SerializeToUtf8Bytes, which defaults to PascalCase —
    // and since RealtimeBridgeService relays the resulting JsonElement
    // verbatim (re-serializing an already-parsed JsonElement does not
    // rename its keys), that camelCase SignalR setting never gets a
    // chance to apply to events that originate here. Confirmed: telemetry
    // pushed from this (Ingestion) process rendered with every field
    // undefined on the dashboard until this was set explicitly.
    private static readonly JsonSerializerOptions EnvelopeOptions = new(JsonSerializerDefaults.Web);

    private readonly MqttTransportAdapter _mqtt;
    private readonly ILogger<MqttRealtimeNotifier> _logger;

    public MqttRealtimeNotifier(MqttTransportAdapter mqtt, ILogger<MqttRealtimeNotifier> logger)
    {
        _mqtt = mqtt;
        _logger = logger;
    }

    public Task TelemetryUpdatedAsync(NormalizedTelemetry telemetry, CancellationToken ct) =>
        PublishAsync(telemetry.TenantId, "TelemetryUpdated", new
        {
            telemetry.DeviceId,
            telemetry.SiteId,
            telemetry.Timestamp,
            telemetry.Fields
        }, ct);

    public Task DeviceStatusChangedAsync(Device device, CancellationToken ct) =>
        PublishAsync(device.TenantId, "DeviceStatusChanged", new
        {
            device.Id,
            device.SiteId,
            Status = device.Status.ToString(),
            device.LastSeenAt,
            device.LastHeartbeatAt,
            device.LastTelemetryAt
        }, ct);

    public Task AlertCreatedAsync(Alert alert, CancellationToken ct) => PublishAsync(alert.TenantId, "AlertCreated", MapAlert(alert), ct);
    public Task AlertResolvedAsync(Alert alert, CancellationToken ct) => PublishAsync(alert.TenantId, "AlertResolved", MapAlert(alert), ct);

    public Task CommandStatusChangedAsync(DeviceCommand command, CancellationToken ct) =>
        PublishAsync(command.TenantId, "CommandStatusChanged", new
        {
            command.Id,
            command.DeviceId,
            Type = command.Type.ToString(),
            Status = command.Status.ToString(),
            command.Error
        }, ct);

    public Task FirmwareDeploymentUpdatedAsync(FirmwareDeployment deployment, CancellationToken ct) =>
        PublishAsync(deployment.TenantId, "FirmwareDeploymentUpdated", new
        {
            deployment.Id,
            deployment.DeviceId,
            deployment.FirmwareId,
            Status = deployment.Status.ToString(),
            deployment.AttemptCount,
            deployment.Error,
            deployment.StartedAt,
            deployment.CompletedAt,
            deployment.CreatedAt
        }, ct);

    public Task CalculationUpdatedAsync(CalculationUpdate update, CancellationToken ct) =>
        PublishAsync(update.TenantId, "CalculationUpdated", new
        {
            update.DeviceId,
            update.SiteId,
            update.RuleId,
            update.RuleKey,
            update.OutputKey,
            update.OutputName,
            update.OutputUnit,
            update.Value,
            update.Timestamp
        }, ct);

    public Task GatewayMessageReceivedAsync(GatewayMessageUpdate update, CancellationToken ct) =>
        PublishGlobalAsync("GatewayMessageReceived", new
        {
            update.GatewayCode,
            update.Topic,
            update.ReceivedAt,
            update.Payload
        }, ct);

    private async Task PublishGlobalAsync(string eventName, object payload, CancellationToken ct)
    {
        if (!_mqtt.IsConnected) return;
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { tenantId = Guid.Empty, eventName, payload }, EnvelopeOptions);
        try { await _mqtt.SendAsync(BridgeTopic, envelope, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to publish global real-time bridge event {Event}", eventName); }
    }

    private static object MapAlert(Alert alert) => new
    {
        alert.Id,
        alert.DeviceId,
        Severity = alert.Severity.ToString(),
        Status = alert.Status.ToString(),
        alert.Parameter,
        alert.CurrentValue,
        alert.Threshold,
        alert.Message,
        alert.TriggeredAt
    };

    private async Task PublishAsync(Guid tenantId, string eventName, object payload, CancellationToken ct)
    {
        if (!_mqtt.IsConnected)
        {
            _logger.LogDebug("Skipping real-time bridge publish for {Event}: MQTT not connected", eventName);
            return;
        }

        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { tenantId, eventName, payload }, EnvelopeOptions);
        try
        {
            await _mqtt.SendAsync(BridgeTopic, envelope, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish real-time bridge event {Event}", eventName);
        }
    }
}
