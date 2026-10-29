# InfluxDB Responsibility

## What InfluxDB owns

High-volume time-series telemetry: temperature, humidity, pressure,
vibration, energy, battery, signal strength — one point per reading per
device, expected to accumulate continuously from every online device. See
[postgresql.md](postgresql.md#why-split-across-two-databases-at-all) for
why this is split from domain data rather than stored in PostgreSQL.

## Measurement / tags / fields

InfluxDB's data model, applied to this platform:

- **Measurement**: `telemetry` (one conceptual measurement; readings are
  distinguished by fields and tags, not by measurement name per metric).
- **Tags** (indexed, used for filtering): `tenantId`, `siteId`, `deviceId`.
- **Fields** (the actual values, not indexed for equality lookup):
  `temperature`, `humidity`, `pressure`, `vibration`, `energy`, `battery`.

Tagging by `tenantId`/`siteId`/`deviceId` is what makes "this tenant's
telemetry," "this site's temperature over the last 24h," and "this
device's history" all efficient range queries rather than full scans.

## Event time vs. ingestion time

Two distinct timestamps matter, and conflating them loses information:

- **Event time** — when the device says the reading occurred
  (`RawDeviceMessage.DeviceTimestamp`, carried through as
  `NormalizedTelemetry.timestamp` — see
  [normalization.md](../protocols/normalization.md)). This is what "what
  was the temperature at 10:30" means.
- **Ingestion time** — when the backend actually received and wrote the
  point (`RawDeviceMessage.ReceivedAt`). This is what answers "how fresh is
  our data" / "is this device's uplink delayed" — relevant for a device
  that buffers readings offline and uploads a batch later, where event
  time and ingestion time can differ by minutes or hours.

Points are written with event time as the primary InfluxDB timestamp;
ingestion time is retained (via `RawDeviceMessage`/PostgreSQL metadata, see
[schema.md](schema.md)) for freshness/latency monitoring rather than
discarded.

## Querying

Historical telemetry for the dashboard
([dashboard docs, core.txt section 34](../../core.txt)) and reports
([core.txt](../../core.txt) section 52) are served by InfluxDB-backed API
queries — range + aggregation (e.g. mean/max per interval) rather than
returning every raw point for a 7-day chart.

## Retention

InfluxDB retention policies bound how long raw high-resolution points are
kept; this is a capacity/cost knob to be tuned once real query and volume
patterns exist, not fixed prematurely in Phase 1. Raw device *messages*
(pre-normalization payloads) have their own, separate retention story — see
[core.txt](../../core.txt) section 30 and
[payload-decoding.md](../protocols/payload-decoding.md) — because they
serve debugging/replay, not charting.
