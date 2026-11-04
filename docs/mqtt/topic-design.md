# MQTT Topic Design

## Default gateway measurement topic

```text
protec/gateway/{gatewayId}/measurement
```

For MQTT-direct devices, the gateway ID is stored as the device code. Each
gateway connects to one sensor. Register the gateway ID, company and site;
leave the optional custom topic blank. Ingestion resolves the generated topic
through the device registry. Publishing is restricted to its owning company;
every authenticated MQTT user can read gateway traffic across companies through
`protec/gateway/#`, according to the platform's shared-read policy.
See [custom-topics.md](custom-topics.md) for custom overrides and rollout details.

## Legacy and control topic shape

The following addresses remain supported for existing publishers, internal TCP
connector telemetry, and command/heartbeat/status traffic. They are not the
default measurement address shown during MQTT device registration.

```
iot/{tenantId}/{siteId}/{deviceId}/{messageType}
```

Examples:

```
iot/tenant-001/site-001/device-001/telemetry
iot/tenant-001/site-001/device-001/heartbeat
iot/tenant-001/site-001/device-001/status
iot/tenant-001/site-001/device-001/events
iot/tenant-001/site-001/device-001/commands
iot/tenant-001/site-001/device-001/commands/ack
iot/tenant-001/site-001/device-001/config
iot/tenant-001/site-001/device-001/firmware
```

The topic encodes exactly four things: **tenant, site, device, message
type.** Nothing else.

## What the topic must never encode: payload format

It's tempting to make routing "easy" by putting the format in the topic:

```
iot/json/tenant-001/site-001/device-001/telemetry     <- WRONG
iot/binary/tenant-001/site-001/device-001/telemetry    <- WRONG
```

This is rejected for the same reason given in
[system-architecture.md](../architecture/system-architecture.md): the topic
is a *transport-level routing key*, not a description of the payload. A
vendor changing their firmware from JSON to binary shouldn't require every
subscriber's topic filters to change, and a single topic must be able to
receive different vendors' different JSON shapes (see
[normalization.md](../protocols/normalization.md)) without the topic itself
needing to distinguish them. The backend determines transport, protocol,
and payload format from the *device registry*
(`Device.Transport`, `Device.Protocol`, `Device.PayloadFormat` — see
[schema.md](../database/schema.md)), not from the topic string.

## Business hierarchy behind the topic

```
Tenant (tenant-001)
  -> Site (site-001, site-002)
       -> Device (device-001, device-002, device-003)
```

- **Tenant** — a customer/organization. Every query and every MQTT ACL is
  scoped by tenant; tenant isolation is mandatory (see
  [api-design.md](../api/api-design.md)).
- **Site** — a physical location owned by a tenant.
- **Device** — a single IoT device at a site.

`Project` is deliberately not part of this hierarchy — it isn't required by
anything in the current scope, and adding a level "because platforms
usually have one" is exactly the kind of premature structure this project
avoids.

## Message types and direction

| Message type | Direction |
|---|---|
| `telemetry` | Device -> Backend |
| `heartbeat` | Device -> Backend |
| `status` | Device -> Backend |
| `events` | Device -> Backend |
| `commands` | Backend -> Device |
| `commands/ack` | Device -> Backend |
| `config` | Backend -> Device |
| `firmware` | Backend -> Device |

Backend-to-device messages (`commands`, `config`, `firmware`) are published
by the API/ingestion host to a specific device's topic; device-to-backend
messages are subscribed to with wildcards (below) so one broker connection
can receive from every device.

## Wildcards

`+` matches exactly one topic level. `#` matches the remaining levels
(any number, including zero), and can only appear as the last segment.

| Intent | Filter |
|---|---|
| Everything from one device | `iot/tenant-001/site-001/device-001/#` |
| One site's telemetry | `iot/tenant-001/site-001/+/telemetry` |
| One tenant's telemetry, all sites | `iot/tenant-001/+/+/telemetry` |
| All telemetry, all tenants | `iot/+/+/+/telemetry` |

The ingestion host subscribes with `iot/+/+/+/#` (or narrower, per
deployment) and relies on `{tenantId}`/`{siteId}`/`{deviceId}` extracted
from the matched topic — not on separate subscriptions per device — to
scale to thousands of devices on one connection.
