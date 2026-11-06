using Bms.Application.Abstractions.Security;
using Bms.Domain;
using Bms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Idempotent development seed data (core.txt section 56): three tenants,
/// several sites and demo devices of each device type. Devices report
/// through our own gateway in BMS parameter keys, so none has a payload
/// mapping.
/// </summary>
public static class DbSeeder
{
    // Shown under each type's name on the Device Types page. Existing
    // databases got the same text from the AddDeviceTypeDescriptions
    // migration — keep the two in step when editing one.
    private static readonly Dictionary<string, string> DeviceTypeDescriptions = new()
    {
        ["Temperature Sensor"] = "Ambient or process temperature measurement.",
        ["Humidity Sensor"] = "Relative humidity monitor with ambient temperature.",
        ["Pressure Sensor"] = "Barometric or process pressure with ambient conditions.",
        ["Vibration Sensor"] = "Machine vibration velocity for condition monitoring (ISO 10816).",
        ["Energy Meter"] = "Electrical energy, current, voltage and power factor metering.",
        ["Air Quality Sensor"] = "Indoor air quality monitor — CO2, PM2.5 and VOC.",
        ["Water Level Sensor"] = "Tank, sump or reservoir water level monitor.",
        ["Environmental Station"] = "Multi-sensor station for temperature, humidity and pressure.",
        ["Industrial Gateway"] = "Edge gateway that aggregates field devices and reports its own ambient conditions."
    };

