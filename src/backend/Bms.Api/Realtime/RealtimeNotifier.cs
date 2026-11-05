using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Realtime;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.AspNetCore.SignalR;

namespace Bms.Api.Realtime;

/// <summary>
/// Bms.Api's implementation of the Application-layer real-time port —
/// Application must not reference SignalR directly, so this class is the
/// only place that translates domain events into hub broadcasts. Each
/// event is sent to both the device's own tenant group and the
/// SuperAdmin "all-tenants" group.
/// </summary>
public sealed class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<TelemetryHub> _hub;

    public RealtimeNotifier(IHubContext<TelemetryHub> hub)
    {
        _hub = hub;
    }

    public Task TelemetryUpdatedAsync(NormalizedTelemetry telemetry, CancellationToken ct) =>
        BroadcastAsync(telemetry.TenantId, "TelemetryUpdated", new
        {
            telemetry.DeviceId,
            telemetry.SiteId,
            telemetry.Timestamp,
            telemetry.Fields
        }, ct);

    public Task DeviceStatusChangedAsync(Device device, CancellationToken ct) =>
        BroadcastAsync(device.TenantId, "DeviceStatusChanged", new
        {
            device.Id,
            device.SiteId,
            Status = device.Status.ToString(),
            device.LastSeenAt,
            device.LastHeartbeatAt,
            device.LastTelemetryAt
        }, ct);

    public Task AlertCreatedAsync(Alert alert, CancellationToken ct) =>
        BroadcastAsync(alert.TenantId, "AlertCreated", MapAlert(alert), ct);

    public Task AlertResolvedAsync(Alert alert, CancellationToken ct) =>
        BroadcastAsync(alert.TenantId, "AlertResolved", MapAlert(alert), ct);

    public Task CommandStatusChangedAsync(DeviceCommand command, CancellationToken ct) =>
        BroadcastAsync(command.TenantId, "CommandStatusChanged", new
        {
            command.Id,
            command.DeviceId,
            Type = command.Type.ToString(),
            Status = command.Status.ToString(),
            command.Error
        }, ct);

    public Task FirmwareDeploymentUpdatedAsync(FirmwareDeployment deployment, CancellationToken ct) =>
        BroadcastAsync(deployment.TenantId, "FirmwareDeploymentUpdated", new
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
        BroadcastAsync(update.TenantId, "CalculationUpdated", new
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
        _hub.Clients.Group(RealtimeGroups.AllTenants).SendAsync("GatewayMessageReceived", update, ct);

    public Task HubMessageReceivedAsync(HubMessageUpdate update, CancellationToken ct) =>
        _hub.Clients.Group(RealtimeGroups.AllTenants).SendAsync("HubMessageReceived", update, ct);

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

    private async Task BroadcastAsync(Guid tenantId, string eventName, object payload, CancellationToken ct)
    {
        await _hub.Clients.Group(RealtimeGroups.Tenant(tenantId)).SendAsync(eventName, payload, ct);
        await _hub.Clients.Group(RealtimeGroups.AllTenants).SendAsync(eventName, payload, ct);
    }
}
