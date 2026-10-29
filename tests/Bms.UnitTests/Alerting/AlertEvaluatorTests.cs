using Bms.Alerting;
using Bms.Application.Abstractions.Persistence;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bms.UnitTests.Alerting;

public class AlertEvaluatorTests
{
    private readonly Mock<IAlertRuleRepository> _rules = new();
    private readonly Mock<IAlertRepository> _alerts = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };

    private readonly AlertRule _highTempRule = new()
    {
        Id = Guid.NewGuid(),
        Name = "High Temperature",
        Metric = "temperature",
        Operator = AlertRuleOperator.GreaterThan,
        Threshold = 50,
        Severity = AlertSeverity.Warning,
        CooldownSeconds = 300,
        IsEnabled = true
    };

    private AlertEvaluator BuildEvaluator(TimeProvider clock) =>
        new(_rules.Object, _alerts.Object, clock, NullLogger<AlertEvaluator>.Instance);

    private NormalizedTelemetry Telemetry(double temperature) => new()
    {
        TenantId = _device.TenantId,
        SiteId = _device.SiteId,
        DeviceId = _device.Id,
        Timestamp = DateTimeOffset.UtcNow,
        IngestedAt = DateTimeOffset.UtcNow,
        Fields = new Dictionary<string, double> { ["temperature"] = temperature }
    };

    [Fact]
    public async Task EvaluateAsync_BreachedThreshold_CreatesAlert()
    {
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.GetLastTriggeredAsync(_device.Id, _highTempRule.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Alert?)null);

        var evaluator = BuildEvaluator(TimeProvider.System);
        var result = await evaluator.EvaluateAsync(_device, Telemetry(55), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(AlertSeverity.Warning, result[0].Severity);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_DeviceAlertsOff_CreatesNoAlertEvenWhenBreached()
    {
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _device.AlertsEnabled = false;

        var result = await BuildEvaluator(TimeProvider.System).EvaluateAsync(_device, Telemetry(55), CancellationToken.None);

        Assert.Empty(result);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_ValueBelowThreshold_CreatesNoAlert()
    {
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);

        var evaluator = BuildEvaluator(TimeProvider.System);
        var result = await evaluator.EvaluateAsync(_device, Telemetry(30), CancellationToken.None);

        Assert.Empty(result);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_AlreadyOpenAlertForSameRule_SuppressesDuplicateEvenAfterCooldown()
    {
        // Duplicate prevention (core.txt section 26): an OPEN alert for the
        // same device+rule must not spawn a second one, regardless of
        // cooldown — only a Resolved alert clears the way for a new trigger.
        var fakeClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var openAlert = new Alert { Status = AlertStatus.Open, TriggeredAt = fakeClock.GetUtcNow().AddSeconds(-1000) };

        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.GetLastTriggeredAsync(_device.Id, _highTempRule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(openAlert);

        var evaluator = BuildEvaluator(fakeClock);
        var result = await evaluator.EvaluateAsync(_device, Telemetry(60), CancellationToken.None);

        Assert.Empty(result);
        _alerts.Verify(a => a.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_ResolvedAlertWithinCooldown_StillSuppressed()
    {
        var fakeClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var resolvedAlert = new Alert { Status = AlertStatus.Resolved, TriggeredAt = fakeClock.GetUtcNow().AddSeconds(-10) };

        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.GetLastTriggeredAsync(_device.Id, _highTempRule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(resolvedAlert);

        var evaluator = BuildEvaluator(fakeClock);
        var result = await evaluator.EvaluateAsync(_device, Telemetry(60), CancellationToken.None);

        Assert.Empty(result); // within the 300s cooldown, even though the previous one resolved
    }

    [Fact]
    public async Task EvaluateAsync_ResolvedAlertAfterCooldown_AllowsNewTrigger()
    {
        var fakeClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var resolvedAlert = new Alert { Status = AlertStatus.Resolved, TriggeredAt = fakeClock.GetUtcNow().AddSeconds(-400) };

        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.GetLastTriggeredAsync(_device.Id, _highTempRule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(resolvedAlert);

        var evaluator = BuildEvaluator(fakeClock);
        var result = await evaluator.EvaluateAsync(_device, Telemetry(60), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_ReadingBackWithinRule_ResolvesItsOpenAlert()
    {
        var open = new Alert { DeviceId = _device.Id, AlertRuleId = _highTempRule.Id, Status = AlertStatus.Acknowledged };
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.ListUnresolvedAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([open]);

        var result = await BuildEvaluator(TimeProvider.System).EvaluateAsync(_device, Telemetry(30), CancellationToken.None);

        Assert.Same(open, Assert.Single(result));
        Assert.Equal(AlertStatus.Resolved, open.Status);
        Assert.NotNull(open.ResolvedAt);
        Assert.Equal(30, open.CurrentValue);
        _alerts.Verify(a => a.UpdateAsync(open, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_StillBreached_LeavesOpenAlertOpen()
    {
        var open = new Alert { DeviceId = _device.Id, AlertRuleId = _highTempRule.Id, Status = AlertStatus.Open, TriggeredAt = DateTimeOffset.UtcNow };
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.ListUnresolvedAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([open]);
        _alerts.Setup(a => a.GetLastTriggeredAsync(_device.Id, _highTempRule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(open);

        var result = await BuildEvaluator(TimeProvider.System).EvaluateAsync(_device, Telemetry(60), CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(AlertStatus.Open, open.Status);
    }

    [Fact]
    public async Task EvaluateAsync_ReadingWithoutTheRulesMetric_LeavesOpenAlertOpen()
    {
        var open = new Alert { DeviceId = _device.Id, AlertRuleId = _highTempRule.Id, Status = AlertStatus.Open };
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_highTempRule]);
        _alerts.Setup(a => a.ListUnresolvedAsync(_device.Id, It.IsAny<CancellationToken>())).ReturnsAsync([open]);
        var humidityOnly = new NormalizedTelemetry
        {
            TenantId = _device.TenantId,
            SiteId = _device.SiteId,
            DeviceId = _device.Id,
            Timestamp = DateTimeOffset.UtcNow,
            IngestedAt = DateTimeOffset.UtcNow,
            Fields = new Dictionary<string, double> { ["humidity"] = 40 }
        };

        var result = await BuildEvaluator(TimeProvider.System).EvaluateAsync(_device, humidityOnly, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(AlertStatus.Open, open.Status);
    }
}

/// <summary>Minimal controllable clock for cooldown-window tests.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
