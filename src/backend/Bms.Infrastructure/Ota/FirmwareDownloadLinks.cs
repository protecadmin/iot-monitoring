using System.Security.Cryptography;
using System.Text;
using Bms.Application.Abstractions.Ota;
using Bms.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Bms.Infrastructure.Ota;

/// <summary>
/// HMAC-signed links: {PublicBaseUrl}/api/firmware/{id}/download?deployment=…&amp;expires=…&amp;signature=….
/// The signing key is derived from the JWT secret with a fixed purpose label,
/// so there is no extra secret to configure and a link signature can never be
/// mistaken for (or replayed as) anything else signed with that secret.
/// </summary>
public sealed class FirmwareDownloadLinks(IOptions<FirmwareOptions> firmwareOptions, IOptions<JwtOptions> jwtOptions, TimeProvider clock)
    : IFirmwareDownloadLinks
{
    public string Create(Guid firmwareId, Guid deploymentId)
    {
        var options = firmwareOptions.Value;
        var expires = clock.GetUtcNow().AddMinutes(Math.Max(1, options.DownloadLinkLifetimeMinutes)).ToUnixTimeSeconds();
        var signature = Sign(firmwareId, deploymentId, expires);
        return $"{options.PublicBaseUrl.TrimEnd('/')}/api/firmware/{firmwareId}/download" +
               $"?deployment={deploymentId}&expires={expires}&signature={signature}";
    }

    public bool IsValid(Guid firmwareId, Guid deploymentId, long expiresUnixSeconds, string signature)
    {
        if (clock.GetUtcNow().ToUnixTimeSeconds() > expiresUnixSeconds)
            return false;

        var expected = Encoding.ASCII.GetBytes(Sign(firmwareId, deploymentId, expiresUnixSeconds));
        var actual = Encoding.ASCII.GetBytes(signature ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(Guid firmwareId, Guid deploymentId, long expires)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("bms-firmware-download:" + jwtOptions.Value.SecretKey));
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{firmwareId:N}.{deploymentId:N}.{expires}"));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
