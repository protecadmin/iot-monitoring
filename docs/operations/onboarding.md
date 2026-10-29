# Onboarding a company, a site and its devices

The end-to-end path from "we signed a new customer" to "their readings are
on the dashboard", in the order the data model requires.

Each step says what to create, what it is used for later, and how to check
it before moving on — because most onboarding failures are silent. A
misnamed field does not raise an error; the reading is simply dropped.

---

## The shape of it

```
Tenant  (slug)                    ─┐
  └─ MQTT credential (per tenant)  │  these three become the MQTT topic:
  └─ Site  (code)                 ─┤  iot/{tenant}/{site}/{device}/telemetry
       └─ Device  (device code)   ─┘
             └─ Device type   → which fields are ALLOWED to be stored
```

Devices report through **our own gateway**, which talks to each device in
its native protocol (Modbus, BACnet, vendor formats) and publishes to BMS in
one shape: JSON over MQTT, keyed by BMS parameter keys. There is no
per-vendor translation in BMS.

One thing deserves attention before you start, because it is where
onboarding actually goes wrong:

- **The device type's parameter list is an allow-list.** The gateway must
  send each reading under exactly a parameter key of the device's type —
  any other key is silently discarded.

Get that aligned and the rest is data entry.

---

## 1. Tenant

One per customer company.

| Field | Notes |
|---|---|
| `Name` | Display name — "Whirlpool India" |
| `Slug` | **Lowercase, no spaces.** Becomes the MQTT username *and* the first topic segment |

The slug is not cosmetic. The broker ACL confines each tenant to
`iot/{slug}/#` by pattern, so changing it later breaks every device's
credentials and topics at once. Choose it as carefully as a primary key.

> **Check:** the tenant appears in the dashboard's tenant list.

## 2. MQTT credential for the tenant

**Nothing to create** — saving the tenant in step 1 creates its MQTT
credential automatically (and the calculation rules defined on device
types). You only need to read the password.

One credential per **tenant**, not per device. Every device belonging to
that company authenticates with the same username and password.

- `Username` is always the tenant slug — it is not free-form, because the
  broker's ACL pattern keys off it
- The password is generated for you and stored two ways: bcrypt for the
  broker to verify against, and AES-encrypted so an admin can read it back
  later in the UI

**Get the password from MQTT Credentials** (SuperAdmin) — you will need it
when configuring the gateway. Set your own there if you prefer.

> **Check:** the tenant appears under MQTT Credentials, with its slug as
> the username and a password you can reveal.

> **Implication worth knowing:** because the credential is per tenant,
> rotating it re-credentials every device that company owns. Plan rotation
> as a site visit, not a button press.

## 3. Site

One per physical location — a factory, a building.

| Field | Notes |
|---|---|
| `Code` | Becomes the second topic segment. Lowercase, no spaces |
| `Name` | Display name |
| `Latitude` / `Longitude` | Optional, drives the map view |

> **Check:** the site shows under the tenant, and on the map if you set
> coordinates.

## 4. Device type

A device type describes **a class of equipment**, not one unit — "WBGT
Sensor", "Energy Meter". Several devices share one type.

Its `Parameters` are the important part. Each is:

| Field | Notes |
|---|---|
| `Key` | The internal field name — e.g. `wbgt`, `temperature`. **This is the allow-list entry** |
| `Label` | What a human sees |
| `Unit` | `°C`, `dB`, `kWh` |
| `MinValue` / `MaxValue` | Validation bounds — readings outside are rejected |
| `Kind` | `Instantaneous` or a cumulative kind. Declared, not inferred from the unit |

**This list decides what can ever be stored for this equipment.** A device
sending `vibration` to a type that has no `vibration` parameter has that
field dropped — no error, no log line per field. If *every* field is
dropped, the whole message is rejected with *"No recognized telemetry
fields — none of the keys sent is a parameter of the device type"*.

Set `MinValue`/`MaxValue` wide enough for genuine faults. A heat-stress
sensor clamped to 18–38 °C cannot report the 45 °C reading you most want to
alert on.

> **Check:** the type lists exactly the parameter keys you expect.

## 5. Gateway output

> **Device that can't send BMS's format?** A sensor that sends its own text
> or binary over TCP (e.g. a 4G tilt sensor) goes through a **connector**
> instead — see [connectors.md](connectors.md). Steps 5 and 7 are then
> replaced by: pick the connector and enter the sensor ID in step 6, and point
> the device at the connector's port.

Configure the gateway to publish this device's readings under the parameter
keys from step 4 — same spelling, same case — in the units the parameters
use:

```json
{ "current": 12.4, "voltage": 231.5, "powerFactor": 0.95, "ts": "2026-10-15T09:15:03Z", "seq": 1042 }
```

`ts` (reading time) and `seq` (changes with every reading) are optional but
recommended. Leave out Derived parameters — BMS calculates them, and keeps
the device's own value only when one is sent.

> **Check:** every key the gateway sends appears in step 4's parameter list.

## 6. Device

The individual physical unit.

