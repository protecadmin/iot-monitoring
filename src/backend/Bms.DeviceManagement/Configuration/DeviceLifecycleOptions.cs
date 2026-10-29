namespace Bms.DeviceManagement.Configuration;

/// <summary>
/// Applied by Bms.Api, which hosts OfflineSweepWorker (see Bms.Api/Program.cs)
/// and also reports these values on the device Configuration guide. The
/// Ingestion worker's copy is not used by the sweep; keep the DeviceLifecycle
/// section identical in both appsettings files anyway.
/// </summary>
public sealed class DeviceLifecycleOptions
{
    public const string SectionName = "DeviceLifecycle";

    /// <summary>How often the offline sweep runs — a device goes Offline between OfflineAfterSeconds and OfflineAfterSeconds + this.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>No heartbeat/telemetry for this long flips ONLINE -> OFFLINE.</summary>
    public int OfflineAfterSeconds { get; set; } = 90;

    public int NoTelemetryAlertAfterSeconds { get; set; } = 120;
    public int DeviceOfflineAlertAfterSeconds { get; set; } = 300;

    /// <summary>A device still not heard from this long after it was added raises Never Connected (default 24 h).</summary>
    public int NeverConnectedAlertAfterSeconds { get; set; } = 86400;
}
