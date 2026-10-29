using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Calculations.Handlers;
using Bms.Domain;
using Bms.Domain.Calculations;
using Bms.Domain.Entities;
using Bms.Domain.Telemetry;
using Moq;
using Xunit;

namespace Bms.UnitTests.Calculations;

/// <summary>
/// The point of a windowed average is that it publishes once per period, so
/// what is pinned here is when a value appears and which period it covers —
/// the averaging itself is done by the store's Flux query, over the window
/// this handler asks for.
/// </summary>
public class WindowedLevelHandlerTests
{
    // 08:00 begins a five-minute window, so the boundary is at 08:05.
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 10, 19, 8, 5, 0, TimeSpan.Zero);

    private readonly Mock<ICalculationStateRepository> _states = new();
    private readonly Mock<ICalculatedSeriesStore> _series = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };
    private readonly CalculationRule _rule;

    public WindowedLevelHandlerTests()
    {
        _rule = new CalculationRule
        {
            TenantId = _device.TenantId, Key = "noise-sensor-leq", Type = CalculationType.EquivalentContinuousLevel,
            SourceParameterKey = "noiseLevel", WindowSeconds = 300,
            OutputKey = "leq", OutputName = "Leq (5 min)", OutputUnit = "dB(A)"
        };
        _states.Setup(s => s.TryCreateAsync(It.IsAny<CalculationState>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _states.Setup(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private EquivalentContinuousLevelHandler Handler() => new(_states.Object, _series.Object);

    private NormalizedTelemetry Reading(DateTimeOffset at, double value) => new()
    {
        TenantId = _device.TenantId, SiteId = _device.SiteId, DeviceId = _device.Id,
        Timestamp = at, IngestedAt = at,
        Fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["noiseLevel"] = value }
    };

    private void WindowAverages(double value) => _series
        .Setup(s => s.GetWindowedLevelSeriesAsync(_device.Id, "noiseLevel", It.IsAny<LevelAggregation>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
        .ReturnsAsync([new CalculatedPoint(WindowEnd, value)]);

    private void OpenWindowAt(DateTimeOffset at) => _states
        .Setup(s => s.GetAsync(_rule.Id, _device.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CalculationState { CalculationRuleId = _rule.Id, DeviceId = _device.Id, LastSourceValue = 70, LastSourceTimestamp = at });

    [Fact]
    public async Task FirstReading_OnlyOpensTheWindow()
    {
        var update = await Handler().EvaluateAsync(_rule, _device, Reading(T0, 91), CancellationToken.None);

        Assert.Null(update);
        _states.Verify(s => s.TryCreateAsync(It.Is<CalculationState>(c => c.LastOutputValue == null), It.IsAny<CancellationToken>()), Times.Once);
        _series.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadingInsideTheOpenWindow_PublishesNothing()
    {
        OpenWindowAt(T0);

        // A momentary 96 dB spike — the thing that fired a critical alert on
        // every reading before — is just one sample of the window now.
        var update = await Handler().EvaluateAsync(_rule, _device, Reading(T0.AddSeconds(5), 96), CancellationToken.None);

        Assert.Null(update);
        _series.VerifyNoOtherCalls();
        _states.Verify(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0, 96, T0.AddSeconds(5), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CrossingTheBoundary_PublishesTheClosedWindowStampedAtItsEnd()
    {
        OpenWindowAt(T0.AddSeconds(295));
        WindowAverages(87.4);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("leq", update.OutputKey);
        Assert.Equal(87.4, update.Value);
        Assert.Equal(WindowEnd, update.Timestamp);
        _series.Verify(s => s.GetWindowedLevelSeriesAsync(_device.Id, "noiseLevel", LevelAggregation.EnergyDecibel,
            T0, WindowEnd, "300s", null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeqAveragesSoundEnergy_AnAverageOfALevelDoesNot()
    {
        OpenWindowAt(T0.AddSeconds(295));
        WindowAverages(87.4);
        await Handler().EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None);

        var arithmetic = new TimeWeightedAverageHandler(_states.Object, _series.Object);
        _rule.Type = CalculationType.TimeWeightedAverage;
        await arithmetic.EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None);

        _series.Verify(s => s.GetWindowedLevelSeriesAsync(It.IsAny<Guid>(), It.IsAny<string>(), LevelAggregation.EnergyDecibel,
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
        _series.Verify(s => s.GetWindowedLevelSeriesAsync(It.IsAny<Guid>(), It.IsAny<string>(), LevelAggregation.Arithmetic,
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AGapLongerThanAWindow_PublishesTheWindowThatWasOpen_NotTheOnesWithNoReadings()
    {
        OpenWindowAt(T0.AddSeconds(10));
        WindowAverages(84.2);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(T0.AddHours(2), 75), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal(WindowEnd, update.Timestamp);
        _series.Verify(s => s.GetWindowedLevelSeriesAsync(_device.Id, "noiseLevel", It.IsAny<LevelAggregation>(),
            T0, WindowEnd, "300s", null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ALateReading_NeitherPublishesNorMovesTheWindow()
    {
        OpenWindowAt(WindowEnd);

        Assert.Null(await Handler().EvaluateAsync(_rule, _device, Reading(T0.AddSeconds(30), 95), CancellationToken.None));
        _series.VerifyNoOtherCalls();
        _states.Verify(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnotherMessageClosedTheWindowFirst_PublishesNothing()
    {
        OpenWindowAt(T0.AddSeconds(295));
        WindowAverages(87.4);
        _states.Setup(s => s.TryAdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<double>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<double?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Null(await Handler().EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None));
    }

    [Fact]
    public async Task AWindowWithNothingToAverage_OpensTheNextOneWithoutPublishing()
    {
        OpenWindowAt(T0.AddSeconds(295));
        _series.Setup(s => s.GetWindowedLevelSeriesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<LevelAggregation>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>(), null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        Assert.Null(await Handler().EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None));
        _states.Verify(s => s.TryAdvanceAsync(_rule.Id, _device.Id, T0.AddSeconds(295), 71, WindowEnd, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoWindowOnTheRule_FallsBackToTheDefaultPeriod()
    {
        _rule.WindowSeconds = null;
        OpenWindowAt(T0.AddSeconds(295));
        WindowAverages(87.4);

        await Handler().EvaluateAsync(_rule, _device, Reading(WindowEnd, 71), CancellationToken.None);

        _series.Verify(s => s.GetWindowedLevelSeriesAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<LevelAggregation>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), LevelWindow.ToFluxDuration(LevelWindow.DefaultSeconds),
            null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ParameterKind.Instantaneous, true)]
    [InlineData(ParameterKind.Counter, false)]
    [InlineData(ParameterKind.Digital, false)]
    [InlineData(ParameterKind.Derived, false)]
    public void OnlyAMeasuredLevelCanBeAveraged(ParameterKind kind, bool eligible)
    {
        Assert.Equal(eligible, Handler().IsEligibleSource(new DeviceTypeParameter { Key = "noiseLevel", Kind = kind }));
    }

    [Fact]
    public async Task AskingForFinerPointsThanTheWindow_StillReturnsWindows()
    {
        _rule.WindowSeconds = 28_800;
        await Handler().QueryAsync(_rule, _device, T0, T0.AddDays(1), "5m", timestampAtWindowStart: false, CancellationToken.None);

        _series.Verify(s => s.GetWindowedLevelSeriesAsync(_device.Id, "noiseLevel", It.IsAny<LevelAggregation>(),
            T0, T0.AddDays(1), "28800s", null, It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskingForCoarserPeriodsThanTheWindow_CombinesWholeWindows()
    {
        await Handler().QueryAsync(_rule, _device, T0, T0.AddDays(1), "1h", timestampAtWindowStart: false, CancellationToken.None);

        _series.Verify(s => s.GetWindowedLevelSeriesAsync(_device.Id, "noiseLevel", It.IsAny<LevelAggregation>(),
            T0, T0.AddDays(1), "300s", "1h", "mean", false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
