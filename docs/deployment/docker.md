# Docker

For deploying this stack to AWS (EC2 + Compose, secrets in SSM, real
certificates, backups, monitoring) see [aws.md](aws.md).

## Services

`docker-compose.yml` defines six services, all runnable via `docker compose up --build`:

| Service | Image / build | Port(s) | Role |
|---|---|---|---|
| `postgres` | `postgres:17-alpine` | 5432 | Domain/transactional data |
| `influxdb` | `influxdb:2` | 8086 | Time-series telemetry |
| `mosquitto` | `eclipse-mosquitto:2` | 1883, 8883 | MQTT broker |
| `api` | [infrastructure/docker/api.Dockerfile](../../infrastructure/docker/api.Dockerfile) | 5000 (Swagger at `/swagger`) | REST, SignalR, auth, health checks, offline/timeout sweeps |
| `ingestion` | [infrastructure/docker/ingestion.Dockerfile](../../infrastructure/docker/ingestion.Dockerfile) | — | MQTT ingestion only |
| `web` | [infrastructure/docker/web.Dockerfile](../../infrastructure/docker/web.Dockerfile) | 3000 | Next.js dashboard |

`api` and `ingestion` are deliberately separate deployables — see
[system-architecture.md](../architecture/system-architecture.md) — because
only `ingestion` needs to run persistent MQTT/TCP listeners, while `api` is
a request/response + SignalR host. Both run `Database.MigrateAsync()` on
startup (idempotent, safe if both start around the same time); only `api`
also seeds development data.

The periodic offline-device and command/firmware-timeout sweeps
(`OfflineSweepWorker` / `TimeoutSweepWorker`) run inside `api`, not
`ingestion` — they only need DB access, not any protocol-listening
machinery, and `api` is the process guaranteed to be up whenever the
dashboard is usable. Colocating them with `ingestion`'s socket listeners
would mean that process restarting (which happens independently of the
API, since it owns the flakier external MQTT/TCP connections) silently
stops all device-health monitoring with no signal that anything's wrong.

Real-time events that originate in `ingestion` (telemetry updates, alerts
raised from a threshold breach, a device coming back Online, command ACKs,
FOTA progress) reach browser clients connected to `api`'s SignalR hub via a
small internal MQTT topic (`bms/realtime/events`), not a new pub/sub system
— see `MqttRealtimeNotifier` / `RealtimeBridgeService` in the codebase.
Events that originate in `api` itself (a device going Offline via the
sweep, alert acknowledge/resolve, initial command dispatch) reach the hub
directly, no bridge needed. This reuses infrastructure already required
for the platform rather than adding Redis or another broker purely to
bridge two processes.

## First-time setup

```bash
cp .env.example .env
# edit .env — at minimum set JWT_SECRET_KEY to a real random value

# Mosquitto needs a real password file; the example only documents how
# (the API's real-time bridge, the ingestion worker, and one device
# credential per tenant — see infrastructure/mosquitto/passwd.example and
# ../acl for the ACL rules that make this tenant-isolating):
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -c -b /mosquitto/config/passwd bms-api-realtime-bridge change-me
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -b /mosquitto/config/passwd bms-ingestion change-me
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -b /mosquitto/config/passwd whirlpool devicepassword
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -b /mosquitto/config/passwd abc-manufacturing devicepassword
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -b /mosquitto/config/passwd demo-company devicepassword

docker compose up --build
```

`docker compose config` validates the compose file (merges `.env`,
resolves `${VAR}` interpolation) without starting anything — useful after
editing `.env` or `docker-compose.yml`, and the check this project's own
CI-equivalent verification relies on in environments without a Docker
daemon available.

## Why the API/Ingestion images build from the repo root

`api.Dockerfile` and `ingestion.Dockerfile`'s build context is the
repository root, because both `Bms.Api.csproj` and `Bms.Ingestion.csproj`
reference sibling projects under `src/backend/` via `ProjectReference` —
the whole `src/backend/` tree needs to be in the build context. `web.
Dockerfile`'s context is `src/frontend/bms-dashboard` itself, since the
Next.js app is self-contained. All three use multi-stage builds (an SDK/
`node` build stage, a slim runtime stage) so the shipped image doesn't
carry the .NET SDK or `node_modules`'s dev dependencies.

## Ports

| Component | Port |
|---|---|
| Next.js | 3000 |
| ASP.NET Core API | 5000 |
| Swagger | 5000/swagger |
| MQTT | 1883 |
| MQTTS | 8883 |
| PostgreSQL | 5432 |
| InfluxDB | 8086 |

All overridable via `.env` (`API_PORT`, `WEB_PORT`, `MQTT_PORT`,
`MQTTS_PORT`, `POSTGRES_PORT`, `INFLUXDB_PORT`).

## Secrets

Nothing is hardcoded: `.env` (git-ignored, seeded from `.env.example`)
supplies Postgres credentials, the InfluxDB admin token/org/bucket, MQTT
credentials for both the API's real-time bridge and the ingestion worker,
and the JWT signing key — see [core.txt](../../core.txt) section 41 and
[api-design.md](../api/api-design.md).

## Verifying without a Docker daemon

`docker compose config` only needs the Compose CLI, not a running daemon,
and is enough to catch syntax/interpolation errors. The full container
build and runtime behavior (migrations, seeding, MQTT connectivity) was
instead verified in this project by running `Bms.Api` and `Bms.Ingestion`
directly against a local PostgreSQL — see the root
[README.md](../../README.md#known-limitations) for that caveat and what it
does and doesn't cover.
