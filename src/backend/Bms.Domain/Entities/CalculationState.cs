using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// The last reading a rule accepted for one device, and the value it produced.
///
/// This is what makes live calculation incremental — each new reading is
/// compared with LastSourceValue instead of re-reading history — and it is the
/// double-count guard: the row only advances through a compare-and-swap on
/// LastSourceTimestamp, so two messages racing for the same device can never
/// both publish a delta from the same previous reading.
/// </summary>
public class CalculationState : TenantScopedEntity
{
    public Guid CalculationRuleId { get; set; }
    public Guid DeviceId { get; set; }

    public double LastSourceValue { get; set; }
    public DateTimeOffset LastSourceTimestamp { get; set; }

    /// <summary>Null until a reading produces a value — the first reading only sets the baseline.</summary>
    public double? LastOutputValue { get; set; }
    public DateTimeOffset? LastOutputTimestamp { get; set; }

    public CalculationRule? CalculationRule { get; set; }
}
