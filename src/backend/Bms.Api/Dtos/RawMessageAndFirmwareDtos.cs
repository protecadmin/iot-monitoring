namespace Bms.Api.Dtos;

public sealed record RawMessageDto(
    Guid Id, Guid DeviceId, string Transport, string Protocol, string? Topic, string? Endpoint,
    string PayloadFormat, string RawPayloadBase64, DateTimeOffset ReceivedAt, DateTimeOffset? DeviceTimestamp,
    Guid CorrelationId, string ProcessingStatus, string? ProcessingError);

public sealed record FirmwareDto(
    Guid Id, Guid DeviceTypeId, string Version, string FileName, string Sha256Checksum,
    long SizeBytes, string? ReleaseNotes, DateTimeOffset ReleaseDate,
    /// <summary>False for releases created before files were stored — those can't be deployed.</summary>
    bool HasFile);

/// <summary>multipart/form-data: the release details plus the binary itself.</summary>
public sealed class CreateFirmwareForm
{
    public Guid DeviceTypeId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }
}

public sealed record DeployFirmwareRequest(Guid FirmwareId, IReadOnlyList<Guid> DeviceIds);

public sealed record FirmwareDeploymentDto(
    Guid Id, Guid FirmwareId, Guid DeviceId, string Status, int AttemptCount, string? Error,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt);
