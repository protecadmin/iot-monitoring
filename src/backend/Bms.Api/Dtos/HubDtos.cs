namespace Bms.Api.Dtos;

public sealed record HubDto(
    Guid Id,
    Guid? TenantId,
    string? TenantName,
    Guid? SiteId,
    string? SiteName,
    string HubId,
    string Name,
    string Status,
    string? IpAddress,
    string? AppVersion,
    string? AgentVersion,
    string? SecurityMode,
    int? Rssi,
    long? UptimeSeconds,
    int ConfigRevision,
    string? ConfigHash,
    string? DisplayMode,
    int? RotationSeconds,
    int? WelcomeSeconds,
    string? HistoryRange,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastMeasurementAt,
    DateTimeOffset? LastConfigSyncAt,
    string? DesiredConfigJson,
    string? ReportedConfigJson,
    string? LastStatusJson
);

public sealed record CreateHubRequest(
    string HubId,
    string? Name = null,
    Guid? SiteId = null,
    string? IpAddress = null,
    string? AppVersion = null,
    string? AgentVersion = null,
    string? SecurityMode = null
);

public sealed record UpdateHubRequest(
    string Name,
    Guid? SiteId = null,
    string? IpAddress = null,
    string? AppVersion = null,
    string? AgentVersion = null,
    string? SecurityMode = null
);

public sealed record HubCommandRequest(
    string Action,
    Dictionary<string, object?>? Arguments = null
);

public sealed record HubDesiredConfigRequest(
    string? DisplayMode = null,
    int? RotationSeconds = null,
    int? WelcomeSeconds = null,
    string? HistoryRange = null
);