    public static async Task SeedAsync(BmsDbContext db, IPasswordHasher hasher, ILogger logger)
    {
        if (await db.Tenants.AnyAsync())
        {
            logger.LogInformation("Seed data already present, skipping");
            await EnsureGlobalAlertRulesAsync(db, logger);
            await EnsureDemoAdditionsAsync(db, logger);
            return;
        }

        logger.LogInformation("Seeding development data...");

        var roles = await db.Roles.ToListAsync();
        foreach (var name in new[] { "SuperAdmin", "Executive", "CompanyAdmin", "SiteAdmin" })
            if (roles.All(r => r.Name != name)) { var role = new Role { Name = name }; roles.Add(role); db.Roles.Add(role); }
        await db.SaveChangesAsync();
        Role RoleByName(string name) => roles.First(r => r.Name == name);

        var deviceTypes = new[]
        {
            "Temperature Sensor", "Humidity Sensor", "Pressure Sensor", "Vibration Sensor",
            "Energy Meter", "Air Quality Sensor", "Water Level Sensor", "Environmental Station", "Industrial Gateway"
        }.Select(name => new DeviceType { Name = name, Description = DeviceTypeDescriptions[name] }).ToList();
        db.DeviceTypes.AddRange(deviceTypes);
        await db.SaveChangesAsync();
        DeviceType TypeByName(string name) => deviceTypes.First(t => t.Name == name);
        // WBGT Sensor and Noise Sensor are not in the list above: they come
        // from the AddWbgtAndNoiseDeviceTypes migration, which has already
        // run by now (migrations precede seeding) and is the one place they
        // are defined, for fresh and existing databases alike.

        // Each Device Type's parameter list doubles as the ingestion
        // allow-list (see TelemetryNormalizer) — a device only ever gets to
        // keep telemetry fields its type has defined here. Each type lists
        // only what that kind of equipment really reports: a parameter on the
        // wrong type (an Energy Meter "temperature") invites formulas and
        // alerts that can never get a value.
        void AddParams(string typeName, params (string Key, string Label, string? Unit, double? Min, double? Max)[] parameters)
        {
            var type = TypeByName(typeName);
            foreach (var p in parameters)
                db.DeviceTypeParameters.Add(new DeviceTypeParameter { DeviceTypeId = type.Id, Key = p.Key, Label = p.Label, Unit = p.Unit, MinValue = p.Min, MaxValue = p.Max });
        }

        AddParams("Temperature Sensor",
            ("temperature", "Temperature", "°C", -50, 150));
        AddParams("Humidity Sensor",
            ("humidity", "Humidity", "%", 0, 100),
            ("temperature", "Temperature", "°C", -50, 150));
        AddParams("Pressure Sensor",
            ("pressure", "Pressure", "hPa", 300, 1200),
            ("temperature", "Temperature", "°C", -50, 150));
        AddParams("Vibration Sensor",
            ("vibration", "Vibration", "mm/s", 0, 50),
            ("temperature", "Temperature", "°C", -50, 150));
        AddParams("Energy Meter",
            ("energy", "Energy", "kWh", 0, null),
            ("current", "Current", "A", 0, null),
            ("voltage", "Voltage", "V", 0, 500),
            ("powerFactor", "Power Factor", null, 0, 1));
        AddParams("Air Quality Sensor",
            ("co2", "CO2", "ppm", 0, 5000),
            ("pm25", "PM2.5", "µg/m³", 0, 1000),
            ("voc", "VOC", "ppb", 0, 5000),
            ("temperature", "Temperature", "°C", -50, 150),
            ("humidity", "Humidity", "%", 0, 100),
            ("battery", "Battery", "%", 0, 100));
        AddParams("Water Level Sensor",
            ("waterLevel", "Water Level", "cm", 0, null),
            ("temperature", "Temperature", "°C", -50, 150));
        AddParams("Environmental Station",
            ("temperature", "Temperature", "°C", -50, 150),
            ("humidity", "Humidity", "%", 0, 100),
            ("pressure", "Pressure", "hPa", 300, 1200));
        AddParams("Industrial Gateway",
            ("temperature", "Temperature", "°C", -50, 150),
            ("humidity", "Humidity", "%", 0, 100));
        await db.SaveChangesAsync();

        // Dashboard cards for the safety and condition readings, matching the
        // AddParameterDashboardWidget migration (which ran before these
        // parameters existed on a fresh database).
        foreach (var p in await db.DeviceTypeParameters.Where(p => p.DashboardWidget == null &&
                     (p.Key == "vibration" || p.Key == "noiseLevel" || p.Key == "wbgt")).ToListAsync())
            p.DashboardWidget = DashboardWidget.SafetyStatus;
        await db.SaveChangesAsync();

        var whirlpool = new Tenant { Name = "Whirlpool", Slug = "whirlpool" };
        var abc = new Tenant { Name = "ABC Manufacturing", Slug = "abc-manufacturing" };
        var demo = new Tenant { Name = "Demo Company", Slug = "demo-company" };
        db.Tenants.AddRange(whirlpool, abc, demo);
        await db.SaveChangesAsync();

        var chennai = new Site { TenantId = whirlpool.Id, Code = "chennai-factory", Name = "Chennai Factory", Latitude = 13.0827, Longitude = 80.2707 };
        var pune = new Site { TenantId = whirlpool.Id, Code = "pune-factory", Name = "Pune Factory", Latitude = 18.5204, Longitude = 73.8567 };
        var bangalore = new Site { TenantId = abc.Id, Code = "bangalore-factory", Name = "Bangalore Factory", Latitude = 12.9716, Longitude = 77.5946 };
        var demoSite = new Site { TenantId = demo.Id, Code = "demo-site", Name = "Demo Site", Latitude = 12.9, Longitude = 77.6 };
        db.Sites.AddRange(chennai, pune, bangalore, demoSite);
        await db.SaveChangesAsync();

        // Every device reports through our own gateway, which sends BMS
        // parameter keys (temperature, current, ...); fields are kept under
        // the names they arrive with (see TelemetryNormalizer).
        Device MakeDevice(Tenant tenant, Site site, string code, string name, DeviceType type) => new()
        {
            TenantId = tenant.Id,
            SiteId = site.Id,
            DeviceTypeId = type.Id,
            DeviceCode = code,
            Name = name,
            SerialNumber = $"SN-{code.ToUpperInvariant()}",
            Manufacturer = "Acme IoT",
            Model = "AX-100",
            Transport = TransportType.Mqtt,
            Protocol = ProtocolType.Mqtt,
            PayloadFormat = PayloadFormat.Json,
            Status = DeviceStatus.Registered,
            Latitude = site.Latitude,
            Longitude = site.Longitude
        };

        var devices = new List<Device>
        {
            MakeDevice(whirlpool, chennai, "device-001", "AHU-001 Supply Temp", TypeByName("Temperature Sensor")),
            MakeDevice(whirlpool, pune, "device-005", "PressureSensor-005", TypeByName("Pressure Sensor")),
            MakeDevice(abc, bangalore, "device-006", "VibrationSensor-006", TypeByName("Vibration Sensor")),
            MakeDevice(abc, bangalore, "device-008", "TemperatureSensor-008 (F)", TypeByName("Temperature Sensor")),
            MakeDevice(demo, demoSite, "device-010", "WaterLevel-010", TypeByName("Water Level Sensor")),
        };
        db.Devices.AddRange(devices);
        await db.SaveChangesAsync();

        await EnsureGlobalAlertRulesAsync(db, logger);

        const string devPassword = "Passw0rd!";
        db.Users.AddRange(
            new User { TenantId = null, RoleId = RoleByName("SuperAdmin").Id, Email = "superadmin@bms.local", DisplayName = "Super Admin", PasswordHash = hasher.Hash(devPassword) },
            new User { TenantId = whirlpool.Id, RoleId = RoleByName("CompanyAdmin").Id, Email = "admin@whirlpool.example", DisplayName = "Whirlpool Company Admin", PasswordHash = hasher.Hash(devPassword) },
            new User { TenantId = abc.Id, RoleId = RoleByName("CompanyAdmin").Id, Email = "admin@abc-manufacturing.example", DisplayName = "ABC Company Admin", PasswordHash = hasher.Hash(devPassword) },
            new User { TenantId = null, RoleId = RoleByName("Executive").Id, Email = "executive@bms.local", DisplayName = "Pro-Tec Executive", PasswordHash = hasher.Hash(devPassword) });
        await db.SaveChangesAsync();

        await EnsureDemoAdditionsAsync(db, logger);

        logger.LogInformation("Seed complete: 3 tenants, 4 sites, {DeviceCount} devices. Dev password for all seeded users: {Password}", await db.Devices.CountAsync(), devPassword);
    }

