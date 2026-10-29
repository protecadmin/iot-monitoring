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

public class BaselineDeltaHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDeviceBaselineRepository> _baselines = new();
    private readonly Mock<ICalculationStateRepository> _states = new();
    private readonly Mock<ICalculatedSeriesStore> _series = new();
    private readonly Device _device = new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceTypeId = Guid.NewGuid() };
    private readonly CalculationRule _rule;

    public BaselineDeltaHandlerTests()
    {
        _rule = new CalculationRule
        {
            Id = Guid.NewGuid(), TenantId = _device.TenantId, Key = "x-tilt-change", Type = CalculationType.BaselineDelta,
            SourceParameterKey = "xDeg", OutputKey = "xDegChange", OutputName = "X tilt change", OutputUnit = "°"
        };
        _states.Setup(s => s.TryCreateAsync(It.IsAny<CalculationState>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private BaselineDeltaHandler Handler() => new(_baselines.Object, _states.Object, _series.Object);

    private void GivenBaseline(double value) =>
        _baselines.Setup(b => b.GetAsync(_device.Id, "xDeg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceBaseline { DeviceId = _device.Id, ParameterKey = "xDeg", Value = value });

    private NormalizedTelemetry Reading(double xDeg) => new()
    {
        TenantId = _device.TenantId, SiteId = _device.SiteId, DeviceId = _device.Id, Timestamp = T0, IngestedAt = T0,
        Fields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["xDeg"] = xDeg }
    };

    [Fact]
    public async Task WithBaseline_PublishesMovementSinceIt()
    {
        GivenBaseline(22.2149);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(22.6149), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("xDegChange", update.OutputKey);
        Assert.Equal(0.4, update.Value, 9);
    }

    [Fact]
    public async Task NegativeBaseline_IsSubtractedCorrectly()
    {
        GivenBaseline(-5.833);

        var update = await Handler().EvaluateAsync(_rule, _device, Reading(-5.5), CancellationToken.None);

        Assert.Equal(0.333, update!.Value, 9);
    }

    [Fact]
    public async Task NoBaseline_PublishesNothing()
    {
        var update = await Handler().EvaluateAsync(_rule, _device, Reading(22.6), CancellationToken.None);

        Assert.Null(update);
        _states.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task History_IsTheFormulaSourceMinusBaseline()
    {
        GivenBaseline(22.2149);
        FormulaExpression? used = null;
        _series.Setup(s => s.GetFormulaSeriesAsync(_device.Id, It.IsAny<FormulaExpression>(), "xDegChange", T0, T0.AddDays(1), null, false, It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback((Guid _, FormulaExpression f, string _, DateTimeOffset _, DateTimeOffset _, string? _, bool _, CancellationToken _, string _) => used = f)
            .ReturnsAsync([]);

        await Handler().QueryAsync(_rule, _device, T0, T0.AddDays(1), null, false, CancellationToken.None);

        Assert.NotNull(used);
        Assert.Equal(["xDeg"], used.Variables);
        Assert.Equal(0.4, used.Evaluate(new Dictionary<string, double> { ["xDeg"] = 22.6149 })!.Value, 9);
    }

    [Fact]
    public void Source_IsAnyMeasuredParameter()
    {
        Assert.True(Handler().IsEligibleSource(new DeviceTypeParameter { Key = "xDeg", Kind = ParameterKind.Instantaneous }));
        Assert.False(Handler().IsEligibleSource(new DeviceTypeParameter { Key = "x", Kind = ParameterKind.Derived }));
    }
}
