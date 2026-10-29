using Bms.Domain.Common;

namespace Bms.Domain.Entities;

public class DeviceType : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// How often devices of this type report, if it is slower than the
    /// platform's offline window — e.g. a 4G sensor that wakes every 5
    /// minutes. A device then only counts as offline, and silence alerts only
    /// fire, after it has missed two reports. Null: the platform defaults.
    /// </summary>
    public int? ExpectedIntervalSeconds { get; set; }

    /// <summary>False for devices that can't receive commands (e.g. sensors with no downlink); the command API refuses them.</summary>
    public bool SupportsCommands { get; set; } = true;

    public ICollection<DeviceTypeParameter> Parameters { get; set; } = new List<DeviceTypeParameter>();
}

/// <summary>
/// A telemetry field a device of this type is expected to report (e.g.
/// "current" on an Energy Meter). This list is also the allow-list the
/// ingestion pipeline filters incoming fields against — a device sending
/// fields not named here has those extra fields silently dropped rather
/// than stored, so a device type only ever accumulates the parameters it
/// was deliberately given. See docs/protocols/normalization.md.
/// </summary>
public class DeviceTypeParameter : Entity
{
    public Guid DeviceTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }

    /// <summary>What the value means over time. See <see cref="ParameterKind"/> for why this is declared rather than inferred from the unit.</summary>
    public ParameterKind Kind { get; set; } = ParameterKind.Instantaneous;

    /// <summary>
    /// How a Derived parameter is calculated, for every tenant: device types
    /// are shared, so this is the one definition each tenant's device-type-wide
    /// calculation rule is kept in step with (see DerivedParameterCalculations).
    /// Null when the parameter isn't Derived or has no calculation yet.
    /// </summary>
    public CalculationType? CalculationType { get; set; }

    /// <summary>The formula, for a Formula calculation.</summary>
    public string? CalculationExpression { get; set; }

    /// <summary>The counter parameter read, for a Totalizer Delta calculation.</summary>
    public string? CalculationSourceKey { get; set; }

    /// <summary>Largest accepted delta, for a Totalizer Delta calculation.</summary>
    public double? CalculationMaxDelta { get; set; }

    /// <summary>The averaging period in seconds, for a windowed calculation (see <see cref="CalculationRule.WindowSeconds"/>).</summary>
    public int? CalculationWindowSeconds { get; set; }

    /// <summary>How the dashboard shows this parameter, for every company. Null: not on the dashboard.</summary>
    public DashboardWidget? DashboardWidget { get; set; }

    public DeviceType? DeviceType { get; set; }
}
