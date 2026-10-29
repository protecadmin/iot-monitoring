using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Calculations;
using Bms.Calculations.Handlers;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Moq;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class FormulaHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICalculationStateRepository> _states = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };
    private readonly CalculationRule _rule;

    public FormulaHandlerTests()
    {
        _rule = new CalculationRule
        {
            TenantId = _device.TenantId, Key = "energy-meter-active-power", Type = CalculationType.Formula,
            Expression = "voltage * current * powerFactor / 1000",
            OutputKey = "activePower", OutputName = "Active Power", OutputUnit = "kW"
        };
        _states.Setup(s => s.TryCreateAsync(It.IsAny<CalculationState>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private FormulaHandler Handler() => new(_states.Object, new Mock<ICalculatedSeriesStore>().Object);

    private NormalizedTelemetry Reading(DateTimeOffset at, params (string Key, double Value)[] fields) => new()
    {
        TenantId = _device.TenantId, SiteId = _device.SiteId, DeviceId = _device.Id,
        Timestamp = at, IngestedAt = at,
        Fields = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase)
    };

    [Fact]
    public async Task AllInputs_PublishesTheFormulaValue()
    {
        var update = await Handler().EvaluateAsync(_rule, _device, Reading(T0, ("voltage", 230), ("current", 12), ("powerFactor", 0.95)), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("activePower", update.OutputKey);
        Assert.Equal(2.622, update.Value, 9);
        _states.Verify(s => s.TryCreateAsync(It.Is<CalculationState>(c => c.LastOutputValue == update.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeviceSendsTheValueItself_NothingIsCalculated()
    {
        var update = await Handler().EvaluateAsync(_rule, _device,
            Reading(T0, ("voltage", 230), ("current", 12), ("powerFactor", 0.95), ("activePower", 2.7)), CancellationToken.None);

        Assert.Null(update);
        _states.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingInput_NothingIsCalculated()
    {
        Assert.Null(await Handler().EvaluateAsync(_rule, _device, Reading(T0, ("voltage", 230), ("current", 12)), CancellationToken.None));
    }

    [Fact]
    public async Task OlderThanTheLatestValue_IsNotPublished()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceTimestamp = T0, LastSourceValue = 2.5, LastOutputValue = 2.5, LastOutputTimestamp = T0 });

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(T0.AddMinutes(-1), ("voltage", 230), ("current", 12), ("powerFactor", 0.95)), CancellationToken.None);

        Assert.Null(update);
    }

    [Fact]
    public void Eligibility_NeedsEveryInputAsAMeasuredParameter()
    {
        var handler = Handler();
        DeviceTypeParameter P(string key, ParameterKind kind = ParameterKind.Instantaneous) => new() { Key = key, Kind = kind };

        Assert.True(CalculationScope.IsEligible(handler, _rule, [P("voltage"), P("current"), P("powerFactor"), P("activePower", ParameterKind.Derived)]));
        Assert.False(CalculationScope.IsEligible(handler, _rule, [P("voltage"), P("current")]));
        Assert.False(CalculationScope.IsEligible(handler, _rule, [P("voltage"), P("current"), P("powerFactor", ParameterKind.Derived)]));
    }
}
