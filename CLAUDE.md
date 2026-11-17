# Protec Smart Monitoring (BMS)

**Before scanning the codebase, check `./CLAUDE.md` and `./.claude/docs/` first. Only read specific files needed for the active task.**
On-demand references: `.claude/docs/architecture.md` (data flow, auth, realtime, process/entry points) · `.claude/docs/database-and-api.md` (schema, routes, MQTT topics, external services).

## Overview
Multi-tenant IoT monitoring platform for Pro-Tec Geosystems (inclinometers, WBGT, gateways, display hubs). Gateways/hubs publish JSON over MQTT (raw-TCP sensors via the Connectors service); the backend normalizes to `NormalizedTelemetry`, stores PostgreSQL + InfluxDB, runs calculations/alerts, and pushes live updates to a Next.js dashboard over SignalR.
Stack: .NET 10 (ASP.NET Core, EF Core 10 + Npgsql, MQTTnet 5, InfluxDB.Client 5) · Next.js 16.3 / React 19.2 / TypeScript / Tailwind 4 / Recharts / Leaflet · Mosquitto 2.0 + mosquitto-go-auth · Postgres 17 · InfluxDB 2 · Docker Compose · nginx.
Dev host is Windows (PowerShell 5.1; Git Bash available). `npm.cmd` if `npm` is blocked by execution policy.

## Commands (repo root unless noted)
| Task | Command |
|---|---|
| Local infra | `docker compose up -d postgres influxdb mosquitto` |
| Build .NET | `dotnet build Bms.sln` |
| Run services | `dotnet run --project src/backend/Bms.Api --launch-profile http` · `…/Bms.Ingestion` · `…/Bms.Connectors` |
| Unit tests (no deps) | `dotnet test tests/Bms.UnitTests` |
| Single test | `dotnet test tests/Bms.UnitTests --filter "FullyQualifiedName~MqttTopicFiltersTests"` (class or `…Tests.MethodName`) |
| Integration tests | `dotnet test tests/Bms.IntegrationTests` — needs Postgres DB whose name ends `_test`; default `Host=localhost;Database=bms_platform_test;Username=bms_app;Password=devpassword`, override with env `BMS_TEST_POSTGRES` |
| Frontend (in `src/frontend/bms-dashboard`) | `npm run dev` · `npm run build` · `npm run lint` · typecheck `npx tsc --noEmit` (no script) |
| Frontend single test | `node --test tests/gateway-status.test.cjs` (plain `node:test`, `.cjs`, no runner/script) |
| New EF migration | `dotnet ef migrations add <Name> -p src/backend/Bms.Infrastructure -s src/backend/Bms.Api -o Persistence/Migrations` |
| Mirror live server MQTT into local broker | `.\infrastructure\scripts\server-mirror.ps1` (`-Disable` to stop) |
No .NET analyzer/formatter config: build warnings are the lint. Frontend ESLint = `eslint-config-next`.

## Folder map
```
src/backend/
  Bms.Domain/          entities, enums, AccessRoles, MqttTelemetryTopicRules — zero deps
  Bms.Application/     ports (Abstractions/*), IngestionPipeline, normalizer/validator, HubProtocol, Tilt calculator
  Bms.Infrastructure/  Persistence/ (BmsDbContext, Repositories, Migrations, DbSeeder, ProductionBootstrapper),
                       Telemetry/InfluxTelemetryStore, Security (JWT, hashing, AES), Notifications (SMTP, Telegram), Ota
  Bms.Protocols/       MqttTransportAdapter, MqttTopicFilters, MqttProtocolHandler, JsonPayloadDecoder,
                       MqttRealtimeNotifier (ingestion→API bridge), MqttDeviceCommandDispatcher
  Bms.Ingestion/       worker process: MqttIngestionWorker, backfill, retention, status publisher
  Bms.Alerting/ Bms.Calculations/ Bms.DeviceManagement/   alert eval · calc handlers (formula, totalizer, baseline, windowed) · lifecycle
  Bms.Connectors(.Core)/  worker: TCP listeners 12000-12099 → frame/decode → MQTT iot/.../telemetry; config from API
  Bms.Api/             composition root: Controllers, Common (AccessPolicy, ApiException, BmsControllerBase),
                       Middleware (AccessScope), Realtime (TelemetryHub, bridge), Workers, Connectors mapping
src/frontend/bms-dashboard/  App Router: app/(protected)/<page>, components/<feature>/, components/ui (Modal, FormField),
                       lib/api (client.ts fetch+refresh, resources.ts *Api objects), lib/auth, lib/tenant,
                       lib/websocket/useRealtime.ts, hooks/useApi.ts, types/api.ts (DTO mirror), app/globals.css (protec-* theme)
tests/  Bms.UnitTests (xUnit+Moq, folder per module) · Bms.IntegrationTests (WebApplicationFactory + real Postgres) · reliability/
infrastructure/  docker/*.Dockerfile · mosquitto/ (acl committed; mosquitto.conf/passwd gitignored, copy *.example)
                 nginx/ · deploy/ (VM installers) · scripts/ (backup.sh, server-mirror.ps1)
simulators/  Bms.DeviceSimulator · python-publisher/ (send_tcp_fgwm400.py, publish_sample.py)
docs/  architecture/ · mqtt/ · deployment/protec-aws-instance.md (live VM runbook) · security/role-access-model.md
core.txt  original 57-section brief; comments cite "core.txt section N"
```