| Field | Notes |
|---|---|
| `DeviceCode` | Third topic segment. Unique, lowercase — `device-014`, `wbgt-line-3` |
| `Name` | Display name |
| `DeviceTypeId` | From step 4 |
| `Transport` / `Protocol` / `PayloadFormat` | `Mqtt` / `Mqtt` / `Json` for every device behind the gateway |
| `SerialNumber`, `Manufacturer`, `Model` | For the asset record |
| `Latitude` / `Longitude` | Optional, places it on the site map |

> **Check:** the device appears with status `Registered` and no telemetry
> yet.

## 7. Configure the physical device

Hand the installer these five values:

| Setting | Value |
|---|---|
| Broker host | your broker's hostname |
| Port | `8883` (TLS). Never 1883 in production |
| Username | the **tenant slug** |
| Password | from step 2 |
| Publish topic | `iot/{tenant-slug}/{site-code}/{device-code}/telemetry` |

Also give them the CA certificate if you are using your own CA — the device
must trust the broker's certificate or it will not connect at all.

If the device supports it, set a heartbeat on
`iot/{tenant}/{site}/{device}/heartbeat`. That is what distinguishes
"alive but not reporting" from "offline" on the dashboard.

## 8. Verify end to end

Before leaving site. In order, because each rules out the step before it:

**a. Does it connect and publish?**
```bash
mosquitto_sub -h <broker> -p 8883 -t 'iot/{tenant}/{site}/{device}/#' -v --cafile <ca.crt> -u <tenant> -P '<password>'
```
Nothing arriving → credentials, topic, TLS trust, or the device itself.

**b. Did we store it, and what did we make of it?**
```sql
SELECT "ReceivedAt", "ProcessingStatus", "ProcessingError"
FROM "RawDeviceMessages"
WHERE "DeviceId" = '<device-id>'
ORDER BY "ReceivedAt" DESC LIMIT 5;
```

This is the single most useful query in onboarding:

| Status | Meaning |
|---|---|
| `Persisted` | Good — it decoded, validated and stored |
| `Rejected` + *"No recognized telemetry fields — none of the keys sent is a parameter of the device type"* | Steps 4 and 5 do not line up. **Most common failure** |
| `Rejected` + a validation message | A value fell outside the parameter's Min/Max |
| nothing at all | It never reached ingestion — go back to (a) |

**c. Does it reach the dashboard?** Live telemetry should update within
seconds, and the chart should populate.

## 9. Alert rules (check, and tune while you are there)

Alert rules are **global**: defined once per device type (**Rules → Alert
Rules**, SuperAdmin), so a new company and its devices are covered the
moment they exist — nothing to create per company. A rule set to *All
companies* applies everywhere; one set otherwise applies only where a
company switches it on.

To see or tune them for a device, open **Alerts** with the company selected
(top of the sidebar) and pick the device in **Alert rules**. Each rule shows
the value in effect and where it comes from. **Edit**, **Turn off** and
**Reset** change it for that device only; every other device keeps
following the standard rule:

```
Global High Voltage > 250 → device-017 240 (tagged Device)
```

A "greater than" rule is the high limit and a "less than" rule the low one;
Warning shows as Caution and Critical as Danger on the dashboard cards.
Alerts close by themselves when readings return within the rule, or when a
silent device reports again.

Under maintenance? Mute the device with **Alerts: On / Muted** in the
Devices list — its status still updates, it just raises no alerts.

## 10. Calculation rules (optional)

Derived values — energy consumed from a cumulative totalizer, for instance.
Only if the customer needs them.

---

## Testing without hardware

The simulator publishes to the same topics as a real device, so you can
prove steps 1–6 before anything is installed:

```bash
cd simulators/python-publisher
.venv/bin/python3 publish_sample.py --list
.venv/bin/python3 publish_sample.py --device-id <code> --interval 5 --mqtt-password '<tenant password>'
```

Its `DEVICE_PROFILES` table must know the device, so for a brand-new device
add an entry there — or just publish by hand with `mosquitto_pub`, which
needs no code change:

```bash
mosquitto_pub -h <broker> -p 8883 --cafile <ca.crt> \
  -u <tenant-slug> -P '<password>' \
  -t 'iot/{tenant}/{site}/{device}/telemetry' \
  -m '{"temp":22.5,"hum":50}'
```

Then run the query in step 8b. That round trip exercises every piece of
configuration you just created.

---

## The mistake you will make

Nine times in ten, a device that "isn't working" is a name mismatch between
step 4 and step 5:

```
gateway sends     {"drybulb": 34.0}
parameter Key      dryBulb                ← must match the key sent (case aside)
```

A key that matches no parameter vanishes without an error. If none matches,
the message is rejected with *"No recognized telemetry fields — none of the
keys sent is a parameter of the device type"*. Keys are matched without
regard to case, but spelling must be exact.

## A reusable order of work

For each new site, roughly half a day the first time and an hour once
familiar:

1. List the **readings** the device provides and agree the parameter keys —
   they determine steps 4 and 5
2. Tenant (its MQTT credential is created with it) → site
3. Device type with parameters taken from that sample
4. Gateway configured to send those parameter keys
5. Device
6. Prove it with `mosquitto_pub` before the hardware arrives
7. Install, configure, verify with step 8
8. Alert rules while on site
