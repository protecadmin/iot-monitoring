using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Application.Abstractions.Security;
using Bms.Domain;
using Bms.Domain.Entities;

namespace Bms.Application.Ota;

/// <summary>
/// The one way a firmware deployment is sent to its device — used for a new
/// deployment, a retry, and a deployment resumed when its device comes back
/// Online, so all three record the attempt the same way.
/// </summary>
public static class FirmwareDelivery
{
    public static async Task SendAsync(
        FirmwareDeployment deployment,
        Device device,
        Firmware firmware,
        IDeviceCommandDispatcher dispatcher,
        IFirmwareDeploymentRepository deployments,
        IRealtimeNotifier notifier,
        CancellationToken ct)
    {
        deployment.Status = FirmwareDeploymentStatus.Pending;
        // The completion timeout counts from here — the moment it is actually
        // sent, not when it was queued for an Offline device.
        deployment.StartedAt = DateTimeOffset.UtcNow;
        deployment.CompletedAt = null;
        deployment.Error = null;
        deployment.AttemptCount++;

        try
        {
            await dispatcher.SendFirmwareDeploymentAsync(device, firmware, deployment, ct);
        }
        catch (Exception ex)
        {
            deployment.Status = FirmwareDeploymentStatus.Failed;
            deployment.Error = $"Could not send to the device: {ex.Message}";
            deployment.CompletedAt = DateTimeOffset.UtcNow;
        }

        await deployments.UpdateAsync(deployment, ct);
        await notifier.FirmwareDeploymentUpdatedAsync(deployment, ct);
    }
}
