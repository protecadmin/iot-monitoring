# Domain Schema (PostgreSQL)

## Entities

Implemented via EF Core starting Phase 3 ([core.txt](../../core.txt)
section 54); Phase 1 defines the boundary these will live in
(`Bms.Domain` for entities, `Bms.Infrastructure` for EF Core
configuration/migrations) but no tables exist yet.

```
Tenant
Site
Device
DeviceType
Gateway
Telemetry               (PostgreSQL keeps domain metadata; bulk time-series -> InfluxDB, see influxdb.md)
TelemetryRawMessage      (RawDeviceMessage persistence — see data-flow.md)
DeviceStatus
DeviceHeartbeat
AlertRule
Alert
Notification
DeviceCommand
DeviceConfiguration
Firmware
FirmwareDeployment
User
Role
AuditLog
```

## Relationships

```
Tenant
  +-- Sites
        +-- Devices
              +-- Telemetry
              +-- Alerts
              +-- Commands
              +-- Configuration
              +-- Firmware
```

Every device-scoped row (telemetry metadata, alerts, commands,
configuration, firmware deployments, raw message metadata) carries its
owning `TenantId` — directly or via `DeviceId` — so tenant isolation (see
[api-design.md](../api/api-design.md)) can be enforced at the query layer,
not bolted on afterward.

## Device registry fields

The device registry is what makes the
[identification stage](../architecture/data-flow.md) possible — it's the
only place `Transport`/`Protocol`/`PayloadFormat` are
recorded per device:

```
DeviceId, TenantId, SiteId, DeviceTypeId
Name, SerialNumber
Transport, Protocol, PayloadFormat
FirmwareVersion, HardwareVersion
Status
LastSeenAt, LastTelemetryAt, LastHeartbeatAt
```

Devices behind our own gateway — every device in normal use — register as:

| Device | Transport | Protocol | Payload format |
|---|---|---|---|
| device-017 | MQTT | MQTT | JSON |

The gateway sends BMS parameter keys, so there is no per-device field
mapping: fields are kept when they are parameters of the device's type.

## Device types

Realistic types this platform models: Temperature Sensor, Humidity Sensor,
Pressure Sensor, Vibration Sensor, Energy Meter, Air Quality Sensor, Water
Level Sensor, Environmental Station, Industrial Gateway, WBGT Sensor, Noise
Sensor.

WBGT Sensor and Noise Sensor are added by the `AddWbgtAndNoiseDeviceTypes`
migration rather than the seeder, so existing databases get them too:

| Type | Parameter keys (unit) |
|---|---|
| Vibration Sensor | `vibration` (mm/s), `temperature` (°C), `humidity` (%) |
| WBGT Sensor | `wbgt`, `dryBulb`, `wetBulb`, `globeTemp` (°C), `humidity` (%) |
| Noise Sensor | `noiseLevel` — LAeq (dB(A)), `noisePeak` — LCpeak (dB(C)) |

The seeded demo alert rules use occupational thresholds for these: WBGT > 30
°C warning / > 32 °C critical, noise > 85 dB(A) warning / > 90 dB(A)
critical, vibration > 4.5 mm/s warning / > 7.1 mm/s critical (ISO 10816
class II).

## Device lifecycle

```
REGISTERED -> PROVISIONED -> ONLINE <-> OFFLINE
                                |
                          WARNING / ERROR / MAINTENANCE
                                |
                         DECOMMISSIONED
```

Heartbeat-driven transitions: no heartbeat within a configured threshold
moves `ONLINE -> OFFLINE`; a resumed heartbeat moves `OFFLINE -> ONLINE`.
`LastSeenAt`, `LastTelemetryAt`, `LastHeartbeatAt` are tracked separately
because they answer different operational questions — e.g. a device can be
sending heartbeats but not telemetry, which is itself a meaningful signal
distinct from fully offline.

## EF Core conventions (from Phase 3 onward)

Primary/foreign keys, constraints, and indexes are defined via EF Core
configuration (not attributes scattered on entities), migrations are
committed to source control, and seed data is provided for local
development. See [postgresql.md](postgresql.md) for what PostgreSQL is —
and is not — responsible for.
