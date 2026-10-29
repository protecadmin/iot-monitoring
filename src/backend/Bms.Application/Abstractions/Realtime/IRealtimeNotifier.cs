using Bms.Application.Abstractions.Calculations;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;

namespace Bms.Application.Abstractions.Realtime;

/// <summary>
/// Application-layer abstraction over SignalR — Application must not
/// reference SignalR directly (see dependency rule). Bms.Api implements
/// this against a SignalR hub context.
/// </summary>
public interface IRealtimeNotifier
{
    Task TelemetryUpdatedAsync(NormalizedTelemetry telemetry, CancellationToken ct);
    Task DeviceStatusChangedAsync(Device device, CancellationToken ct);
    Task AlertCreatedAsync(Alert alert, CancellationToken ct);
    Task AlertResolvedAsync(Alert alert, CancellationToken ct);
    Task CommandStatusChangedAsync(DeviceCommand command, CancellationToken ct);
    Task FirmwareDeploymentUpdatedAsync(FirmwareDeployment deployment, CancellationToken ct);
    Task CalculationUpdatedAsync(CalculationUpdate update, CancellationToken ct);
}
