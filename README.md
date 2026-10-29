# BMS — Industrial IoT Monitoring Platform

A working, end-to-end Industrial IoT / Building Management System monitoring
platform. Simulated devices talk over MQTT, TCP, HTTP, and a simulated
BACnet boundary, in whatever JSON/HEX/binary/proprietary format their vendor
chose; the backend decodes, maps, and normalizes all of it into one internal
telemetry model, persists it to PostgreSQL and InfluxDB, evaluates alert
rules, and streams live updates to a Next.js dashboard over SignalR.
Commands, remote configuration, and a simulated OTA firmware rollout flow
the other direction, device → backend → dashboard and back.

This is **not** a CRUD app — it's a from-scratch exercise in multi-protocol
ingestion, payload normalization, time-series data, real-time delivery,
device lifecycle management, and production troubleshooting.

## What's implemented

Everything below is real, working code — not a stub:

- **Multi-protocol ingestion**: MQTT (real broker connection, QoS 1,
  LWT, reconnect-with-backoff), raw TCP (length-prefixed framing, partial-
  packet handling), HTTP, and a documented-simulated BACnet boundary
  (object/instance/present-value model, carried over HTTP since a real
  BACnet/IP stack isn't practical to stand up locally).
- **Decode → map → normalize → validate → dedupe → persist → alert →
  notify** pipeline, shared by every transport, with per-message failure
  isolation (one bad message never stops the rest of the fleet).
- **One device shape: our own gateway**, which publishes JSON over MQTT keyed
  by BMS parameter keys. Fields are kept when they are parameters of the
  device's type and converge on `NormalizedTelemetry` — no per-vendor
  mapping. JSON, binary, hex, proprietary and BACnet decoders remain.
- **PostgreSQL** (EF Core, real migrations, seed data: 3 tenants, 4 sites,
  13 devices spanning every transport/format combo) for domain data;
  **InfluxDB** for time-series telemetry.
- **Alert engine** with cooldown, duplicate prevention, and device-offline /
  no-telemetry rules.
- **JWT auth + RBAC + tenant isolation**, enforced at the query layer, not
  just the UI.
- **SignalR real-time delivery** — including from the separate Ingestion
  worker process, bridged over an internal MQTT topic (no Redis/extra
  infra) back to the API's hub.
- **Commands, remote configuration, and a simulated FOTA state machine**
  (download → verify → install → reboot → success/failure), driven over
  MQTT and tracked per-device.
- **A real device simulator** producing MQTT/TCP/HTTP traffic across all 13
  seeded devices, with heartbeat, command ACKs, FOTA participation, and
  injectable failures (malformed payloads, duplicates, partial TCP packets).
- **Next.js dashboard**: login, live-updating overview, device list with
  filters/pagination, device detail tabs (Overview/Live/History/Alerts/
  Commands/Configuration/Raw Messages), alerts, commands, firmware/OTA,
  CSV reports, and a lat/long device map.
- **42 unit tests + 10 integration tests** (the latter boot the real API
  host against a real PostgreSQL database), all passing.

See [Known limitations](#known-limitations) for what's intentionally out of
scope, and `core.txt` for the original brief this was built against.

## Stack

| Concern | Choice |
|---|---|
| Backend | ASP.NET Core (C#), REST + SignalR |
| Frontend | Next.js, TypeScript, App Router |
| Domain/transactional data | PostgreSQL + EF Core |
| Time-series telemetry | InfluxDB |
| Device transports | MQTT (Mosquitto), TCP, HTTP, simulated BACnet/IP |
| Testing | xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing` |
| Infra | Docker / Docker Compose |

Deliberately **not** used: Kafka, Kubernetes, Redis, a microservices split —
see [docs/architecture/system-architecture.md](docs/architecture/system-architecture.md#why-not-kafka--kubernetes--microservices)
for why none of these are needed at this scale.

## Repository layout

```
src/
  backend/
    Bms.Domain/            entities, value objects — zero external dependencies
    Bms.Application/       use cases, ports (ITransportAdapter, IPayloadDecoder, repositories, ...)
    Bms.Infrastructure/     EF Core + PostgreSQL, InfluxDB client, JWT, password hashing
    Bms.Protocols/          MQTT/TCP/HTTP transports, protocol handlers, decoders, mappers
    Bms.Ingestion/          the separate Ingestion Worker: MQTT/TCP listeners
    Bms.Alerting/           alert rule evaluation (cooldown, duplicate prevention)
    Bms.DeviceManagement/   device lifecycle (online/offline, heartbeat)
    Bms.Api/                composition root — REST, SignalR, auth, health checks, offline/timeout sweeps
  frontend/
    bms-dashboard/          Next.js dashboard
simulators/
  Bms.DeviceSimulator/       MQTT/TCP/HTTP device simulator
tests/
  Bms.UnitTests/
  Bms.IntegrationTests/
infrastructure/
  docker/                   Dockerfiles (api, ingestion, web)
  mosquitto/                 MQTT broker config
  postgres/                  init scripts
  influxdb/
docs/                        architecture & protocol documentation
Bms.sln
docker-compose.yml
```

## Running it

### Option A — Docker Compose (full stack)

```bash
cp .env.example .env      # then edit JWT_SECRET_KEY and other secrets
# generate the real (git-ignored) Mosquitto password file — see
# infrastructure/mosquitto/passwd.example for the exact commands
docker compose up --build
```

| Service | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API | http://localhost:5000 |
| Swagger | http://localhost:5000/swagger |
| MQTT | localhost:1883 |
| PostgreSQL | localhost:5432 |
| InfluxDB | localhost:8086 |

`api` runs migrations and seeds development data on startup automatically.

### Option B — local development (no Docker)

Prerequisites: .NET SDK 10, Node.js 20+, a local PostgreSQL (InfluxDB/
Mosquitto optional — the pipeline degrades gracefully without them, logging
and continuing rather than crashing).

Local dev settings are git-ignored (they hold a JWT signing key, DB
password and InfluxDB token — see core.txt section 41), so create them
once from the committed examples:

```bash
cp src/backend/Bms.Api/appsettings.Development.json.example src/backend/Bms.Api/appsettings.Development.json
cp src/backend/Bms.Ingestion/appsettings.Development.json.example src/backend/Bms.Ingestion/appsettings.Development.json
cp src/frontend/bms-dashboard/.env.local.example src/frontend/bms-dashboard/.env.local
```

Then:

```bash
# 1. Backend API (migrates + seeds automatically)
dotnet run --project src/backend/Bms.Api

# 2. Ingestion worker (MQTT/TCP listeners) — optional if Mosquitto isn't running
dotnet run --project src/backend/Bms.Ingestion

# 3. Frontend
cd src/frontend/bms-dashboard && npm install && npm run dev

# 4. Device simulator — needs the API (HTTP) and/or Mosquitto (MQTT/TCP) reachable
dotnet run --project simulators/Bms.DeviceSimulator -- --device-id=device-004 --interval=5
dotnet run --project simulators/Bms.DeviceSimulator -- --count=11
```

Seeded login (all seeded users share the dev password `Passw0rd!`):
`superadmin@bms.local`, `admin@whirlpool.example`, `admin@abc-manufacturing.example`, `viewer@demo-company.example`.

### Running the tests

```bash
dotnet test tests/Bms.UnitTests            # 42 tests, no external dependencies
dotnet test tests/Bms.IntegrationTests     # 10 tests, needs a local Postgres — see BmsApiFactory.cs
```

## Demonstrating the platform

- **Gateway telemetry**: run the simulator for several devices
  (`--count=11`) and watch `/devices` — each publishes over MQTT the way
  our gateway does (JSON keyed by BMS parameter keys) and lands on the same
  `NormalizedTelemetry` fields in the dashboard.
- **Alerts**: push a temperature above 50°C for a device (the simulator's
  random walk will eventually cross it, or send it directly with
  `simulators/python-publisher`) and watch an alert appear in
  real time on `/alerts` — then acknowledge/resolve it.
- **Commands**: from a device's Commands tab, send Restart — the simulator
  ACKs it over MQTT and the dashboard updates without a refresh.
- **FOTA**: create a firmware release and deploy it to a device on
  `/firmware` — watch it progress DOWNLOADING → VERIFYING → INSTALLING →
  REBOOTING → SUCCESS (or a simulated checksum failure) in real time.
- **Raw message replay**: after adding a missing parameter to a device type,
  replay a previously-rejected raw message from a device's Raw Messages tab.
- **Failure resilience**: stop InfluxDB or Mosquitto — ingestion keeps
  persisting to PostgreSQL and evaluating alerts; it only stops writing
  time-series points, logging the failure instead of crashing.

## Known limitations

- **MQTT only**: devices report through our own gateway, which speaks each
  device's native protocol (Modbus, BACnet, ...) and publishes JSON over
  MQTT. BMS has no TCP, HTTP or BACnet ingestion of its own.
- **FOTA** is entirely software-side simulation, per its own requirement —
  no real firmware, bootloader, or flashing.
- **Reports** are CSV only; Excel/PDF export is a documented follow-up.
- **Fine-grained permissions** (`Device.Manage`, `Alert.View`, etc. from the
  original brief) are simplified to role-based `[Authorize(Roles=...)]`
  checks rather than a full permission matrix.
- **Per-device MQTT credentials**: authentication is per-tenant (one
  credential per tenant, restricted by broker ACL to that tenant's own
  topic namespace — see `infrastructure/mosquitto/acl`), not per-device.
  A compromised device can't spoof another tenant, but can still spoof
  another device within its own tenant; true per-device isolation needs a
  dynamic auth backend, which static password-file auth can't provide.
- **Device management UI**: full create/edit/delete for devices, sites,
  tenants, users, device types, and alert rules is in the dashboard.
- Docker Compose was validated with `docker compose config`; the full
  `docker compose up --build` path should be verified in an environment
  with the Docker daemon available (not available in the environment this
  was built in — the equivalent had to be verified against a local
  PostgreSQL instead, see docs/deployment/docker.md).

## Documentation

- [docs/architecture/system-architecture.md](docs/architecture/system-architecture.md) — layers, dependency direction, why this shape
- [docs/architecture/data-flow.md](docs/architecture/data-flow.md) — the full device → dashboard pipeline
- [docs/mqtt/topic-design.md](docs/mqtt/topic-design.md), [qos.md](docs/mqtt/qos.md), [reliability.md](docs/mqtt/reliability.md)
- [docs/protocols/payload-decoding.md](docs/protocols/payload-decoding.md), [normalization.md](docs/protocols/normalization.md)
- [docs/database/schema.md](docs/database/schema.md), [postgresql.md](docs/database/postgresql.md), [influxdb.md](docs/database/influxdb.md)
- [docs/api/api-design.md](docs/api/api-design.md)
- [docs/deployment/docker.md](docs/deployment/docker.md)
- [docs/deployment/aws.md](docs/deployment/aws.md) — end-to-end AWS deployment: EC2 + Compose, SSM secrets, TLS, backups, monitoring
- [docs/troubleshooting/common-issues.md](docs/troubleshooting/common-issues.md)
