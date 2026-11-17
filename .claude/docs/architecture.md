# Architecture reference (load on demand)

## Processes and entry points
| Process | Entry | Hosted services | Talks to |
|---|---|---|---|
| Bms.Api | `src/backend/Bms.Api/Program.cs` | `RealtimeBridgeService`, `TiltDailyWorker`, `OfflineSweepWorker`*, `GatewayOfflineSweepWorker`, `TimeoutSweepWorker`* | Postgres, Influx, MQTT (client `bms-api-realtime-bridge`, non-persistent), SMTP/Telegram |
| Bms.Ingestion | `src/backend/Bms.Ingestion/Program.cs` | `MqttIngestionWorker`, `TimeSeriesBackfillWorker`, `RawMessageRetentionWorker`, `IngestionStatusPublisher` | Postgres, Influx, MQTT (client `bms-ingestion`, persistent session, 24 h expiry), Prometheus :9464 |
| Bms.Connectors | `src/backend/Bms.Connectors/Program.cs` | `MqttPublisher`, `ListenerSupervisor`, `Reporter` | API `/api/connector-service` (API key), MQTT user `bms-connectors`, TCP 12000-12099 |
| Next.js web | `src/frontend/bms-dashboard/app/layout.tsx`, `app/(protected)/layout.tsx` | — | API REST + SignalR |
\* classes live in `Bms.Ingestion/Workers` but are registered by the API.
Startup: API and Ingestion run `DatabaseStartup.MigrateWithRetryAsync` (migrate + retry); API then `DbSeeder` (Development) or `ProductionBootstrapper` (other envs).

## Telemetry data flow
1. Device/gateway publishes → Mosquitto (auth: go-auth files backend for service accounts, Postgres backend `MqttCredentials` + view `MqttAllowedTopics` for company accounts).
2. `MqttTransportAdapter` (manual ack, auto-reconnect, re-subscribe, overlap-collapsing subscriptions) → `MqttIngestionWorker.OnMessageAsync`.
3. Routing (`DispatchAsync`): `protec/hubs/{id}/…` → `HandleHubMessageAsync` (heartbeat/snapshot/ack only, v2 envelope, redacted, captured); topic registered to a device → telemetry; else by `MqttTopic.MessageType`: telemetry · ack · heartbeat · events (FOTA) · commands/ack · status (gateway health) · default (forward gateway response to realtime). `protec/gateway/{code}/…` also touches `Gateway.LastSeen`.
4. Telemetry: `MqttProtocolHandler.TryBuildRawMessageAsync` (device lookup by topic) → `IngestionPipeline.ProcessAsync`: normalize (decode `JsonPayloadDecoder` + keep params of the device type) → validate → dedupe (device + sequence/timestamp key; duplicate ⇒ `Rejected` row) → `TryAddPersistedAsync` (**ack point**) → Influx write → `MarkTimeSeriesWritten` → calculations (`Bms.Calculations`) → alerts (`AlertEvaluator`) → realtime + email/Telegram notifiers.
5. Raw message statuses: Received → Decoded → Mapped → Normalized → Validated → Persisted | Rejected. Rejected messages can be replayed (`POST /api/raw-messages/{id}/replay`).
6. Connectors path: TCP sensor (e.g. `BW-NB-WM400,` + 10 CSV fields) → `MessageFramer`/`MessageDecoder` → MQTT `iot/{tenant}/{site}/{device}/telemetry` → same pipeline.

## Realtime
- Ingestion cannot reach SignalR: `MqttRealtimeNotifier` publishes events to MQTT `bms/realtime/events`; the API's `RealtimeBridgeService` subscribes and relays via `RealtimeNotifier` → `TelemetryHub` at `/hubs/realtime` (groups `tenant-{id}` + all-tenants group).
- `RealtimeDelivery` re-checks each recipient's current DB scope per event (revocation applies to open sockets).
- Events: `TelemetryUpdated`, `DeviceStatusChanged`, `AlertCreated`, `AlertResolved`, `CommandStatusChanged`, `FirmwareDeploymentUpdated`, `CalculationUpdated`, `GatewayMessageReceived`, `HubMessageReceived` (union type in `lib/websocket/useRealtime.ts`).
- Ingestion liveness: retained `bms/ingestion/bms-ingestion/status` (+ LWT); API pauses offline sweeps while ingestion is down.

