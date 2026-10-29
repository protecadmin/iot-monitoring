namespace Bms.Domain;

public enum TransportType
{
    Mqtt,
    Tcp,
    Http,
    BacnetIp
}

public enum ProtocolType
{
    Mqtt,
    ProprietaryTcp,
    Bacnet,
    Http
}

public enum PayloadFormat
{
    Json,
    Hex,
    Binary,
    Proprietary,
    Bacnet
}

public enum DeviceStatus
{
    Registered,
    Provisioned,
    Online,
    Offline,
    Warning,
    Error,
    Maintenance,
    Decommissioned
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Open,
    Acknowledged,
    Resolved
}

/// <summary>
/// AlertRule.Metric is a free-text field so it can name any Device Type
/// parameter without a code change — these are the exceptions: lifecycle
/// conditions evaluated from device silence, not from a telemetry field, so
/// they need fixed, well-known names instead of a parameter key. Their
/// threshold is a duration in seconds. See DeviceLifecycleService.
/// </summary>
public static class LifecycleMetrics
{
    public const string DeviceOffline = "DeviceOffline";
    public const string NoTelemetry = "NoTelemetry";

    /// <summary>A device added but never heard from — an install that hasn't come up.</summary>
    public const string NeverConnected = "NeverConnected";

    public static readonly string[] All = [DeviceOffline, NoTelemetry, NeverConnected];

    public static bool IsLifecycle(string metric) => All.Contains(metric);
}

/// <summary>
/// Where a device's reading times come from. Devices that send no time of
/// their own (many 4G sensors) are stamped on arrival, which is shown as
/// "Received at" rather than as a measurement time.
/// </summary>
/// <summary>How a device's data reaches BMS.</summary>
public enum DeviceConnection
{
    /// <summary>The device (or its gateway) publishes BMS-format JSON over MQTT itself.</summary>
    MqttDirect,

    /// <summary>The device sends its own format to a connector, which translates it (see Connector).</summary>
    Connector
}

public enum TimeSource
{
    /// <summary>The device sends the time it measured.</summary>
    Device,

    /// <summary>The time the reading arrived; the device sends none.</summary>
    Received
}

public enum AlertRuleOperator
{
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Equal
}

public enum CommandType
{
    Restart,
    RequestTelemetry,
    ChangeReportingInterval,
    SynchronizeTime,
    UpdateConfiguration
}

public enum CommandStatus
{
    Pending,
    Sent,
    Acknowledged,
    Failed,
    Timeout
}

public enum FirmwareDeploymentStatus
{
    Pending,
    Downloading,
    Verifying,
    Installing,
    Rebooting,
    Success,
    Failed,
    Timeout,
    WaitingForDevice
}

public enum MessageProcessingStatus
{
    Received,
    Decoded,
    Mapped,
    Normalized,
    Validated,
    Persisted,
    Rejected
}

public enum SystemRole
{
    SuperAdmin,
    Admin,
    Engineer,
    Viewer,
    Customer
}

/// <summary>
/// What a Device Type parameter's value means over time. Declared, never
/// inferred from the unit: a kWh value can be an instantaneous register as
/// easily as a cumulative meter count, so a parameter only counts as a
/// totalizer when it is marked <see cref="Counter"/>.
/// </summary>
/// <summary>
/// How a Device Type parameter is shown on the dashboard. Set per device
/// type, so every company's devices of that type get the same widget —
/// adding a meter type needs a setting, not code.
/// </summary>
public enum DashboardWidget
{
    /// <summary>A quantity used over time (kWh, m³, litres): today, this month, daily bars — totalled per device type.</summary>
    Consumption,

    /// <summary>A live reading (kW, m³/h, °C): the value and its trend, one card per device.</summary>
    LiveValue,

    /// <summary>A 0–100 % level (tank, load), one card per device.</summary>
    LevelGauge,

    /// <summary>A reading with alert limits (vibration, noise, WBGT): Good / Caution / Danger, one card per device.</summary>
    SafetyStatus,

    /// <summary>An on/off state (pump running, door open).</summary>
    OnOff
}

public enum ParameterKind
{
    /// <summary>A reading of the current state — voltage, temperature, flow rate.</summary>
    Instantaneous,

    /// <summary>A cumulative count that only grows between resets — energy, water or fuel totalizer, runtime hours.</summary>
    Counter,

    /// <summary>An on/off or discrete state — run status, door contact.</summary>
    Digital,

    /// <summary>Produced by the Calculations module from other parameters. Never accepted from a device.</summary>
    Derived
}

/// <summary>
/// How a Calculation Rule derives its output. Each value is served by exactly
/// one ICalculationHandler in Bms.Calculations, so adding a calculation type
/// means adding a handler — not changing ingestion, the engine or the API.
/// </summary>
public enum CalculationType
{
    /// <summary>Consumption between consecutive valid readings of a Counter parameter.</summary>
    TotalizerDelta,

    /// <summary>An arithmetic formula over parameters of the same reading — e.g. active power from voltage, current and power factor.</summary>
    Formula,

    /// <summary>
    /// The arithmetic mean of a level over a fixed window — a WBGT or dust
    /// average, where compliance is judged on a sustained reading rather than
    /// a single sample.
    /// </summary>
    TimeWeightedAverage,

    /// <summary>
    /// Leq: the equivalent continuous sound level over a fixed window. Decibels
    /// are logarithmic, so this is the mean of the sound energy expressed back
    /// in dB — never the arithmetic mean of the readings.
    /// </summary>
    EquivalentContinuousLevel,

    /// <summary>
    /// The reading minus the device's baseline for that parameter (see
    /// DeviceBaseline) — movement since installation, which is what tilt and
    /// settlement limits are written against. No baseline, no value.
    /// </summary>
    BaselineDelta
}

/// <summary>How a Calculation Rule reaches devices.</summary>
public enum CalculationAssignmentMode
{
    /// <summary>Every device matching the rule's scope — including devices created later. Nothing to select per device.</summary>
    Automatic,

    /// <summary>Only devices it has been selected on in the device form. Scope still limits which devices can select it.</summary>
    Selected
}
