using System.Text.Json;

namespace Bms.DeviceSimulator;

/// <summary>
/// The one payload shape BMS ingests: what our own gateway publishes — flat
/// JSON keyed by BMS parameter keys, plus a sequence number.
/// </summary>
public static class PayloadEncoders
{
    public static byte[] Gateway(DeviceProfile profile, TelemetryState s)
    {
        var body = new Dictionary<string, object>();
        foreach (var key in profile.ParameterKeys)
        {
            if (s.Read(key) is { } value)
                // A cumulative counter climbs by a fraction per tick; 2 decimals would flatten it.
                body[key] = Math.Round(value, key == "energy" ? 4 : 2);
        }
        body["seq"] = s.Sequence;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static byte[] MalformedJson() => "{ this is not valid json"u8.ToArray();
}
