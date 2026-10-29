namespace Bms.DeviceSimulator;

/// <summary>
/// A small random walk per device, not pure noise — core.txt section 23
/// asks for "realistic rather than purely random" telemetry. Each tick
/// nudges the previous value by a small bounded delta instead of
/// resampling independently every time.
/// </summary>
public sealed class TelemetryState
{
    private readonly Random _random;

    public double Temperature { get; private set; }
    public double Humidity { get; private set; }
    public double Pressure { get; private set; }
    public double Vibration { get; private set; }
    public double Energy { get; private set; }
    public double Battery { get; private set; }
    public double SignalStrength { get; private set; }

    // Energy Meter readings.
    public double Current { get; private set; }
    public double Voltage { get; private set; }
    public double PowerFactor { get; private set; }

    public double WaterLevel { get; private set; }

    // Heat-stress readings (WBGT Sensor). Dry bulb and radiant load walk independently;
    // wet bulb and WBGT are derived from them rather than walked, so the
    // four values stay physically consistent with each other.
    public double DryBulb { get; private set; }
    public double RadiantLoad { get; private set; }
    public double WetBulb => PsychrometricWetBulb(DryBulb, Humidity) + 0.5; // natural wet bulb runs slightly above psychrometric
    public double GlobeTemp => DryBulb + RadiantLoad;
    public double Wbgt => 0.7 * WetBulb + 0.3 * GlobeTemp; // ISO 7243, indoor / no solar load

    // Sound-level readings (Noise Sensor).
    public double NoiseLevel { get; private set; }
    public double NoisePeakMargin { get; private set; }
    public double NoisePeak => Math.Min(160, NoiseLevel + NoisePeakMargin);

    public long Sequence { get; private set; }

    public TelemetryState(int seed)
    {
        _random = new Random(seed);

        // A real device's sequence counter doesn't reset to 0 every time
        // it (re)connects — it's either persisted across reboots or
        // derived from a clock, so it keeps climbing. Seeding from the
        // current time means restarting this simulator won't replay
        // sequence numbers a previous run already sent, which the
        // backend's dedup check (correctly) would otherwise reject as
        // duplicates of messages from that earlier run still sitting in
        // the database. See docs/architecture/data-flow.md on dedup.
        Sequence = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Temperature = 22 + _random.NextDouble() * 6;
        Humidity = 45 + _random.NextDouble() * 20;
        Pressure = 1000 + _random.NextDouble() * 20;
        // mm/s RMS; starts in ISO 10816 zone A/B and can drift past the
        // 4.5 / 7.1 mm/s alert thresholds seeded in DbSeeder.
        Vibration = 1.5 + _random.NextDouble() * 2;
        Energy = _random.NextDouble() * 5;
        Battery = 80 + _random.NextDouble() * 20;
        SignalStrength = -55 + _random.NextDouble() * 15;

        Current = 5 + _random.NextDouble() * 15;
        Voltage = 220 + _random.NextDouble() * 20;
        PowerFactor = 0.85 + _random.NextDouble() * 0.15;

        WaterLevel = 80 + _random.NextDouble() * 80;

        DryBulb = 29 + _random.NextDouble() * 5;
        RadiantLoad = 3 + _random.NextDouble() * 4;

        NoiseLevel = 75 + _random.NextDouble() * 10;
        NoisePeakMargin = 18 + _random.NextDouble() * 8;
    }

    public void Tick()
    {
        Sequence++;
        Temperature = Clamp(Temperature + Delta(0.6), -10, 60);
        Humidity = Clamp(Humidity + Delta(1.5), 10, 100);
        Pressure = Clamp(Pressure + Delta(0.8), 950, 1050);
        Vibration = Clamp(Vibration + Delta(0.3), 0.2, 11);
        Energy += Math.Abs(Delta(0.3)); // monotonically increasing counter, like a real energy meter
        Battery = Clamp(Battery - Math.Abs(Delta(0.02)), 0, 100); // slowly drains
        SignalStrength = Clamp(SignalStrength + Delta(2), -100, -30);

        Current = Clamp(Current + Delta(0.8), 0, 50);
        Voltage = Clamp(Voltage + Delta(1.5), 180, 260);
        PowerFactor = Clamp(PowerFactor + Delta(0.02), 0, 1);

        WaterLevel = Clamp(WaterLevel + Delta(1), 0, 500);

        DryBulb = Clamp(DryBulb + Delta(0.4), 20, 45);
        RadiantLoad = Clamp(RadiantLoad + Delta(0.5), 0, 12);

        NoiseLevel = Clamp(NoiseLevel + Delta(1.5), 55, 105);
        NoisePeakMargin = Clamp(NoisePeakMargin + Delta(1.5), 10, 35);
    }

    /// <summary>The current value of a BMS parameter key, or null when this simulator doesn't model it.</summary>
    public double? Read(string key) => key switch
    {
        "temperature" => Temperature,
        "humidity" => Humidity,
        "pressure" => Pressure,
        "vibration" => Vibration,
        "energy" => Energy,
        "battery" => Battery,
        "current" => Current,
        "voltage" => Voltage,
        "powerFactor" => PowerFactor,
        "waterLevel" => WaterLevel,
        "wbgt" => Wbgt,
        "dryBulb" => DryBulb,
        "wetBulb" => WetBulb,
        "globeTemp" => GlobeTemp,
        "noiseLevel" => NoiseLevel,
        "noisePeak" => NoisePeak,
        _ => null
    };

    /// <summary>Occasionally injects an out-of-range value to exercise validation — see core.txt section 53.</summary>
    public void InjectInvalidSpike() => Temperature = 500;

    /// <summary>Stull (2011) wet-bulb approximation, °C from °C and %RH — good to ±1 °C over the range simulated here.</summary>
    private static double PsychrometricWetBulb(double t, double rh) =>
        t * Math.Atan(0.151977 * Math.Sqrt(rh + 8.313659))
        + Math.Atan(t + rh) - Math.Atan(rh - 1.676331)
        + 0.00391838 * Math.Pow(rh, 1.5) * Math.Atan(0.023101 * rh)
        - 4.686035;

    private double Delta(double magnitude) => (_random.NextDouble() - 0.5) * 2 * magnitude;
    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
}