## Outbound (cloud → device)
- Legacy devices: `MqttDeviceCommandDispatcher` → `iot/{tenantSlug}/{siteCode}/{deviceCode}/{commands|config|firmware}`; ack on `…/commands/ack`; `TimeoutSweepWorker` times out pending commands.
- Protec gateway: `GatewaysController` → `protec/gateway/{code}/command`; replies on `protec/gateway/{code}/{ack|status|…}` shown live via `GatewayMessageReceived`.
- Hubs: `HubsController` + `Application/Common/HubProtocol` (v2 envelope `{v:2,id,op,…}`) → `protec/hubs/{hubId}/config` etc.; replies `ack`/`snapshot`.
- FOTA: simulated state machine; binaries on volume `/data/firmware`, `GET /api/firmware/{id}/download`, links via `FirmwareDownloadLinks` (`Firmware:PublicBaseUrl`).

## Auth and access
- `POST /api/auth/login` → JWT access (15 min, issuer `bms-platform`, HMAC `Jwt:SecretKey`) + refresh token (7 days, stored in `RefreshTokens`, rotated on `/api/auth/refresh`, revoked on logout).
- Fallback policy = authenticated user; anonymous: `AuthController`, `/health`, `/health/live`, `/health/ready`, Prometheus endpoint, `ConnectorServiceController` (guarded by `[ConnectorServiceKey]`).
- Per request: `AccessScopeMiddleware` reloads the user and role from DB, rejects legacy roles (`ROLE_REVIEW_REQUIRED`), rebuilds claims, sets `BmsDbContext.AccessTenantId/RestrictToSites/AllowedSiteIds/EnforceAccessScope` (global query filters on Tenant, Site, Device, Alert, AlertRule…), rejects forged `tenantId` query params.
- `AccessPolicyFilter` → `AccessPolicy.Allows(role, controller, action, method)`: SuperAdmin all; Executive read-only business data; CompanyAdmin = own tenant + manage SiteAdmins/sites; SiteAdmin = assigned sites (`UserSiteAccess`), may acknowledge alerts. Details: `docs/security/role-access-model.md`.
- SignalR auth: `access_token` query string accepted only on `/hubs/*`.

## Frontend state
- `AuthContext` (user + tokens in localStorage `bms.accessToken`, `bms.refreshToken`, `bms.user`); `client.ts` does a single shared refresh-and-retry on 401, then hard logout to `/login`.
- `app/(protected)/layout.tsx` gates on `useAuth()` and renders `AppShell` (sidebar nav filtered by `hasPermission`, header, tenant switcher).
- `TenantScopeContext`: SuperAdmin/Executive pick a tenant (localStorage); scoped users are pinned to their own tenant.
- `ThemeContext` (light/dark → `dark` class on `<html>`, persisted in localStorage). No global data store: pages fetch with `useApi`, mutate via `*Api` then `refetch()`, patch state from `useRealtime` events.
- Maps: react-leaflet with ArcGIS World Street Map tiles; wrap map containers in their own stacking context (`isolate`/`relative z-0`).

## Deployment topology (protecdemo VM)
nginx (80→443 redirect, Let's Encrypt IP certificate valid ~6 days, renewed hourly by timer) → `/api/`, `/hubs/realtime`, `/health` → api:8080; everything else → web:3000. Public: 443, 80, TCP 12000 (connectors), MQTT 1883/8883. Postgres/Influx are not published. Compose project `protec` = `docker-compose.yml` + `docker-compose.aws.yml`; secrets in `/opt/protec/shared/cloud.env`; daily on-VM backups (`protec-backup.timer`), no off-VM backups.
