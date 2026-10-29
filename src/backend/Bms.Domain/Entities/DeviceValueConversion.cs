namespace Bms.Domain.Entities;

/// <summary>
/// Corrects one parameter of an MQTT-direct device before it is stored:
/// value = (raw - Offset) * Scale — e.g. millidegrees to degrees (Scale 0.001)
/// or °F to °C (Offset 32, Scale 0.5556). Kept under the same key, so the rest
/// of BMS only ever sees the corrected value. Per device, because a wrong unit
/// is one model's problem, not its device type's. A connector device converts
/// in its connector's mapping instead (the same formula), never here.
/// </summary>
public class DeviceValueConversion
{
    public string ParameterKey { get; set; } = string.Empty;
    public double Offset { get; set; }
    public double Scale { get; set; } = 1;

    /// <summary>
    /// Applies <paramref name="device"/>'s conversions to a reading's fields,
    /// in place. Only MQTT-direct devices are converted; a field the reading
    /// doesn't carry is left alone.
    /// </summary>
    public static void Apply(Device device, IDictionary<string, double> fields)
    {
        if (device.Connection != DeviceConnection.MqttDirect)
            return;

        foreach (var conversion in device.ValueConversions)
        {
            var key = fields.Keys.FirstOrDefault(k => string.Equals(k, conversion.ParameterKey, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
                fields[key] = (fields[key] - conversion.Offset) * conversion.Scale;
        }
    }
}
