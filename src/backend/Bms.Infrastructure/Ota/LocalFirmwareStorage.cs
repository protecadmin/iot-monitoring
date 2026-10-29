using System.Security.Cryptography;
using Bms.Application.Abstractions.Ota;
using Microsoft.Extensions.Options;

namespace Bms.Infrastructure.Ota;

/// <summary>Firmware binaries on the API server's disk, one file per release: {StoragePath}/{firmwareId}.bin.</summary>
public sealed class LocalFirmwareStorage(IOptions<FirmwareOptions> options) : IFirmwareStorage
{
    private string Root => options.Value.StoragePath;

    private string PathFor(Guid firmwareId) => Path.Combine(Root, $"{firmwareId:N}.bin");

    public async Task<StoredFirmwareFile> SaveAsync(Guid firmwareId, Stream content, long maxBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var finalPath = PathFor(firmwareId);
        // Written under a temporary name and moved into place only once
        // complete, so a half-uploaded file is never served to a device.
        var tempPath = finalPath + ".uploading";

        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var target = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new FirmwareTooLargeException(maxBytes);
                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return new StoredFirmwareFile(Convert.ToHexStringLower(sha.GetHashAndReset()), total);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public bool Exists(Guid firmwareId) => File.Exists(PathFor(firmwareId));

    public Stream? OpenRead(Guid firmwareId)
    {
        var path = PathFor(firmwareId);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(Guid firmwareId)
    {
        var path = PathFor(firmwareId);
        if (File.Exists(path)) File.Delete(path);
    }
}
