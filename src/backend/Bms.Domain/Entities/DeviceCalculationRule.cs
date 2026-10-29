using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// A Selected-mode Calculation Rule chosen for one device, from the device form.
/// Automatic rules never need one of these — their scope alone decides.
/// </summary>
public class DeviceCalculationRule : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public Guid CalculationRuleId { get; set; }

    public CalculationRule? CalculationRule { get; set; }
}
