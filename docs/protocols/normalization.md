# Normalization

## NormalizedTelemetry

The single internal shape every transport, protocol, and vendor format
ultimately produces:

```json
{
  "tenantId": "tenant-001",
  "siteId": "site-001",
  "deviceId": "device-001",
  "timestamp": "2026-09-01T10:30:00Z",
  "temperature": 31.5,
  "humidity": 68.2,
  "pressure": 1012.4,
  "vibration": 0.4,
  "energy": 12.5,
  "battery": 87,
  "signalStrength": -62,
  "latitude": 13.0827,
  "longitude": 80.2707
}
```

This is the boundary line for the entire codebase: everything to the left
of it (transports, protocols, decoders — all of `Bms.Protocols`)
deals with vendor- and wire-specific detail. Everything to the right
(validation, deduplication, business processing, persistence, alerting,
the API, the dashboard) works with this one shape and never again asks "is
this JSON or binary," "did this come over MQTT or TCP," or "does this
vendor call it `temp` or `temperature_c`."

## Why this is the single most important type in the system

Without it, every downstream concern — alert rules, dashboard charts,
reports — would need to understand every vendor's raw format, multiplied
across every transport. Alert rule "temperature > 50" would need a
different implementation per vendor JSON shape. That doesn't scale past a
couple of device types, and it's exactly the coupling our gateway absorbs
— it sends BMS parameter keys — before data reaches this point.

## Validation happens against the normalized shape

Because validation ([data-flow.md](../architecture/data-flow.md)) — value
ranges, required fields, timestamp sanity — is expressed once, against
`NormalizedTelemetry.temperature`, `NormalizedTelemetry.timestamp`, etc.,
it doesn't need a per-vendor variant. A vendor-specific validation bug
would mean the mapping produced a wrong value, not that validation itself
needs vendor awareness.

## Value conversion (MQTT-direct devices)

A device that sends a parameter in the wrong unit — millidegrees instead of
degrees, °F instead of °C — can be given a per-device conversion on its edit
form (`PUT /api/devices/{id}/value-conversions`). The normalizer applies
`value = (raw - Offset) * Scale` right after the allow-list filter, under the
same key, so validation, calculations, alerts and storage all see the
corrected value. It is per device, not per device type, because one type
covers several vendors and a wrong unit is one model's problem.

- Only MQTT-direct devices are converted. A connector device converts in its
  connector's mapping (the same formula), so it is never converted twice.
- It applies from the next reading on. Stored readings keep their value; the
  raw message keeps what the device sent. A time-series backfill rebuilds
  with the device's conversions as they are at backfill time.
- Every change is audit-logged (`Device.SetValueConversions`, before/after).

## Event time vs. ingestion time

`NormalizedTelemetry.timestamp` is **event time** — when the device says
the reading happened (`RawDeviceMessage.DeviceTimestamp`). InfluxDB also
has the option to record **ingestion time** — when the backend received
the message (`RawDeviceMessage.ReceivedAt`) — separately. These can differ
significantly (a device buffering readings offline and uploading them
later, clock drift, network delay), and conflating them would corrupt both
"what happened when" analysis and "how fresh is our data" monitoring. See
[docs/database/influxdb.md](../database/influxdb.md) for how both are
stored.
