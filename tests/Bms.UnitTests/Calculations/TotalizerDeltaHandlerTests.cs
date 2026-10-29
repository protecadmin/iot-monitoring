using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Calculations.Handlers;
using Bms.Domain;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class TotalizerDeltaHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICalculationStateRepository> _states = new();
    private readonly Mock<ICounterResetRepository> _resets = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };
    private readonly CalculationRule _rule;

    public TotalizerDeltaHandlerTests()
    {
        _rule = new CalculationRule
        {
            TenantId = _device.TenantId,
            Key = "energy-consumption",
            Type = CalculationType.TotalizerDelta,
            SourceParameterKey = "totalizer",
            OutputKey = "energyConsumption",
            OutputName = "Energy Consumption",
            OutputUnit = "kWh"
        };
    }

    private TotalizerDeltaHandler Handler() => new(_states.Object, _resets.Object, new Mock<ICalculatedSeriesStore>().Object, NullLogger<TotalizerDeltaHandler>.Instance);

    private NormalizedTelemetry Reading(double totalizer, DateTimeOffset at) => new()
    {
        TenantId = _device.TenantId,
        SiteId = _device.SiteId,
        DeviceId = _device.Id,
        Timestamp = at,
        IngestedAt = at,
        Fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["totalizer"] = totalizer }
    };

    [Fact]
    public void OnlyCounterParametersAreEligibleSources_RegardlessOfUnit()
    {
        var handler = Handler();

        Assert.True(handler.IsEligibleSource(new DeviceTypeParameter { Key = "totalizer", Unit = "kWh", Kind = ParameterKind.Counter }));
        Assert.False(handler.IsEligibleSource(new DeviceTypeParameter { Key = "energy", Unit = "kWh", Kind = ParameterKind.Instantaneous }));
        Assert.True(handler.IsEligibleSource(new DeviceTypeParameter { Key = "runtime", Unit = "h", Kind = ParameterKind.Counter }));
    }

    [Fact]
    public async Task FirstReading_CreatesBaseline_AndPublishesNothing()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>())).ReturnsAsync((CalculationState?)null);
        _states.Setup(s => s.TryCreateAsync(It.IsAny<CalculationState>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(100000.20, T0), CancellationToken.None);

        Assert.Null(update);
        _states.Verify(s => s.TryCreateAsync(It.Is<CalculationState>(c => c.LastSourceValue == 100000.20 && c.DeviceId == _device.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Increase_AdvancesTheBaseline_AndPublishesTheConsumption()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceValue = 100000.20, LastSourceTimestamp = T0 });
        _states.Setup(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0, 100000.21, T0.AddMinutes(5), It.IsAny<double?>(), T0.AddMinutes(5), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(100000.21, T0.AddMinutes(5)), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("energyConsumption", update.OutputKey);
        Assert.Equal("kWh", update.OutputUnit);
        Assert.Equal(0.01, update.Value, precision: 9);
    }

    [Fact]
    public async Task LostConcurrentUpdate_PublishesNothing_SoNothingIsCountedTwice()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceValue = 10, LastSourceTimestamp = T0 });
        _states.Setup(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(11, T0.AddMinutes(5)), CancellationToken.None);

        Assert.Null(update);
    }

    [Fact]
    public async Task Decrease_MovesTheBaselineWithoutPublishing_AndKeepsTheLastValue()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceValue = 98000, LastSourceTimestamp = T0 });
        _states.Setup(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(3, T0.AddMinutes(5)), CancellationToken.None);

        Assert.Null(update);
        _states.Verify(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0, 3, T0.AddMinutes(5), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateTimestamp_TouchesNoState()
    {
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceValue = 10, LastSourceTimestamp = T0 });

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(10.5, T0), CancellationToken.None);

        Assert.Null(update);
        _states.Verify(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadingAfterAScalingChange_OnlyRebasesTheCounter_AndPublishesNothing()
    {
        // The meter was stored as-is (4500 kWh) and its mapping now divides by
        // 100: the next reading is 45.10. That is a new basis, not a -4454.9 kWh
        // (or, the other way round, a +4455 kWh) consumption.
        _states.Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculationState { LastSourceValue = 4500, LastSourceTimestamp = T0 });
        _resets.Setup(r => r.AnyBetweenAsync(_device.Id, "totalizer", T0, T0.AddMinutes(5), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _states.Setup(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0, 45.10, T0.AddMinutes(5), null, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(45.10, T0.AddMinutes(5)), CancellationToken.None);

        Assert.Null(update);
        _states.Verify(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0, 45.10, T0.AddMinutes(5), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
