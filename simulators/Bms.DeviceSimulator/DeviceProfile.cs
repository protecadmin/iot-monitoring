namespace Bms.DeviceSimulator;

/// <summary>
/// Mirrors Bms.Infrastructure.Persistence.DbSeeder: each simulated device
/// publishes over MQTT the way our own gateway does — JSON keyed by the BMS
/// parameter keys of its device type.
/// </summary>
public sealed record DeviceProfile(string DeviceCode, string TenantSlug, string SiteCode, IReadOnlyList<string> ParameterKeys)
{
    public static readonly IReadOnlyList<DeviceProfile> All =
    [
        new("device-001", "whirlpool", "chennai-factory", ["temperature", "humidity"]),
        new("device-005", "whirlpool", "pune-factory", ["pressure", "temperature"]),
        new("device-006", "abc-manufacturing", "bangalore-factory", ["temperature", "vibration"]),
        new("device-008", "abc-manufacturing", "bangalore-factory", ["temperature", "humidity"]),
        new("device-010", "demo-company", "demo-site", ["waterLevel", "temperature"]),
        new("device-012", "abc-manufacturing", "bangalore-factory", ["wbgt", "dryBulb", "wetBulb", "globeTemp", "humidity"]),
        new("device-013", "abc-manufacturing", "bangalore-factory", ["noiseLevel", "noisePeak"]),
        new("device-014", "whirlpool", "chennai-factory", ["temperature", "vibration"]),
        new("device-015", "whirlpool", "chennai-factory", ["wbgt", "dryBulb", "wetBulb", "globeTemp", "humidity"]),
        new("device-016", "whirlpool", "chennai-factory", ["noiseLevel", "noisePeak"]),
        new("device-017", "whirlpool", "chennai-factory", ["current", "voltage", "powerFactor", "energy"])
    ];

    public static DeviceProfile? FindByCode(string code) => All.FirstOrDefault(d => d.DeviceCode == code);
}
