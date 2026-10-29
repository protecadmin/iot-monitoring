# System Architecture

## The core problem

Industrial IoT platforms don't get to choose their devices' wire formats.
One vendor sends JSON over MQTT, another sends binary over TCP, another
speaks BACnet, another posts JSON over HTTP — and even two vendors using the
*same* transport and the *same* MQTT topic shape will send structurally
different JSON. A platform that lets any of that leak past its edge ends up
with alerting, dashboards, and reports that all need to know about every
vendor's quirks. That doesn't scale past a handful of device types.

The fix is a hard rule, stated in [core.txt](../../core.txt) section 2 and
carried through every layer of this codebase:

> TRANSPORT ≠ PROTOCOL ≠ PAYLOAD FORMAT

These are three independent axes of variation, not one:

| Axis | Answers | Examples |
|---|---|---|
| Transport | How bytes move | MQTT connection, TCP socket, HTTP request |
| Protocol | What the conversation means | MQTT, a proprietary TCP protocol, BACnet, HTTP |
| Payload format | How the message body is encoded | JSON, HEX, binary, proprietary |

Which field means what is not an axis here: devices report through our own
gateway, which sends BMS parameter keys, and a field is kept when it is a
parameter of the device's type. Everything upstream of that is decided by
*how the device happens to talk*. Everything downstream — alerting, storage, dashboards,
reports — must never know any of it. It only ever sees `NormalizedTelemetry`
(see [normalization.md](../protocols/normalization.md)).

## Clean Architecture layering

```
                 Bms.Api  (composition root: REST, SignalR, DI wiring)
                     |
   +-----------------+------------------+------------------+
   |                 |                  |                  |
Bms.Ingestion   Bms.Protocols     Bms.Alerting     Bms.DeviceManagement
   |                 |                  |                  |
   +-----------------+------------------+------------------+
                     |
              Bms.Application   (use cases, ports/interfaces)
                     |
               Bms.Domain        (entities, value objects — zero dependencies)

Bms.Infrastructure (EF Core, PostgreSQL, InfluxDB) implements Application's
ports and is referenced only by Bms.Api.
```

**Dependency direction is one-way, inward:** `Api → {Ingestion, Protocols,
Alerting, DeviceManagement, Infrastructure} → Application → Domain`. Nothing
in `Domain` or `Application` references MQTT, TCP, BACnet, HTTP, EF Core,
PostgreSQL, InfluxDB, or Next.js — those are all details, and details plug
into the core, not the other way around. This is what makes it possible to,
say, swap Mosquitto for another broker, or PostgreSQL for another RDBMS,
without touching a single business rule.

Concretely, the project boundaries:

- **Bms.Domain** — entities (`Device`, `Tenant`, `Site`, `Telemetry`, ...),
  value objects, domain invariants. No NuGet packages beyond the BCL.
- **Bms.Application** — use cases (e.g. "ingest a raw message", "evaluate
  alert rules") and the *ports* those use cases depend on:
  `ITransportAdapter`, `IProtocolHandler`, `IPayloadDecoder`,
  repository interfaces, etc. Depends only on Domain.
- **Bms.Infrastructure** — implements Application's persistence ports
  against EF Core/PostgreSQL and InfluxDB.
- **Bms.Protocols** — implements the transport/protocol/decoder
  ports for each real transport (MQTT, TCP, BACnet, HTTP) and vendor
  format. This is where "the messy outside world" lives.
- **Bms.Ingestion** — orchestrates Protocols + Application to run the
  end-to-end pipeline in [data-flow.md](data-flow.md).
- **Bms.Alerting** — alert rule evaluation over normalized telemetry.
- **Bms.DeviceManagement** — device registry and lifecycle
  (online/offline, heartbeat tracking).
- **Bms.Api** — the only project allowed to know about all of the above; it
  wires them together via DI and exposes REST + SignalR.

`Ingestion` additionally ships its own executable entry point (a .NET
Generic Host) so it can run as a separate deployable from `Api` — see
[docker.md](../deployment/docker.md) for why, and how real-time events
generated there reach the API's SignalR hub without a new message broker.

## Why not Kafka / Kubernetes / microservices?

Because nothing in this project's actual requirements demands them yet:

- **Kafka** solves durable, replayable, multi-consumer event streaming at a
  scale this project doesn't have. A single ingestion process reading from
  Mosquitto/TCP/HTTP and writing to PostgreSQL/InfluxDB handles the stated
  target (100 → 10,000 simulated devices) without it. If a real need for
  replay-to-multiple-consumers or horizontal ingestion scaling shows up,
  that's the point to reconsider — not before.
- **Kubernetes** solves multi-node orchestration for services that need
  independent scaling and deployment. This is a handful of services running
  as a monolith-per-concern under Docker Compose; there's no fleet to
  orchestrate.
- **Microservices** trade simplicity for independent deployability and
  scaling boundaries. Here the boundaries that matter (transport, protocol,
  payload format, mapping, domain vs. time-series storage) are already
  enforced *in-process* by Clean Architecture. Splitting them into network
  services would add latency, deployment complexity, and distributed-systems
  failure modes for no corresponding benefit at this scale.

Each of these gets revisited only if a concrete, demonstrated requirement
(not "IoT platforms usually have X") calls for it.

## Current state

Every boundary described above is fully implemented, not just scaffolded:
real MQTT/TCP/HTTP transports, real decoders for every payload format in [normalization.md](../protocols/normalization.md), a real alert
engine, real device lifecycle tracking, and a real Next.js dashboard
consuming the live API and SignalR hub. See [data-flow.md](data-flow.md)
for the pipeline this architecture exists to support, the root
[README.md](../../README.md) for what's demonstrable end-to-end, and its
"Known limitations" section for what's intentionally simplified.
