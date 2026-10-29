namespace Bms.Application.Abstractions.DeviceManagement;

/// <summary>
/// Whether "no message from this device lately" can be blamed on the device.
/// While the ingestion service is down, degraded, or still catching up on a
/// backlog, every device looks silent at once — that silence says nothing
/// about the devices, and must not flip them Offline or raise per-device
/// no-telemetry/offline alerts.
/// </summary>
public interface IIngestionHealthMonitor
{
    /// <returns>Null when device silence is trustworthy; otherwise the reason it isn't.</returns>
    string? DeviceSilenceUntrustedReason(DateTimeOffset now);
}
