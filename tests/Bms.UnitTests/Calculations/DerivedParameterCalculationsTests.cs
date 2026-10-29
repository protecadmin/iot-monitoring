using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Application.Common;
using Bms.Calculations;
using Bms.Calculations.Handlers;
using Bms.Domain;
using Bms.Domain.Entities;
using Moq;
using Xunit;

namespace Bms.UnitTests.Calculations;

public class DerivedParameterCalculationsTests
{
    private readonly List<CalculationRule> _rules = [];
    private readonly Mock<ICalculationStateRepository> _states = new();
    private readonly Tenant _whirlpool = new() { Id = Guid.NewGuid(), Name = "Whirlpool", Slug = "whirlpool" };
    private readonly Tenant _abc = new() { Id = Guid.NewGuid(), Name = "ABC", Slug = "abc" };
    private readonly DeviceType _meter;

    public DerivedParameterCalculationsTests()
    {
        _meter = new DeviceType
        {
            Id = Guid.NewGuid(),
            Name = "Energy Meter",
            Parameters =
            [
                P("voltage"), P("current"), P("powerFactor"),
                P("energy", ParameterKind.Counter),
                P("activePower", ParameterKind.Derived)
            ]
        };
    }

    private static DeviceTypeParameter P(string key, ParameterKind kind = ParameterKind.Instantaneous) =>
        new() { Id = Guid.NewGuid(), Key = key, Label = key, Kind = kind, Unit = "u" };

    private DeviceTypeParameter ActivePower(string expression = "voltage * current * powerFactor / 1000")
    {
        var p = _meter.Parameters.Single(x => x.Key == "activePower");
        p.CalculationType = CalculationType.Formula;
        p.CalculationExpression = expression;
        p.Unit = "kW";
        return p;
    }

