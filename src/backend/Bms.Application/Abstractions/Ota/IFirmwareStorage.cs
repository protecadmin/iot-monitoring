namespace Bms.Application.Abstractions.Ota;

/// <summary>
/// Where firmware binaries live. One file per firmware release, keyed by the
/// release's id — never by the uploaded name, so a file name can't be used to
/// reach anything outside the store.
/// </summary>
public interface IFirmwareStorage
{
    /// <summary>
    /// Streams the upload to the store, computing its SHA-256 and size on the
    /// way through — the checksum a device verifies is the one of the bytes
    /// actually stored, never a value the uploader claims.
    /// </summary>
    Task<StoredFirmwareFile> SaveAsync(Guid firmwareId, Stream content, long maxBytes, CancellationToken ct);

    bool Exists(Guid firmwareId);

    /// <summary>Null when no file is stored for this release.</summary>
    Stream? OpenRead(Guid firmwareId);

    void Delete(Guid firmwareId);
}

public sealed record StoredFirmwareFile(string Sha256Checksum, long SizeBytes);

/// <summary>Thrown when an upload exceeds the configured size limit; nothing is left in the store.</summary>
public sealed class FirmwareTooLargeException(long maxBytes)
    : Exception($"Firmware file is larger than the {maxBytes / (1024 * 1024)} MB limit")
{
    public long MaxBytes { get; } = maxBytes;
}
