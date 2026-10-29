namespace Bms.DeviceSimulator;

public sealed class SimulatorOptions
{
    public string? DeviceId { get; init; }
    public int Count { get; init; } = 1;
    public int IntervalSeconds { get; init; } = 5;
    public int HeartbeatIntervalSeconds { get; init; } = 30;

    public string MqttHost { get; init; } = "localhost";
    public int MqttPort { get; init; } = 1883;

    // No single username here on purpose — MqttDeviceRunner authenticates
    // as the device's own tenant slug (see infrastructure/mosquitto/acl),
    // so a device can never be configured to publish under a tenant it
    // doesn't belong to. The password is still shared across tenants for
    // local dev convenience; a real deployment would set per-tenant
    // passwords too.
    public string MqttPassword { get; init; } = "devicepassword";

    public double MalformedRate { get; init; }
    public double DuplicateRate { get; init; }
    public int DelayMs { get; init; }

    public static SimulatorOptions Parse(string[] args)
    {
        string? Get(string name) => args
            .Select(a => a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase) ? a[(name.Length + 3)..] : null)
            .FirstOrDefault(v => v is not null);

        int GetInt(string name, int fallback) => int.TryParse(Get(name), out var v) ? v : fallback;
        double GetDouble(string name, double fallback) => double.TryParse(Get(name), out var v) ? v : fallback;
        string GetString(string name, string fallback) => Get(name) ?? fallback;

        return new SimulatorOptions
        {
            DeviceId = Get("device-id"),
            Count = GetInt("count", Get("device-id") is null ? 1 : 1),
            IntervalSeconds = GetInt("interval", 5),
            HeartbeatIntervalSeconds = GetInt("heartbeat-interval", 30),
            MqttHost = GetString("mqtt-host", "localhost"),
            MqttPort = GetInt("mqtt-port", 1883),
            MqttPassword = GetString("mqtt-password", "devicepassword"),
            MalformedRate = GetDouble("malformed-rate", 0),
            DuplicateRate = GetDouble("duplicate-rate", 0),
            DelayMs = GetInt("delay-ms", 0)
        };
    }
}
