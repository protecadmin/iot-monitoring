using Bms.Domain;

namespace Bms.Api.Dtos;

public sealed record CreateCommandRequest(CommandType Type, string? PayloadJson);

public sealed record DeviceCommandDto(
    Guid Id, Guid DeviceId, string Type, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? CompletedAt, string? Error);

public sealed record DeviceConfigurationDto(
    Guid DeviceId, int Version, int ReportingIntervalSeconds, int HeartbeatIntervalSeconds,
    double? TemperatureThreshold, DateTimeOffset? AcknowledgedAt);

public sealed record UpdateDeviceConfigurationRequest(
    int ReportingIntervalSeconds, int HeartbeatIntervalSeconds, double? TemperatureThreshold);
