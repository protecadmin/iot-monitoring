using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Realtime;
using Bms.Calculations;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class CalculationEngineTests
{
    private readonly Mock<ICalculationRuleRepository> _rules = new();
    private readonly Mock<ICalculationHandler> _handler = new();
    private readonly Mock<IRealtimeNotifier> _notifier = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };

    public CalculationEngineTests()
    {
        _handler.Setup(h => h.Type).Returns(CalculationType.TotalizerDelta);
        _handler.Setup(h => h.SourceRequirement).Returns("a Counter parameter");
        _handler.Setup(h => h.IsEligibleSource(It.IsAny<DeviceTypeParameter>())).Returns((DeviceTypeParameter p) => p.Kind == ParameterKind.Counter);
        _handler.Setup(h => h.InputKeys(It.IsAny<CalculationRule>())).Returns((CalculationRule r) => [r.SourceParameterKey]);
    }

    private CalculationEngine Engine() => new(_rules.Object, [_handler.Object], _notifier.Object, NullLogger<CalculationEngine>.Instance);

    private CalculationRule Rule(string key = "energy-consumption", string source = "totalizer") => new()
    {
        TenantId = _device.TenantId, Key = key, Type = CalculationType.TotalizerDelta,
        SourceParameterKey = source, OutputKey = "energyConsumption", OutputName = "Energy Consumption"
    };

    private NormalizedTelemetry Reading(params (string Key, double Value)[] fields) => new()
    {
        TenantId = _device.TenantId, SiteId = _device.SiteId, DeviceId = _device.Id,
        Timestamp = DateTimeOffset.UtcNow, IngestedAt = DateTimeOffset.UtcNow,
        Fields = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase)
    };

    private void GivenRules(params CalculationRule[] rules) =>
        _rules.Setup(r => r.GetApplicableRulesAsync(_device.TenantId, _device.SiteId, _device.Id, _device.DeviceTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

    private static DeviceTypeParameter Param(string key, ParameterKind kind, string unit = "kWh") => new() { Key = key, Kind = kind, Unit = unit };

    [Fact]
    public async Task CounterSource_ValueProduced_IsPublishedOverRealtime()
    {
        var rule = Rule();
        GivenRules(rule);
        var update = new CalculationUpdate(_device.TenantId, _device.SiteId, _device.Id, rule.Id, rule.Key, "energyConsumption", "Energy Consumption", "kWh", 0.01, DateTimeOffset.UtcNow);
        _handler.Setup(h => h.EvaluateAsync(rule, _device, It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>())).ReturnsAsync(update);

        await Engine().EvaluateAsync(_device, [Param("totalizer", ParameterKind.Counter)], Reading(("totalizer", 100000.21)), CancellationToken.None);

        _notifier.Verify(n => n.CalculationUpdatedAsync(update, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KwhParameterThatIsNotACounter_IsNeverTreatedAsATotalizer()
    {
        GivenRules(Rule(source: "energy"));

        await Engine().EvaluateAsync(_device, [Param("energy", ParameterKind.Instantaneous)], Reading(("energy", 42)), CancellationToken.None);

        _handler.Verify(h => h.EvaluateAsync(It.IsAny<CalculationRule>(), It.IsAny<Device>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadingWithoutTheSourceField_IsSkipped()
    {
        GivenRules(Rule());

        await Engine().EvaluateAsync(_device, [Param("totalizer", ParameterKind.Counter)], Reading(("voltage", 230)), CancellationToken.None);

        _handler.Verify(h => h.EvaluateAsync(It.IsAny<CalculationRule>(), It.IsAny<Device>(), It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoValueProduced_NothingPublished()
    {
        var rule = Rule();
        GivenRules(rule);
        _handler.Setup(h => h.EvaluateAsync(rule, _device, It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>())).ReturnsAsync((CalculationUpdate?)null);

        await Engine().EvaluateAsync(_device, [Param("totalizer", ParameterKind.Counter)], Reading(("totalizer", 1)), CancellationToken.None);

        _notifier.Verify(n => n.CalculationUpdatedAsync(It.IsAny<CalculationUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OneFailingRule_DoesNotStopTheOthers()
    {
        var failing = Rule(key: "broken");
        var healthy = Rule(key: "energy-consumption");
        GivenRules(failing, healthy);
        _handler.Setup(h => h.EvaluateAsync(failing, _device, It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        _handler.Setup(h => h.EvaluateAsync(healthy, _device, It.IsAny<NormalizedTelemetry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationUpdate(_device.TenantId, _device.SiteId, _device.Id, healthy.Id, healthy.Key, "energyConsumption", "Energy Consumption", "kWh", 0.2, DateTimeOffset.UtcNow));

        await Engine().EvaluateAsync(_device, [Param("totalizer", ParameterKind.Counter)], Reading(("totalizer", 5)), CancellationToken.None);

        _notifier.Verify(n => n.CalculationUpdatedAsync(It.Is<CalculationUpdate>(u => u.RuleKey == "energy-consumption"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
