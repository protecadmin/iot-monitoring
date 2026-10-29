using Bms.Domain;
using Bms.Domain.Alerting;
using Bms.Domain.Entities;
using Xunit;

namespace Bms.UnitTests.Alerting;

public class AlertRuleResolutionTests
{
    private static readonly Guid Whirlpool = Guid.NewGuid();
    private static readonly Guid Abc = Guid.NewGuid();
    private static readonly Guid Chennai = Guid.NewGuid();
    private static readonly Guid Pune = Guid.NewGuid();
    private static readonly Guid Meter17 = Guid.NewGuid();
    private static readonly Guid Meter18 = Guid.NewGuid();

    private readonly GlobalAlertRule _highVoltage = new()
    {
        Name = "High Voltage", Metric = "voltage", Operator = AlertRuleOperator.GreaterThan,
        Threshold = 250, Severity = AlertSeverity.Critical, CooldownSeconds = 300
    };

    private AlertRuleOverride Change(Guid tenant, Guid? site = null, Guid? device = null,
        bool? enabled = null, double? threshold = null, AlertSeverity? severity = null) =>
        new() { TenantId = tenant, GlobalAlertRuleId = _highVoltage.Id, SiteId = site, DeviceId = device, IsEnabled = enabled, Threshold = threshold, Severity = severity };

    [Fact]
    public void NoChanges_UsesTheGlobalValues()
    {
        var e = AlertRuleResolution.Resolve(_highVoltage, [], Whirlpool, Chennai, Meter17);

        Assert.True(e.IsEnabled);
        Assert.Equal(250, e.Threshold);
        Assert.Equal(AlertRuleLevel.Global, e.ThresholdFrom);
        Assert.Equal(AlertSeverity.Critical, e.Severity);
    }

    [Fact]
    public void MostSpecificChangeWins_EachValueOnItsOwn()
    {
        AlertRuleOverride[] changes =
        [
            Change(Whirlpool, threshold: 245, severity: AlertSeverity.Warning),
            Change(Whirlpool, site: Chennai, threshold: 242),
            Change(Whirlpool, device: Meter18, threshold: 240)
        ];

        var meter18 = AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Chennai, Meter18);
        Assert.Equal(240, meter18.Threshold);
        Assert.Equal(AlertRuleLevel.Device, meter18.ThresholdFrom);
        Assert.Equal(AlertSeverity.Warning, meter18.Severity); // the company's, nothing more specific changed it
        Assert.Equal(AlertRuleLevel.Company, meter18.SeverityFrom);

        var meter17 = AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Chennai, Meter17);
        Assert.Equal(242, meter17.Threshold);
        Assert.Equal(AlertRuleLevel.Site, meter17.ThresholdFrom);

        var pune = AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Pune, null);
        Assert.Equal(245, pune.Threshold);
        Assert.Equal(AlertRuleLevel.Company, pune.ThresholdFrom);
    }

    [Fact]
    public void AnotherCompanysChange_DoesNotApply()
    {
        var e = AlertRuleResolution.Resolve(_highVoltage, [Change(Abc, threshold: 200)], Whirlpool, Chennai, Meter17);

        Assert.Equal(250, e.Threshold);
    }

    [Fact]
    public void ADeviceCanSwitchTheRuleOffAndItsSiteStaysOn()
    {
        AlertRuleOverride[] changes = [Change(Whirlpool, device: Meter17, enabled: false)];

        Assert.False(AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Chennai, Meter17).IsEnabled);
        Assert.True(AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Chennai, Meter18).IsEnabled);
    }

    [Fact]
    public void SelectedCompaniesOnly_AppliesWhereSwitchedOn()
    {
        _highVoltage.AppliesByDefault = false;
        AlertRuleOverride[] changes = [Change(Whirlpool, enabled: true)];

        Assert.True(AlertRuleResolution.Resolve(_highVoltage, changes, Whirlpool, Chennai, Meter17).IsEnabled);
        Assert.False(AlertRuleResolution.Resolve(_highVoltage, changes, Abc, null, null).IsEnabled);
    }

    [Fact]
    public void GlobalSwitchOff_WinsOverEveryChange()
    {
        _highVoltage.IsEnabled = false;

        var e = AlertRuleResolution.Resolve(_highVoltage, [Change(Whirlpool, device: Meter17, enabled: true)], Whirlpool, Chennai, Meter17);

        Assert.False(e.IsEnabled);
        Assert.Equal(AlertRuleLevel.Global, e.EnabledFrom);
    }

    [Fact]
    public void AsAlertRule_KeepsTheGlobalRulesIdSoCooldownsWork()
    {
        var rule = AlertRuleResolution.ToAlertRule(
            AlertRuleResolution.Resolve(_highVoltage, [Change(Whirlpool, threshold: 245)], Whirlpool, null, null), Whirlpool);

        Assert.Equal(_highVoltage.Id, rule.Id);
        Assert.Equal(Whirlpool, rule.TenantId);
        Assert.Equal(245, rule.Threshold);
        Assert.Equal("voltage", rule.Metric);
    }
}