    /// <summary>
    /// The standard alert rules, defined once for every company by device type
    /// (see GlobalAlertRule). Only when there are none yet — a fresh database,
    /// or one whose rules the AddGlobalAlertRules migration had nothing to
    /// convert — so a rule deliberately deleted doesn't come back on restart.
    ///
    /// Occupational thresholds: WBGT per ISO 7243 / ACGIH heat-stress guidance
    /// for moderate work, noise at the 85 dB(A) action level and 90 dB(A)
    /// exposure limit, vibration at the ISO 10816 class II zone B/C (4.5 mm/s)
    /// and C/D (7.1 mm/s) boundaries.
    /// </summary>
    private static async Task EnsureGlobalAlertRulesAsync(BmsDbContext db, ILogger logger)
    {
        if (await db.GlobalAlertRules.AnyAsync())
            return;

        var standard = new (string Name, string Metric, AlertRuleOperator Op, double Threshold, AlertSeverity Severity, int Cooldown)[]
        {
            ("High Temperature", "temperature", AlertRuleOperator.GreaterThan, 50, AlertSeverity.Warning, 300),
            ("High Humidity", "humidity", AlertRuleOperator.GreaterThan, 90, AlertSeverity.Warning, 300),
            ("Low Pressure", "pressure", AlertRuleOperator.LessThan, 900, AlertSeverity.Critical, 300),
            ("Low Battery", "battery", AlertRuleOperator.LessThan, 20, AlertSeverity.Warning, 600),
            ("High WBGT", "wbgt", AlertRuleOperator.GreaterThan, 30, AlertSeverity.Warning, 300),
            ("Extreme Heat Stress (WBGT)", "wbgt", AlertRuleOperator.GreaterThan, 32, AlertSeverity.Critical, 300),
            ("High Noise", "noiseLevel", AlertRuleOperator.GreaterThan, 85, AlertSeverity.Warning, 300),
            ("Excessive Noise", "noiseLevel", AlertRuleOperator.GreaterThan, 90, AlertSeverity.Critical, 300),
            ("High Vibration", "vibration", AlertRuleOperator.GreaterThan, 4.5, AlertSeverity.Warning, 300),
            ("Excessive Vibration", "vibration", AlertRuleOperator.GreaterThan, 7.1, AlertSeverity.Critical, 300),
            ("Device Offline", LifecycleMetrics.DeviceOffline, AlertRuleOperator.GreaterThan, 300, AlertSeverity.Critical, 600),
            ("No Telemetry", LifecycleMetrics.NoTelemetry, AlertRuleOperator.GreaterThan, 120, AlertSeverity.Warning, 600),
            ("Never Connected", LifecycleMetrics.NeverConnected, AlertRuleOperator.GreaterThan, 86400, AlertSeverity.Warning, 3600)
        };

        // One rule per device type that has the metric; the lifecycle
        // conditions are one rule for every type.
        var parameters = await db.DeviceTypeParameters.AsNoTracking().ToListAsync();
        foreach (var (name, metric, op, threshold, severity, cooldown) in standard)
        {
            var isLifecycle = LifecycleMetrics.IsLifecycle(metric);
            var typeIds = isLifecycle
                ? [null]
                : parameters.Where(p => string.Equals(p.Key, metric, StringComparison.OrdinalIgnoreCase)).Select(p => (Guid?)p.DeviceTypeId).Distinct().ToList();
            foreach (var typeId in typeIds)
            {
                db.GlobalAlertRules.Add(new GlobalAlertRule
                {
                    Name = name, DeviceTypeId = typeId, Metric = metric, Operator = op, Threshold = threshold,
                    Severity = severity, CooldownSeconds = cooldown, IsEnabled = true, AppliesByDefault = true
                });
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Standard global alert rules created");
    }

    /// <summary>
    /// Demo data added after the original seed. SeedAsync skips an existing
    /// database entirely, so without this an already-seeded dev database
    /// would never get these devices and rules — and the simulator
    /// would send telemetry for device codes the backend has never heard of.
    ///
    /// Runs on every startup and is idempotent: each piece is looked up by
    /// its natural key (device code, rule name) and only
    /// created when missing, so nothing a user has since edited is touched.
    /// A database without the demo tenants is left alone.
    /// </summary>
    private static async Task EnsureDemoAdditionsAsync(BmsDbContext db, ILogger logger)
    {
        var added = new List<string>();

        async Task EnsureDevice(string tenantSlug, string siteCode, string code, string name, string typeName)
        {
            if (await db.Devices.AnyAsync(d => d.DeviceCode == code)) return;
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantSlug);
            var site = tenant is null ? null : await db.Sites.FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Code == siteCode);
            var type = await db.DeviceTypes.FirstOrDefaultAsync(t => t.Name == typeName);
            if (tenant is null || site is null || type is null) return; // demo tenant/site or catalogue type gone — nothing sensible to attach to

            db.Devices.Add(new Device
            {
                TenantId = tenant.Id,
                SiteId = site.Id,
                DeviceTypeId = type.Id,
                DeviceCode = code,
                Name = name,
                SerialNumber = $"SN-{code.ToUpperInvariant()}",
                Manufacturer = "Acme IoT",
                Model = "AX-100",
                Transport = TransportType.Mqtt,
                Protocol = ProtocolType.Mqtt,
                PayloadFormat = PayloadFormat.Json,
                Status = DeviceStatus.Registered,
                Latitude = site.Latitude,
                Longitude = site.Longitude
            });
            added.Add($"device {code}");
        }

        // Only a demo database gets demo devices. Like the rest, they report
        // through our gateway in BMS parameter keys, so none needs a mapping.
        if (await db.Tenants.AnyAsync(t => t.Slug == "abc-manufacturing" || t.Slug == "whirlpool"))
        {
            await EnsureDevice("whirlpool", "chennai-factory", "device-017", "EnergyMeter-017 (MQTT)", "Energy Meter");
            await EnsureDevice("abc-manufacturing", "bangalore-factory", "device-012", "HeatStress-012 (WBGT)", "WBGT Sensor");
            await EnsureDevice("abc-manufacturing", "bangalore-factory", "device-013", "NoiseMeter-013", "Noise Sensor");

            // A full shop-floor health & safety set at Chennai: vibration,
            // heat stress and noise side by side, streamable with
            // simulators/python-publisher as well.
            await EnsureDevice("whirlpool", "chennai-factory", "device-014", "Compressor-014 Vibration", "Vibration Sensor");
            await EnsureDevice("whirlpool", "chennai-factory", "device-015", "HeatStress-015 (WBGT)", "WBGT Sensor");
            await EnsureDevice("whirlpool", "chennai-factory", "device-016", "NoiseMeter-016", "Noise Sensor");
        }

        if (added.Count == 0) return;
        await db.SaveChangesAsync();
        logger.LogInformation("Demo data top-up added: {Added}", string.Join(", ", added));
    }
}