## Architecture boundaries (enforced)
- Dependencies point inward: Api → {Ingestion, Protocols, Alerting, Calculations, DeviceManagement, Infrastructure} → Application → Domain. Domain/Application never reference EF, MQTTnet, Influx or ASP.NET.
- Transport ≠ protocol ≠ payload format ≠ mapping (core.txt §2). Past decoding, code sees only `NormalizedTelemetry`; no vendor/device-specific branches in alerting, storage or UI. Payload variants are never solved with new topics (§13).
- Ingestion: ack an MQTT message only after the raw message is durably in Postgres (`TryAddPersistedAsync`). Influx write, calculations and alerts follow; Influx gaps are refilled by `TimeSeriesBackfillWorker`.
- Failure isolation: transient errors (DB down) → retry without ack; permanent errors (bad payload) → log + ack. One bad message must never stall the fleet.
- Access control is server-side and default-deny: `AccessScopeMiddleware` reloads user/role/tenant/sites from DB every request and sets EF global query filters; `AccessPolicy.Allows` gates every controller/action for non-SuperAdmins; `BmsControllerBase.RequireTenantAccess`/`EffectiveTenantFilter` for explicit IDs. Frontend `lib/auth/permissions.ts` only hides UI.
- Errors: throw `ApiException.BadRequest|Forbidden|Conflict(message, "UPPER_SNAKE_CODE")`; frontend `ApiError.errorCode` reads it.
- Frontend data: `useApi(fetcher, deps)` + `resources.ts` (no React Query/Redux/SWR). Live data via `useRealtime({ EventName: handler })`. New DTO ⇒ add type to `types/api.ts`.
- Modals/forms: reuse `components/ui/Modal` (`size` md|lg|xl), `FormField`, `inputClass`.

## Forbidden
- New controller/action without an `AccessPolicy.Allows` rule (non-SuperAdmins get 403) or without tenant checks.
- MQTT subscribe calls that bypass `MqttTransportAdapter.SubscribeAsync` (it collapses overlapping filters; overlaps ⇒ one broker copy per matching filter ⇒ duplicate raw messages).
- A second process using MQTT client id `bms-ingestion` (persistent session; instances kick each other).
- Running the API in `Development` against a real database (Development seeds demo data via `DbSeeder`), or any local process against prod DB (auto-migrates).
- A z-index on `.protec-page` or any ancestor of overlays (traps modals under the z-50 header).
- `docker compose down -v`; changing `ENCRYPTION_KEY` on an existing DB (AES-encrypted SMTP/Telegram/MQTT secrets).
- Committing secrets: `appsettings.Development.json`, `.env*`, `mosquitto.conf`, `passwd`, `*.pem` stay gitignored.

## Gotchas
- `OfflineSweepWorker` and `TimeoutSweepWorker` live in `Bms.Ingestion/Workers` but are hosted by **Bms.Api** (with `TiltDailyWorker`, `GatewayOfflineSweepWorker`).
- API and ingestion both apply EF migrations at startup (`DatabaseStartup.MigrateWithRetryAsync`). Non-Development runs `ProductionBootstrapper` (empty DB needs `Bootstrap:AdminEmail` + 16+ char password).
- Next.js 16 differs from training data: read `node_modules/next/dist/docs/` before using Next APIs (see frontend `AGENTS.md`).
- `NEXT_PUBLIC_API_URL`/`WS_URL` are baked at build time; production builds use the same origin behind nginx (`/api/`, `/hubs/realtime` → API).
- Device default MQTT topic = `protec/gateway/{deviceCode}/measurement` (`MqttTelemetryTopicRules`); custom topics must be exact (no `+`/`#`).
- Duplicates are stored as `Rejected` raw messages ("Duplicate telemetry") — expected after broker redelivery; many per message ⇒ overlapping subscriptions.
- Mosquitto image reads `/etc/mosquitto/mosquitto.conf`; go-auth hashes with `/mosquitto/pw` (bcrypt), not `mosquitto_passwd`.
- Integration tests share one DB in one xUnit collection (serial); the factory refuses DB names not ending `_test` and removes `RealtimeBridgeService`.
- Dev seed logins (Development only): `superadmin@bms.local`, `executive@bms.local`, `admin@whirlpool.example` / `Passw0rd!`.
- README is partly stale: lines 1-15 are pasted terminal output; test counts, TCP/HTTP/BACnet decoders and "13 seeded devices" predate the MQTT-gateway + Connectors design. `.tmp-ingest-publish/` is committed build output — ignore it.
- Deploy target is a single EC2 VM (`protecdemo`, 52.74.238.107): manual tarball → `/opt/protec/releases`, images built on the VM, `sudo /usr/local/sbin/protec-compose …`. No CI, no registry. Runbook: `docs/deployment/protec-aws-instance.md`.
