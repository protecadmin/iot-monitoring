using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BmsDbContext : DbContext
{
    public BmsDbContext(DbContextOptions<BmsDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceType> DeviceTypes => Set<DeviceType>();
    public DbSet<TiltMonitoringSettings> TiltMonitoringSettings => Set<TiltMonitoringSettings>();
    public DbSet<TiltDailyReading> TiltDailyReadings => Set<TiltDailyReading>();
    public DbSet<DeviceBaseline> DeviceBaselines => Set<DeviceBaseline>();
    public DbSet<Bms.Domain.Connectors.Connector> Connectors => Set<Bms.Domain.Connectors.Connector>();
    public DbSet<Bms.Domain.Connectors.ConnectorUnknownDevice> ConnectorUnknownDevices => Set<Bms.Domain.Connectors.ConnectorUnknownDevice>();
    public DbSet<Bms.Domain.Connectors.ConnectorUnmappedKey> ConnectorUnmappedKeys => Set<Bms.Domain.Connectors.ConnectorUnmappedKey>();
    public DbSet<Bms.Domain.Connectors.ConnectorServiceStatus> ConnectorServiceStatuses => Set<Bms.Domain.Connectors.ConnectorServiceStatus>();
    public DbSet<Bms.Domain.Connectors.ConnectorCapture> ConnectorCaptures => Set<Bms.Domain.Connectors.ConnectorCapture>();
    public DbSet<DeviceTypeParameter> DeviceTypeParameters => Set<DeviceTypeParameter>();
    public DbSet<Gateway> Gateways => Set<Gateway>();
    public DbSet<Hub> Hubs => Set<Hub>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<GlobalAlertRule> GlobalAlertRules => Set<GlobalAlertRule>();
    public DbSet<AlertRuleOverride> AlertRuleOverrides => Set<AlertRuleOverride>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();
    public DbSet<DeviceConfiguration> DeviceConfigurations => Set<DeviceConfiguration>();
    public DbSet<Firmware> Firmwares => Set<Firmware>();
    public DbSet<FirmwareDeployment> FirmwareDeployments => Set<FirmwareDeployment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RawDeviceMessage> RawDeviceMessages => Set<RawDeviceMessage>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MqttCredential> MqttCredentials => Set<MqttCredential>();
    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();
    public DbSet<TelegramSettings> TelegramSettings => Set<TelegramSettings>();
    public DbSet<CalculationRule> CalculationRules => Set<CalculationRule>();
    public DbSet<CalculationState> CalculationStates => Set<CalculationState>();
    public DbSet<CounterReset> CounterResets => Set<CounterReset>();
    public DbSet<DeviceCalculationRule> DeviceCalculationRules => Set<DeviceCalculationRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(t => t.Sites).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.Property(x => x.AlertsEnabled).HasDefaultValue(true);
            e.Property(x => x.TimeSource).HasConversion<string>().HasMaxLength(16).HasDefaultValue(TimeSource.Device);
            e.OwnsMany(x => x.ValueConversions, c => c.ToJson());
            e.Property(x => x.Connection).HasConversion<string>().HasMaxLength(16).HasDefaultValue(DeviceConnection.MqttDirect);
            e.Property(x => x.SensorId).HasMaxLength(64);
            e.Property(x => x.MountingLocation).HasMaxLength(256);
            // One device per sensor ID on a connector — the connector looks devices up by it.
            e.HasIndex(x => new { x.ConnectorId, x.SensorId }).IsUnique().HasFilter("\"ConnectorId\" IS NOT NULL");
            e.HasOne<Bms.Domain.Connectors.Connector>().WithMany().HasForeignKey(x => x.ConnectorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DeviceCode).IsUnique();
            e.Property(x => x.MqttTelemetryTopic).HasMaxLength(512);
            e.HasIndex(x => x.MqttTelemetryTopic).IsUnique();
            e.Property(x => x.TwinTemplate).HasMaxLength(40);
            e.Property(x => x.TwinMountPoint).HasMaxLength(40);
            e.Property(x => x.TwinAssetName).HasMaxLength(80);
            e.Property(x => x.Transport).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PayloadFormat).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Site).WithMany(s => s.Devices).HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeviceType).WithMany().HasForeignKey(x => x.DeviceTypeId).OnDelete(DeleteBehavior.Restrict);
            // SetNull, not Restrict/Cascade — deleting a gateway shouldn't be
            // blocked by devices pointing at it, and shouldn't take the
            // devices down with it; they just become unassigned.
            e.HasOne(x => x.Gateway).WithMany(g => g.Devices).HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Bms.Domain.Connectors.Connector>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Vendor).HasMaxLength(128);
            e.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Framing).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Format).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.BinaryChecksum).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.DeviceCodePrefix).HasMaxLength(24);
            e.Property(x => x.EndCharacter).HasMaxLength(4);
            e.Property(x => x.StartMarker).HasMaxLength(64);
            e.Property(x => x.CsvSeparator).HasMaxLength(4);
            e.Property(x => x.PairSeparator).HasMaxLength(4);
            e.Property(x => x.KeyValueSeparator).HasMaxLength(4);
            e.Property(x => x.BinaryHeaderHex).HasMaxLength(64);
            // Two connectors can't listen on one port.
            e.HasIndex(x => new { x.Protocol, x.Port }).IsUnique();
            e.HasIndex(x => x.DeviceCodePrefix).IsUnique();
            // The mapping is part of the connector — stored with it, as JSON.
            e.OwnsMany(x => x.Mappings, m =>
            {
                m.ToJson();
                m.Property(x => x.Target).HasConversion<string>();
            });
        });

        modelBuilder.Entity<Bms.Domain.Connectors.ConnectorUnknownDevice>(e =>
        {
            e.Property(x => x.SensorId).HasMaxLength(64);
            e.Property(x => x.LastSample).HasMaxLength(512);
            e.HasIndex(x => new { x.ConnectorId, x.SensorId }).IsUnique();
            e.HasOne<Bms.Domain.Connectors.Connector>().WithMany().HasForeignKey(x => x.ConnectorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bms.Domain.Connectors.ConnectorUnmappedKey>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(128);
            e.Property(x => x.LastValue).HasMaxLength(128);
            e.HasIndex(x => new { x.ConnectorId, x.Source }).IsUnique();
            e.HasOne<Bms.Domain.Connectors.Connector>().WithMany().HasForeignKey(x => x.ConnectorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bms.Domain.Connectors.ConnectorServiceStatus>(e =>
        {
            e.Property(x => x.Version).HasMaxLength(32);
            e.Property(x => x.LastError).HasMaxLength(1024);
        });

        modelBuilder.Entity<Bms.Domain.Connectors.ConnectorCapture>(e =>
        {
            e.Property(x => x.Sample).HasMaxLength(8192);
            e.HasIndex(x => new { x.ConnectorId, x.CapturedAt });
            e.HasOne<Bms.Domain.Connectors.Connector>().WithMany().HasForeignKey(x => x.ConnectorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceBaseline>(e =>
        {
            e.Property(x => x.ParameterKey).HasMaxLength(64);
            e.HasIndex(x => new { x.DeviceId, x.ParameterKey }).IsUnique();
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        // Existing types keep accepting commands; only a type marked otherwise refuses them.
        modelBuilder.Entity<DeviceType>(e => {
            e.Property(x => x.SupportsCommands).HasDefaultValue(true);
            e.OwnsOne(x => x.TiltAxes, a => a.ToJson());
        });
        modelBuilder.Entity<TiltMonitoringSettings>(e => {
            e.HasIndex(x => x.DeviceId).IsUnique();
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
        });
        modelBuilder.Entity<TiltDailyReading>(e => {
            e.HasIndex(x => new { x.DeviceId, x.Date }).IsUnique();
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceTypeParameter>(e =>
        {
            e.HasIndex(x => new { x.DeviceTypeId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(64);
            // Existing parameters predate the notion of a kind; they were all
            // plain readings, so they become Instantaneous — nothing is
            // silently promoted to a Counter.
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ParameterKind.Instantaneous);
            e.Property(x => x.CalculationType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DashboardWidget).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CalculationExpression).HasMaxLength(256);
            e.Property(x => x.CalculationSourceKey).HasMaxLength(64);
            e.HasOne(x => x.DeviceType).WithMany(t => t.Parameters).HasForeignKey(x => x.DeviceTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Gateway>(e =>
        {
            e.HasIndex(x => x.GatewayCode).IsUnique();
            e.Property(x => x.GatewayCode).HasMaxLength(128).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Hub>(e =>
        {
            e.HasIndex(x => x.HubId).IsUnique();
            e.Property(x => x.HubId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.SecurityMode).HasMaxLength(32);
            e.Property(x => x.DisplayMode).HasMaxLength(64);
            e.Property(x => x.HistoryRange).HasMaxLength(32);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertRule>(e =>
        {
            e.Property(x => x.Metric).HasMaxLength(64);
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<GlobalAlertRule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Metric).HasMaxLength(64);
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.AppliesByDefault).HasDefaultValue(true);
            e.HasIndex(x => x.DeviceTypeId);
        });

        modelBuilder.Entity<AlertRuleOverride>(e =>
        {
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.GlobalAlertRule).WithMany().HasForeignKey(x => x.GlobalAlertRuleId).OnDelete(DeleteBehavior.Cascade);
            // One change per rule at each level of a company: NULLS NOT
            // DISTINCT, so two company-level rows (both scopes null) collide.
            e.HasIndex(x => new { x.GlobalAlertRuleId, x.TenantId, x.SiteId, x.DeviceId }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.TenantId, x.GlobalAlertRuleId });
        });

        modelBuilder.Entity<Alert>(e =>
        {
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Device).WithMany(d => d.Alerts).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
            // No foreign key: AlertRuleId names either a company's rule or a
            // GlobalAlertRule (see Alert.AlertRuleId).
            e.HasIndex(x => new { x.DeviceId, x.AlertRuleId, x.TriggeredAt });
        });

        modelBuilder.Entity<DeviceCommand>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Device).WithMany(d => d.Commands).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceConfiguration>(e =>
        {
            e.HasIndex(x => x.DeviceId).IsUnique();
            e.HasOne(x => x.Device).WithOne(d => d.Configuration).HasForeignKey<DeviceConfiguration>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Firmware>(e =>
        {
            e.HasOne(x => x.DeviceType).WithMany().HasForeignKey(x => x.DeviceTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FirmwareDeployment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Firmware).WithMany(f => f.Deployments).HasForeignKey(x => x.FirmwareId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Device).WithMany(d => d.FirmwareDeployments).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RawDeviceMessage>(e =>
        {
            e.Property(x => x.Transport).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PayloadFormat).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ProcessingStatus).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.DeviceId, x.DeviceTimestamp });
            e.HasIndex(x => x.CorrelationId);

            // One Persisted row per device reading. Partial: Rejected rows
            // (including recorded duplicates) carry the same key on purpose.
            e.Property(x => x.DedupKey).HasMaxLength(64);
            e.HasIndex(x => new { x.DeviceId, x.DedupKey })
                .IsUnique()
                .HasDatabaseName("UX_RawDeviceMessages_DeviceId_DedupKey_Persisted")
                .HasFilter("\"ProcessingStatus\" = 'Persisted' AND \"DedupKey\" IS NOT NULL");
            e.HasIndex(x => new { x.DeviceId, x.SequenceNumber });

            // Small partial index: only the backlog the time-series backfill
            // scans. Named explicitly because retention needs its own index
            // on the same column, and EF keys indexes by property set.
            e.HasIndex(x => x.ReceivedAt, "IX_RawDeviceMessages_TimeSeriesPending")
                .HasFilter("\"ProcessingStatus\" = 'Persisted' AND \"TimeSeriesWrittenAt\" IS NULL");

            // What the retention sweep walks: oldest first, limited. Without
            // it the hourly sweep sequentially scans and sorts the largest
            // table in the database, competing with the insert on the
            // acknowledgement path. Deliberately unfiltered — a partial
            // index on the sweep's OR-predicate is not reliably matched by
            // the planner, and the rows it would exclude are a small
            // minority anyway.
            e.HasIndex(x => x.ReceivedAt, "IX_RawDeviceMessages_RetentionSweep");
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CalculationRule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SourceParameterKey).HasMaxLength(64);
            e.Property(x => x.Expression).HasMaxLength(256);
            // Existing rules predate selection and behaved automatically, so they keep doing so.
            e.Property(x => x.AssignmentMode).HasConversion<string>().HasMaxLength(16).HasDefaultValue(CalculationAssignmentMode.Automatic);
            e.Property(x => x.OutputKey).HasMaxLength(64);
            e.Property(x => x.OutputName).HasMaxLength(128);
            e.Property(x => x.OutputUnit).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsEnabled });
            // No foreign keys on SiteId/DeviceId/DeviceTypeId, matching AlertRule:
            // SetNull on delete would silently widen a device-scoped rule to the
            // whole tenant, and Cascade would delete configuration unasked.
        });

        modelBuilder.Entity<CounterReset>(e =>
        {
            e.Property(x => x.ParameterKey).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.HasIndex(x => new { x.DeviceId, x.ParameterKey, x.EffectiveAt });
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CalculationState>(e =>
        {
            e.HasIndex(x => new { x.CalculationRuleId, x.DeviceId }).IsUnique();
            e.HasIndex(x => x.DeviceId);
            e.HasOne(x => x.CalculationRule).WithMany().HasForeignKey(x => x.CalculationRuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceCalculationRule>(e =>
        {
            e.HasIndex(x => new { x.DeviceId, x.CalculationRuleId }).IsUnique();
            e.HasIndex(x => x.CalculationRuleId);
            e.HasOne(x => x.CalculationRule).WithMany().HasForeignKey(x => x.CalculationRuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MqttCredential>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(64);
            e.HasIndex(x => x.Username).IsUnique();
            // One active credential per tenant — rotating replaces this row's hash/encrypted values rather than adding a new one.
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
