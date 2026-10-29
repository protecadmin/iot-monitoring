namespace Bms.Application.Abstractions.Ota;

/// <summary>
/// Signed, expiring download links for devices. A device has no BMS login, so
/// the link itself is the permission: it names one release and one
/// deployment, and stops working after its expiry.
/// </summary>
public interface IFirmwareDownloadLinks
{
    string Create(Guid firmwareId, Guid deploymentId);

    bool IsValid(Guid firmwareId, Guid deploymentId, long expiresUnixSeconds, string signature);
}
