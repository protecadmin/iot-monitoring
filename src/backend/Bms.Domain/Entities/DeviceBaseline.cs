using Bms.Domain.Common;

namespace Bms.Domain.Entities;

/// <summary>
/// A device's reference reading for one parameter — typically taken at
/// installation, so movement is measured from where the structure started
/// (a tilt meter's 22.21° is normal; 0.4° on top of it is the news).
/// Read by "Change from baseline" calculations.
/// </summary>
public class DeviceBaseline : TenantScopedEntity
{
    public Guid DeviceId { get; set; }
    public string ParameterKey { get; set; } = string.Empty;
    public double Value { get; set; }

    /// <summary>When the reference reading was taken (the reading's own time).</summary>
    public DateTimeOffset ReadingAt { get; set; }
    public Guid? SetByUserId { get; set; }
}
