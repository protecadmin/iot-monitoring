using Bms.Application.Abstractions.Notifications;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.DeviceManagement;
using Bms.DeviceManagement.Configuration;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Bms.UnitTests.DeviceManagement;

public class DeviceLifecycleServiceTests
{
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IAlertRuleRepository> _alertRules = new();
    private readonly Mock<IAlertRepository> _alerts = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();
    private readonly Mock<IAlertEmailNotifier> _email = new();
    private readonly Mock<IAlertTelegramNotifier> _telegram = new();
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    private DeviceLifecycleService BuildService(DeviceLifecycleOptions? options = null) => new(
        _devices.Object, _alertRules.Object, _alerts.Object, _notifier.Object, _email.Object, _telegram.Object,
        Options.Create(options ?? new DeviceLifecycleOptions()), _clock, NullLogger<DeviceLifecycleService>.Instance);

    [Theory]
    [InlineData(DeviceStatus.Offline)]
    [InlineData(DeviceStatus.Registered)]
    [InlineData(DeviceStatus.Provisioned)]
    public async Task RecordTelemetryAsync_FromNonOnlineStatus_TransitionsToOnlineAndNotifies(DeviceStatus initialStatus)
    {
        var device = new Device { Status = initialStatus };
        var service = BuildService();

        await service.RecordTelemetryAsync(device, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(DeviceStatus.Online, device.Status);
        _notifier.Verify(n => n.DeviceStatusChangedAsync(device, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordTelemetryAsync_ClosesNoTelemetryAndOfflineAlerts_ButNotReadingAlerts()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Online };
        var noTelemetry = new Alert { Parameter = LifecycleMetrics.NoTelemetry, Status = AlertStatus.Open };
        var offline = new Alert { Parameter = LifecycleMetrics.DeviceOffline, Status = AlertStatus.Acknowledged };
        var highTemp = new Alert { Parameter = "temperature", Status = AlertStatus.Open };
        _alerts.Setup(a => a.ListUnresolvedAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([noTelemetry, offline, highTemp]);

        await BuildService().RecordTelemetryAsync(device, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(AlertStatus.Resolved, noTelemetry.Status);
        Assert.Equal(AlertStatus.Resolved, offline.Status);
        Assert.Equal(AlertStatus.Open, highTemp.Status);
        _notifier.Verify(n => n.AlertResolvedAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RecordHeartbeatAsync_ClosesOfflineAlert_ButNotNoTelemetry()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Online };
        var noTelemetry = new Alert { Parameter = LifecycleMetrics.NoTelemetry, Status = AlertStatus.Open };
        var offline = new Alert { Parameter = LifecycleMetrics.DeviceOffline, Status = AlertStatus.Open };
        _alerts.Setup(a => a.ListUnresolvedAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([noTelemetry, offline]);

        await BuildService().RecordHeartbeatAsync(device, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(AlertStatus.Resolved, offline.Status);
        Assert.Equal(AlertStatus.Open, noTelemetry.Status);
    }

    [Fact]
    public async Task RecordTelemetryAsync_AlreadyOnline_DoesNotRenotify()
    {
        var device = new Device { Status = DeviceStatus.Online };
        var service = BuildService();

        await service.RecordTelemetryAsync(device, _clock.GetUtcNow(), CancellationToken.None);

        _notifier.Verify(n => n.DeviceStatusChangedAsync(It.IsAny<Device>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_SilentBeyondThreshold_FlipsToOfflineAndNotifies()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastHeartbeatAt = _clock.GetUtcNow().AddSeconds(-200)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        _alertRules.Setup(r => r.GetApplicableRulesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(1, flipped);
        Assert.Equal(DeviceStatus.Offline, device.Status);
        _notifier.Verify(n => n.DeviceStatusChangedAsync(device, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_RecentHeartbeat_StaysOnline()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastHeartbeatAt = _clock.GetUtcNow().AddSeconds(-10)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        _alertRules.Setup(r => r.GetApplicableRulesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(0, flipped);
        Assert.Equal(DeviceStatus.Online, device.Status);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_StaleHeartbeatButRecentTelemetry_StaysOnline()
    {
        // A device that once sent heartbeats and now only sends telemetry is
        // still alive — the newest message of either kind decides.
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastHeartbeatAt = _clock.GetUtcNow().AddHours(-2),
            LastTelemetryAt = _clock.GetUtcNow().AddSeconds(-5)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        _alertRules.Setup(r => r.GetApplicableRulesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(0, flipped);
        Assert.Equal(DeviceStatus.Online, device.Status);
    }

    private AlertRule LifecycleRule(string metric, double threshold) => new()
    {
        Id = Guid.NewGuid(), Name = metric, Metric = metric, Operator = AlertRuleOperator.GreaterThan,
        Threshold = threshold, Severity = AlertSeverity.Warning, CooldownSeconds = 600, IsEnabled = true
    };

    private void GivenRules(params AlertRule[] rules) =>
        _alertRules.Setup(r => r.GetApplicableRulesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

    [Fact]
    public async Task SweepOfflineDevicesAsync_AddedLongAgoAndNeverHeardFrom_RaisesNeverConnected()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Registered, CreatedAt = _clock.GetUtcNow().AddHours(-25) };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.NeverConnected, 86400));

        await BuildService().SweepOfflineDevicesAsync(null, CancellationToken.None);

        _alerts.Verify(a => a.AddAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.NeverConnected && x.DeviceId == device.Id), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(DeviceStatus.Registered, device.Status);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_AddedRecently_NoNeverConnectedYet()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Registered, CreatedAt = _clock.GetUtcNow().AddHours(-2) };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.NeverConnected, 86400));

        await BuildService().SweepOfflineDevicesAsync(null, CancellationToken.None);

        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_StillOfflinePastThreshold_RaisesDeviceOffline()
    {
        // Flipped Offline at ~90 s on an earlier sweep; Device Offline's 300 s
        // threshold is only reached on a later one.
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Offline, LastSeenAt = _clock.GetUtcNow().AddSeconds(-400) };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.DeviceOffline, 300));

        await BuildService().SweepOfflineDevicesAsync(null, CancellationToken.None);

        _alerts.Verify(a => a.AddAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.DeviceOffline), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_DeviceOfflineAlert_IsEmailedAndSentToTelegram_EvenWhenEmailFails()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Offline, LastSeenAt = _clock.GetUtcNow().AddSeconds(-400) };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.DeviceOffline, 300));
        _email.Setup(e => e.NotifyAsync(It.IsAny<Alert>(), device, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("SMTP down"));

        await BuildService().SweepOfflineDevicesAsync(null, CancellationToken.None);

        _email.Verify(e => e.NotifyAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.DeviceOffline), device, It.IsAny<CancellationToken>()), Times.Once);
        _telegram.Verify(t => t.NotifyAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.DeviceOffline), device, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_IngestionUntrusted_SilenceThatStartedBeforeTheOutage_StillFlips()
    {
        // The outage explains the last minute of quiet, not the four days
        // before it — leaving this device Online would be a lie.
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastHeartbeatAt = _clock.GetUtcNow().AddDays(-4)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules();

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(_clock.GetUtcNow().AddMinutes(-1), CancellationToken.None);

        Assert.Equal(1, flipped);
        Assert.Equal(DeviceStatus.Offline, device.Status);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_IngestionUntrusted_SilenceCausedByTheOutage_StaysOnline()
    {
        // Silence stops being counted when ingestion does: this device was
        // fine 10 s before the outage and has not aged since.
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastHeartbeatAt = _clock.GetUtcNow().AddHours(-1).AddSeconds(-10),
            LastTelemetryAt = _clock.GetUtcNow().AddHours(-1).AddSeconds(-10)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.DeviceOffline, 300), LifecycleRule(LifecycleMetrics.NoTelemetry, 120));

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(_clock.GetUtcNow().AddHours(-1), CancellationToken.None);

        Assert.Equal(0, flipped);
        Assert.Equal(DeviceStatus.Online, device.Status);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_IngestionUntrusted_HeardFromAfterTheOutageBegan_IsLeftAlone()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Status = DeviceStatus.Online,
            LastTelemetryAt = _clock.GetUtcNow().AddSeconds(-5)
        };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.NoTelemetry, 120));

