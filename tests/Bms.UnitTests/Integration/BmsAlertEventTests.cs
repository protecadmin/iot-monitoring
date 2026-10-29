using Bms.Application.Abstractions.Notifications;
using Bms.Domain;
using Bms.Domain.Entities;
using Xunit;

namespace Bms.UnitTests.Integration;

/// <summary>
/// The event name is what the maintenance system writes policy against
/// ("HIGH_TEMPERATURE raises work, NORMAL_TEMPERATURE does not"), so it has to
/// be stable and derived from the rule rather than from whatever the reading
/// happened to be.
/// </summary>
public class BmsAlertEventTests
{
    private static Alert AlertWith(string parameter, double? value = 85, double? threshold = 80) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Parameter = parameter,
        CurrentValue = value,
        Threshold = threshold,
        Severity = AlertSeverity.Critical,
        Message = "breached"
    };

    private static AlertRule RuleWith(AlertRuleOperator op) => new() { Operator = op, Metric = "temperature" };

    [Theory]
    [InlineData(AlertRuleOperator.GreaterThan, "HIGH_TEMPERATURE")]
    [InlineData(AlertRuleOperator.GreaterThanOrEqual, "HIGH_TEMPERATURE")]
    [InlineData(AlertRuleOperator.LessThan, "LOW_TEMPERATURE")]
    [InlineData(AlertRuleOperator.LessThanOrEqual, "LOW_TEMPERATURE")]
    [InlineData(AlertRuleOperator.Equal, "EXACT_TEMPERATURE")]
    public void DeriveEventType_UsesTheRuleOperatorForDirection(AlertRuleOperator op, string expected)
    {
        Assert.Equal(expected, BmsAlertEvent.DeriveEventType(AlertWith("temperature"), RuleWith(op)));
    }

    [Fact]
    public void DeriveEventType_BoundaryBreachOfALessThanOrEqualRule_IsStillLow()
    {
        // value == threshold: comparing the numbers would say HIGH, which
        // would be the wrong policy bucket. The operator decides.
        var alert = AlertWith("pressure", value: 2.0, threshold: 2.0);
        Assert.Equal("LOW_PRESSURE", BmsAlertEvent.DeriveEventType(alert, RuleWith(AlertRuleOperator.LessThanOrEqual)));
    }

    [Theory]
    [InlineData("power factor", "HIGH_POWER_FACTOR")]
    [InlineData("kWh.total", "HIGH_KWH_TOTAL")]
    [InlineData("", "HIGH_UNKNOWN")]
    public void DeriveEventType_NormalisesTheParameterIntoAStableKey(string parameter, string expected)
    {
        Assert.Equal(expected, BmsAlertEvent.DeriveEventType(AlertWith(parameter), RuleWith(AlertRuleOperator.GreaterThan)));
    }

    [Fact]
    public void DeriveEventType_RuleDeleted_FallsBackToTheRecordedValues()
    {
        Assert.Equal("HIGH_TEMPERATURE", BmsAlertEvent.DeriveEventType(AlertWith("temperature", 95, 80), rule: null));
        Assert.Equal("LOW_TEMPERATURE", BmsAlertEvent.DeriveEventType(AlertWith("temperature", 10, 80), rule: null));
        Assert.Equal("ALERT_TEMPERATURE", BmsAlertEvent.DeriveEventType(AlertWith("temperature", null, null), rule: null));
    }

    [Fact]
    public void From_CarriesTheReasonButNoTelemetrySeries()
    {
        var alert = AlertWith("temperature", 85.4, 80);
        var device = new Device { Id = Guid.NewGuid(), SiteId = Guid.NewGuid(), DeviceCode = "device-001" };

        var evt = BmsAlertEvent.From(alert, device, RuleWith(AlertRuleOperator.GreaterThan));

        Assert.Equal("BMS", evt.SourceSystem);
        Assert.Equal(alert.Id.ToString(), evt.SourceEventId);
        Assert.Equal("HIGH_TEMPERATURE", evt.EventType);
        Assert.Equal(device.Id.ToString(), evt.BmsDeviceId);
        Assert.Equal("device-001", evt.BmsDeviceCode);
        Assert.Equal(85.4, evt.Value);
        Assert.Equal(80, evt.Threshold);

        // The contract is a single reading plus its threshold — there is
        // nowhere in it for a series to travel.
        Assert.DoesNotContain(
            typeof(BmsAlertEvent).GetProperties().Select(p => p.PropertyType),
            t => t.IsArray || (t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && t != typeof(string)));
    }
}
