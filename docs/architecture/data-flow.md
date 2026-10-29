# Data Flow

## The complete pipeline

Every message, regardless of transport or vendor, passes through the same
stages before it's allowed to touch business logic:

```
Receive
  -> Identify Tenant / Site / Device
  -> Identify Transport / Protocol / Payload Format
  -> Decode            (IPayloadDecoder: bytes -> structured data)
  -> Normalize          (keep fields that are parameters of the device type -> NormalizedTelemetry)
  -> Validate
  -> Deduplicate
  -> Business Processing
  -> Persist             (PostgreSQL: domain data, InfluxDB: time series)
  -> Alert Evaluation
  -> Real-Time Event      (SignalR -> Next.js)
```

Two invariants hold for every stage:

1. **The raw payload is never discarded.** `RawDeviceMessage` (see below) is
   preserved even after successful decoding — it's the only way to debug a
   vendor's malformed message, replay it after a decoder fix, or prove to a
   vendor what they actually sent. See
   [payload-decoding.md](../protocols/payload-decoding.md).
2. **A malformed message never crashes the ingestion service.** One device
   sending garbage must not take down processing for every other device. Each
   pipeline stage catches and logs its own failures and moves on.

## Entry points converge before decoding

```
IoT DEVICES
   |
   +-- MQTT --+-- TCP/IP --+-- HTTP --+-- BACnet
   |          |            |          |
   v          v            v          v
        TRANSPORT / PROTOCOL LAYER  (Bms.Protocols)
                    |
                    v
             RAW DEVICE MESSAGE
                    |
                    v
           DEVICE IDENTIFICATION
                    |
                    v
          PAYLOAD CONFIGURATION LOOKUP
                    |
                    v
           FORMAT / DECODER ROUTER
             |       |        |
           JSON     HEX     BINARY
             |       |        |
             +-------+--------+
                    |
                    v
           NORMALIZED TELEMETRY
                    |
                    v
               VALIDATION
                    |
                    v
             DEDUPLICATION
                    |
                    v
           BUSINESS PROCESSING
              |            |
              v            v
         PostgreSQL     InfluxDB
        (domain data)  (time series)
              |            |
              +-----+------+
                    |
                    v
               ASP.NET Core
              |            |
            REST        SignalR
              |            |
              +-----+------+
                    |
                    v
                 Next.js
                    |
                    v
                Dashboard
```

No matter which of MQTT/TCP/HTTP/BACnet a device used, by the time the
message reaches "Device Identification" it's a transport-agnostic
`RawDeviceMessage`. Everything from that point on is shared code.

## RawDeviceMessage

The one shape every transport/protocol adapter produces:

| Field | Purpose |
|---|---|
| `MessageId` | Unique id for this ingested message |
| `TenantId`, `SiteId`, `DeviceId` | Resolved identity |
| `Transport` | MQTT / TCP / HTTP / BACnet |
| `Protocol` | MQTT / ProprietaryTCP / BACnet / HTTP / vendor-specific |
| `Topic` / `Endpoint` | MQTT topic, or TCP/HTTP endpoint identifier |
| `PayloadFormat` | JSON / HEX / Binary / Proprietary |
| `RawPayload` | The untouched bytes, always preserved |
| `ReceivedAt` | When the backend received it |
| `DeviceTimestamp` | When the device says it happened |
| `CorrelationId` | Ties related messages together for tracing |

## Why identification happens before decoding

You can't pick a decoder or the device type's parameter list without first
knowing *which device* sent the message and *what that device is configured
to send*. A device's `Transport`/`Protocol`/`PayloadFormat`/`DeviceTypeId` live on
its registry record (see
[docs/database/schema.md](../database/schema.md)), not on the wire — the
topic/endpoint only tells you who's talking, not how to parse them.
Skipping identification and guessing the format from the bytes would be
fragile and would silently misinterpret one vendor's payload as another's.

## Why validation and deduplication happen after normalization, not before

Validation rules (value ranges, required fields, stale/future timestamps)
are expressed in terms of the *normalized* model — "temperature must be
between -50 and 150" means the same thing regardless of whether the vendor
called it `temp`, `temperature_c`, or nested it under `data.temperature`.
Writing validation against every vendor's raw shape would duplicate every
rule per vendor. Deduplication is likewise a property of normalized
identity (tenant + device + timestamp + sequence), not of raw bytes.

## Failure isolation

Each stage is scoped to a single message for a single device. A decoder
exception, a mapping miss, or a validation failure is caught, logged with
the message's `CorrelationId`, and the message is rejected — it does not
propagate up and stop the ingestion host from processing the next message,
from this device or any other. See
[docs/troubleshooting/common-issues.md](../troubleshooting/common-issues.md)
for the specific failure scenarios this is designed to survive.
