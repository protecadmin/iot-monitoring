using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// Configures one derived parameter: which source parameter it reads, how it
/// derives a value, and what that output is called.
///
/// Scope narrows by every field that is set — a rule with both SiteId and
/// DeviceTypeId applies only to devices of that type at that site; one with
/// none applies to every device in the tenant.
///
/// A rule never alters the raw telemetry it reads. The raw Totalizer stays
/// exactly as the device reported it; the Energy Consumption derived from it
/// lives in <see cref="CalculationState"/> (latest value) and is computed from
/// raw history on read for everything else.
/// </summary>
public class CalculationRule : TenantScopedEntity
{
    public Guid? SiteId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? DeviceTypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Stable identifier, unique within the tenant (e.g. "energy-consumption").</summary>
    public string Key { get; set; } = string.Empty;

    public CalculationType Type { get; set; }

    /// <summary>The Device Type parameter key this rule reads (e.g. "totalizer"). Empty for a Formula, which reads the parameters its <see cref="Expression"/> names.</summary>
    public string SourceParameterKey { get; set; } = string.Empty;

    /// <summary>
    /// A Formula rule's arithmetic over parameters of the same reading, e.g.
    /// "voltage * current * powerFactor / 1000" (see
    /// Bms.Domain.Calculations.FormulaExpression). Null for every other type.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>Key the derived value is published under (e.g. "energyConsumption").</summary>
    public string OutputKey { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;
    public string? OutputUnit { get; set; }

    /// <summary>
    /// Largest delta accepted between two consecutive readings. A bigger jump is
    /// treated like a reset — typically a replacement meter that starts at a
    /// higher reading than the old one — instead of being counted as consumption.
    /// Null accepts any non-negative delta.
    /// </summary>
    public double? MaxDelta { get; set; }

    /// <summary>
    /// The averaging period, in seconds, for a windowed type (Time Weighted
    /// Average, Equivalent Continuous Level). Windows are aligned to the Unix
    /// epoch, the same way Flux aggregateWindow aligns them, so the live value
    /// and the history of it describe exactly the same periods. Null for every
    /// other type.
    /// </summary>
    public int? WindowSeconds { get; set; }

    /// <summary>
    /// Automatic rules apply to every device their scope matches; Selected rules
    /// apply only to devices they were picked for (<see cref="DeviceCalculationRule"/>).
    /// Either way the scope must match and the source must be eligible.
    /// </summary>
    public CalculationAssignmentMode AssignmentMode { get; set; } = CalculationAssignmentMode.Automatic;

    public bool IsEnabled { get; set; } = true;
}
