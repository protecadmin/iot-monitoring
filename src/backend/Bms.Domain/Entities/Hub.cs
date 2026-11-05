using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class Hub : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? SiteId { get; set; }
    public string HubId { get; set; } = string.Empty; // 12-char identifier e.g. "41bfdcac4333"
    public string Name { get; set; } = string.Empty;
    public HubStatus Status { get; set; } = HubStatus.Standalone;
    public string? IpAddress { get; set; }
    public string? AppVersion { get; set; }
    public string? AgentVersion { get; set; }
    public string? SecurityMode { get; set; } // e.g. "tls"
    public int? Rssi { get; set; }
    public long? UptimeSeconds { get; set; }
    public int ConfigRevision { get; set; }
    public string? ConfigHash { get; set; }
    public string? DisplayMode { get; set; }
    public int? RotationSeconds { get; set; }
    public int? WelcomeSeconds { get; set; }
    public string? HistoryRange { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastMeasurementAt { get; set; }
    public DateTimeOffset? LastConfigSyncAt { get; set; }
    public string? DesiredConfigJson { get; set; }
    public string? ReportedConfigJson { get; set; }
    public string? LastStatusJson { get; set; }

    public Site? Site { get; set; }
    public Tenant? Tenant { get; set; }
}

public enum HubStatus
{
    Standalone,
    Online,
    Offline,
    Maintenance
}

