using Bms.Domain.Entities;

namespace Bms.Calculations;

/// <summary>
/// A rule applies to a device when it is in the same tenant and every scope
/// field the rule sets matches. Kept identical to the scope part of the database
/// query in CalculationRuleRepository.GetApplicableRulesAsync. Scope alone does not
/// make a Selected-mode rule apply — the device must also have selected it.
/// </summary>
public static class CalculationScope
{
    public static bool Matches(CalculationRule rule, Device device) =>
        rule.TenantId == device.TenantId &&
        (rule.SiteId is null || rule.SiteId == device.SiteId) &&
        (rule.DeviceId is null || rule.DeviceId == device.Id) &&
        (rule.DeviceTypeId is null || rule.DeviceTypeId == device.DeviceTypeId);

    public static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a device type can feed this rule: it defines every parameter
    /// the rule reads, each of a Kind the handler accepts. Decided by declared
    /// Kind, never by unit.
    /// </summary>
    public static bool IsEligible(ICalculationHandler handler, CalculationRule rule, IEnumerable<DeviceTypeParameter> parameters)
    {
        var inputs = handler.InputKeys(rule);
        var list = parameters as IReadOnlyCollection<DeviceTypeParameter> ?? parameters.ToList();
        return inputs.Count > 0 && inputs.All(key => list.Any(p => KeyEquals(p.Key, key) && handler.IsEligibleSource(p)));
    }
}
