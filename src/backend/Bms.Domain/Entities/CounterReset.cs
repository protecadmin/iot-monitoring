using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// A point from which a device's counter parameter is measured on a new basis
/// — e.g. its meter was replaced or re-scaled. Consumption is never computed across it: the first
/// reading after <see cref="EffectiveAt"/> only becomes the new baseline, both
/// in live calculation and when history is re-derived, so a changed factor
/// can't show up as a spike (e.g. 45 → 4500 kWh) in energy consumption.
/// </summary>
public class CounterReset : Entity
{
    public Guid DeviceId { get; set; }
    public string ParameterKey { get; set; } = string.Empty;
    public DateTimeOffset EffectiveAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}