        var service = BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 });
        var flipped = await service.SweepOfflineDevicesAsync(_clock.GetUtcNow().AddMinutes(-10), CancellationToken.None);

        Assert.Equal(0, flipped);
        Assert.Equal(DeviceStatus.Online, device.Status);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_FirstContact_ClosesNeverConnected()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Registered };
        var neverConnected = new Alert { Parameter = LifecycleMetrics.NeverConnected, Status = AlertStatus.Open };
        _alerts.Setup(a => a.ListUnresolvedAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([neverConnected]);

        await BuildService().RecordHeartbeatAsync(device, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(AlertStatus.Resolved, neverConnected.Status);
        Assert.Equal(DeviceStatus.Online, device.Status);
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(150, "2.5 min")]
    [InlineData(5400, "1.5 h")]
    [InlineData(86400, "24 h")]
    [InlineData(345600, "4 days")]
    public void Describe_ReadableDurations(int seconds, string expected) =>
        Assert.Equal(expected, DeviceLifecycleService.Describe(TimeSpan.FromSeconds(seconds)));

    /// <summary>A device of a type that reports every 5 minutes, last heard <paramref name="secondsAgo"/> ago.</summary>
    private Device SlowReporter(int secondsAgo) => new()
    {
        Id = Guid.NewGuid(),
        Status = DeviceStatus.Online,
        LastSeenAt = _clock.GetUtcNow().AddSeconds(-secondsAgo),
        LastTelemetryAt = _clock.GetUtcNow().AddSeconds(-secondsAgo),
        DeviceType = new DeviceType { Name = "Inclinometer", ExpectedIntervalSeconds = 300 }
    };

    [Fact]
    public async Task SweepOfflineDevicesAsync_SlowReporterBetweenReports_StaysOnlineAndRaisesNothing()
    {
        // 4 minutes into a 5-minute sleep: past the 90 s platform window, but no report missed.
        var device = SlowReporter(240);
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.NoTelemetry, 120), LifecycleRule(LifecycleMetrics.DeviceOffline, 300));

        var flipped = await BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 }).SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(0, flipped);
        Assert.Equal(DeviceStatus.Online, device.Status);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_SlowReporterMissedTwoReports_GoesOfflineAndAlerts()
    {
        var device = SlowReporter(660);
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules(LifecycleRule(LifecycleMetrics.NoTelemetry, 120), LifecycleRule(LifecycleMetrics.DeviceOffline, 300));

        var flipped = await BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 }).SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(1, flipped);
        Assert.Equal(DeviceStatus.Offline, device.Status);
        _alerts.Verify(a => a.AddAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.NoTelemetry), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.AddAsync(It.Is<Alert>(x => x.Parameter == LifecycleMetrics.DeviceOffline), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SweepOfflineDevicesAsync_TypeWithoutInterval_KeepsPlatformWindow()
    {
        var device = new Device { Id = Guid.NewGuid(), Status = DeviceStatus.Online, LastSeenAt = _clock.GetUtcNow().AddSeconds(-120), DeviceType = new DeviceType { Name = "Meter" } };
        _devices.Setup(d => d.ListForOfflineSweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync([device]);
        GivenRules();

        var flipped = await BuildService(new DeviceLifecycleOptions { OfflineAfterSeconds = 90 }).SweepOfflineDevicesAsync(null, CancellationToken.None);

        Assert.Equal(1, flipped);
    }
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
