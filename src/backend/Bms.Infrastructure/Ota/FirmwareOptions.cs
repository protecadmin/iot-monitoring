namespace Bms.Infrastructure.Ota;

public sealed class FirmwareOptions
{
    public const string SectionName = "Firmware";

    /// <summary>Folder the binaries are stored in. A relative path is resolved against the API's content root.</summary>
    public string StoragePath { get; set; } = "data/firmware";

    public int MaxFileSizeMb { get; set; } = 64;

    /// <summary>
    /// The address devices use to reach this API — it goes into the download
    /// link inside the MQTT message, so it must be reachable from the devices'
    /// network, not just from this machine.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>How long a download link works after the update is sent. A retry sends a fresh one.</summary>
    public int DownloadLinkLifetimeMinutes { get; set; } = 60;

    /// <summary>How long a deployment to an Offline device waits for it to come back before giving up.</summary>
    public int WaitForDeviceHours { get; set; } = 72;

    /// <summary>How long a sent update may take to report Success/Failed before it is marked Timeout.</summary>
    public int CompletionTimeoutMinutes { get; set; } = 10;
}