    private DerivedParameterCalculations Service()
    {
        var rules = new Mock<ICalculationRuleRepository>();
        rules.Setup(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, CancellationToken _) => _rules.Where(r => r.TenantId == tenantId).ToList());
        rules.Setup(r => r.AddAsync(It.IsAny<CalculationRule>(), It.IsAny<CancellationToken>()))
            .Callback<CalculationRule, CancellationToken>((r, _) => _rules.Add(r))
            .ReturnsAsync((CalculationRule r, CancellationToken _) => r);
        rules.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid id, CancellationToken _) => _rules.RemoveAll(r => r.TenantId == tenantId && r.Id == id) > 0);

        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.ListAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Tenant> { Items = [_whirlpool, _abc], TotalCount = 2, Page = 1, PageSize = 200 });

        var deviceTypes = new Mock<IDeviceTypeRepository>();
        deviceTypes.Setup(d => d.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_meter]);

        var series = new Mock<ICalculatedSeriesStore>().Object;
        ICalculationHandler[] handlers =
        [
            new FormulaHandler(_states.Object, series),
            new TotalizerDeltaHandler(_states.Object, new Mock<ICounterResetRepository>().Object, series,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TotalizerDeltaHandler>.Instance)
        ];
        return new DerivedParameterCalculations(rules.Object, _states.Object, tenants.Object, deviceTypes.Object, handlers);
    }

    [Fact]
    public async Task Apply_CreatesOneAutomaticDeviceTypeRulePerTenant()
    {
        var warnings = await Service().ApplyAsync(_meter, ActivePower(), CancellationToken.None);

        Assert.Empty(warnings);
        Assert.Equal(2, _rules.Count);
        Assert.All(_rules, r =>
        {
            Assert.Equal("energy-meter-active-power", r.Key);
            Assert.Equal(CalculationType.Formula, r.Type);
            Assert.Equal("voltage * current * powerFactor / 1000", r.Expression);
            Assert.Equal("activePower", r.OutputKey);
            Assert.Equal("kW", r.OutputUnit);
            Assert.Equal(_meter.Id, r.DeviceTypeId);
            Assert.Equal(CalculationAssignmentMode.Automatic, r.AssignmentMode);
        });
        Assert.Equal([_whirlpool.Id, _abc.Id], _rules.Select(r => r.TenantId));
    }

    [Fact]
    public async Task Apply_AdoptsAnExistingDeviceTypeRuleAndResetsItsBaselineWhenTheFormulaChanges()
    {
        var existing = new CalculationRule
        {
            TenantId = _whirlpool.Id, Key = "made-on-calculations-page", DeviceTypeId = _meter.Id, Type = CalculationType.Formula,
            Expression = "voltage * current / 1000", OutputKey = "activePower", OutputName = "Active Power", IsEnabled = false
        };
        _rules.Add(existing);

        await Service().ApplyAsync(_meter, ActivePower(), CancellationToken.None);

        Assert.Single(_rules, r => r.TenantId == _whirlpool.Id);
        Assert.Equal("voltage * current * powerFactor / 1000", existing.Expression);
        Assert.False(existing.IsEnabled); // a tenant's own on/off choice is kept
        _states.Verify(s => s.DeleteForRuleAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_ReportsATenantWhereAnotherRuleAlreadyOutputsTheKey()
    {
        _rules.Add(new CalculationRule
        {
            TenantId = _abc.Id, Key = "site-power", Name = "Site power", SiteId = Guid.NewGuid(), DeviceTypeId = _meter.Id,
            Type = CalculationType.Formula, Expression = "voltage * current", OutputKey = "activePower"
        });

        var warnings = await Service().ApplyAsync(_meter, ActivePower(), CancellationToken.None);

        Assert.Equal(["ABC: calculation rule 'Site power' already outputs 'activePower'"], warnings);
        Assert.Single(_rules, r => r.TenantId == _whirlpool.Id);
    }

    [Fact]
    public async Task Apply_WithoutACalculation_RemovesTheRules()
    {
        var service = Service();
        var parameter = ActivePower();
        await service.ApplyAsync(_meter, parameter, CancellationToken.None);

        parameter.CalculationType = null;
        await service.ApplyAsync(_meter, parameter, CancellationToken.None);

        Assert.Empty(_rules);
    }

    [Fact]
    public async Task NewTenant_GetsARuleForEveryCalculatedParameter()
    {
        ActivePower();
        var newTenant = Guid.NewGuid();

        await Service().MaterializeForTenantAsync(newTenant, CancellationToken.None);

        var rule = Assert.Single(_rules);
        Assert.Equal(newTenant, rule.TenantId);
        Assert.Equal("activePower", rule.OutputKey);
    }

    [Theory]
    [InlineData("voltage * current * powerFactor / 1000", null)]
    [InlineData("voltage * temperature", "'temperature' isn't a parameter of Energy Meter")]
    [InlineData("voltage * energy", null)]
    [InlineData("activePower * 2", "A formula can't use its own output")]
    [InlineData("voltage *", "Formula: Formula ends too early at position 10")]
    public void Validate_Formula(string expression, string? expected)
    {
        Assert.Equal(expected, Service().Validate(_meter, ActivePower(expression)));
    }

    [Fact]
    public void Validate_TotalizerNeedsACounterSource()
    {
        var p = _meter.Parameters.Single(x => x.Key == "activePower");
        p.CalculationType = CalculationType.TotalizerDelta;
        p.CalculationSourceKey = "voltage";

        Assert.Equal("'voltage' on Energy Meter must each be a Counter parameter (a cumulative totalizer)", Service().Validate(_meter, p));

        p.CalculationSourceKey = "energy";
        Assert.Null(Service().Validate(_meter, p));
    }

    [Fact]
    public async Task IsDefinedBy_OnlyTheTenantCopyOfACalculatedParameter()
    {
        await Service().ApplyAsync(_meter, ActivePower(), CancellationToken.None);
        var copy = _rules.First(r => r.TenantId == _whirlpool.Id);
        Assert.True(DerivedParameterCalculations.IsDefinedBy(_meter, copy));

        // The same output narrowed to a site, or another output, is the tenant's own rule.
        var siteRule = new CalculationRule { DeviceTypeId = _meter.Id, SiteId = Guid.NewGuid(), OutputKey = "activePower" };
        var ownRule = new CalculationRule { DeviceTypeId = _meter.Id, OutputKey = "apparentPower" };
        Assert.False(DerivedParameterCalculations.IsDefinedBy(_meter, siteRule));
        Assert.False(DerivedParameterCalculations.IsDefinedBy(_meter, ownRule));

        // Once the parameter is no longer calculated, the rule isn't the type's any more.
        _meter.Parameters.Single(x => x.Key == "activePower").CalculationType = null;
        Assert.False(DerivedParameterCalculations.IsDefinedBy(_meter, copy));
    }
}
