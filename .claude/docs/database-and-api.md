# Database, API and external services reference (load on demand)

## PostgreSQL (EF Core 10 + Npgsql, code-first)
- Context: `src/backend/Bms.Infrastructure/Persistence/BmsDbContext.cs`. Migrations: `Persistence/Migrations/` (~40, additive, snapshot `BmsDbContextModelSnapshot.cs`). Auto-applied at API and ingestion startup.
- Naming: EF defaults, so table and column names are PascalCase and must be quoted in raw SQL (`"Devices"."DeviceCode"`). Keys are `Guid Id`. Some enums are stored as strings (`HasConversion<string>()`, e.g. `Device.Connection`, `TimeSource`, `Transport`); check the model config before writing SQL.
- Scope filters: global query filters on Tenant, Site, Device, Alert, AlertRule… are driven by `AccessTenantId`, `RestrictToSites`, `AllowedSiteIds` and `EnforceAccessScope` (set by `AccessScopeMiddleware`). Background workers run with `EnforceAccessScope=false`, so they see all rows.
- JSON columns: `Device.ValueConversions` (`OwnsMany(...).ToJson()`), connector mappings (`ToJson`).
- Raw-SQL view: `MqttAllowedTopics` (migration `20261103100642_AddDeviceMqttTelemetryTopic`, updated by `…DefaultProtecGatewayTopics`) feeds the Mosquitto go-auth ACL. Changing device-topic or credential columns means updating this view.
- Unique indexes worth knowing: `Tenant.Slug`; `Site(TenantId, Code)`; `Device.DeviceCode` (globally unique); `Device.MqttTelemetryTopic`; `Device(ConnectorId, SensorId)` filtered; `Connector(Protocol, Port)`; `Connector.DeviceCodePrefix`.

| Area | Entities (DbSet) |
|---|---|
| Org and auth | Tenants, Sites, Users, Roles, UserSiteAccess, RefreshTokens, AuditLogs, MqttCredentials |
| Devices | Devices, DeviceTypes, DeviceTypeParameters, DeviceConfigurations, DeviceCommands, DeviceBaselines, Gateways, Hubs |
| Ingestion | RawDeviceMessages (status Received…Persisted/Rejected, dedup key, time-series-written flag) |
| Alerts | Alerts, AlertRules, AlertRuleOverrides, GlobalAlertRules |
| Calculations | CalculationRules, DeviceCalculationRules, CalculationStates, CounterResets |
| Tilt (inclinometers) | TiltDailyReadings (closed 23:59 Asia/Singapore by `TiltDailyWorker`), TiltMonitoringSettings |
| Firmware | Firmwares, FirmwareDeployments |
| Connectors | Connectors, ConnectorUnknownDevices, ConnectorUnmappedKeys, ConnectorServiceStatuses, ConnectorCaptures |
| Settings | SmtpSettings, TelegramSettings (secrets AES-encrypted with `Encryption:Key`) |

Seeding: `DbSeeder` (Development only: demo tenants, users and devices; on existing DBs it still runs `EnsureGlobalAlertRulesAsync` and `EnsureDemoAdditionsAsync`). `ProductionBootstrapper` creates the four roles and the first SuperAdmin.

## InfluxDB 2
- Org `bms-platform`, bucket `telemetry`, measurement `telemetry`, tags `tenantId`, `siteId`, `deviceId`; one field per normalized parameter key.
- `InfluxTelemetryStore` implements `ITelemetryStore`, `ICalculatedSeriesStore` and `ITiltTelemetryReader`. Calculated and derived series (counter deltas, rollups) are computed in Flux at query time; there is no separate measurement.
- Postgres stays the source of truth: `TimeSeriesBackfillWorker` rewrites persisted raw messages that are missing from Influx.

## REST API (`Bms.Api/Controllers`, all under `/api`)
Conventions: controllers derive from `BmsControllerBase`; lists return `PagedResult<T>` with `[FromQuery]` filters (`tenantId`, `siteId`, `search`, `sortBy`, `sortDescending`, page params); errors are `ApiError { success:false, message, errorCode, traceId }` thrown via `ApiException.NotFound|BadRequest|Conflict|Forbidden|Unprocessable`. DTOs live in `Bms.Api/Dtos`, mirrored by hand in `frontend/types/api.ts`; frontend calls go through `lib/api/resources.ts`.

| Base route | Endpoints (beyond CRUD) |
|---|---|
| `auth` (anonymous) | `login`, `refresh`, `logout`, `change-password` |
| `tenants`, `sites`, `users` | users: `{id}/activate`, `{id}/deactivate` (SuperAdmin + CompanyAdmin) |
| `devices` | `lifecycle-settings`, `{id}/alerts`, `{id}/digital-twin`, `{id}/value-conversions`; nested: `{deviceId}/commands`, `/configuration`, `/baseline`, `/tilt`, `/calculations`, `/raw-messages`, `/telemetry[/history|/rollup]` |
| `telemetry/latest` | latest values across visible devices |
| `raw-messages/{id}/replay` | re-run the pipeline on a stored message |
| `device-types` | `{id}/parameters` CRUD |
| `alerts` | `{id}/acknowledge`, `{id}/resolve`, `rules`, `rules/effective` |
| `alert-rules/global` | `effective`, `{id}/override` |
| `calculations` | `types`, `rules`, `rules/{id}/devices`, `rules/{id}/results`, `eligible` |
| `gateways` | `available`, `{id}/commands` |
| `hubs` | `{id}/commands`, `{id}/commands/prepare`, `{id}/monitor`, `{id}/config/desired` |
| `connectors` | `test`, `{id}/capture`, `captures/{id}`, `unknown-devices`, `{id}/unmapped-keys`, `service-status` |
| `connector-service` (API key) | `config`, `heartbeat`, `unknown-devices`, `unmapped-keys`, `captures/pending`, `captures/{id}` (used by Bms.Connectors) |
| `firmware` | `{id}/file`, `{id}/download`, `deploy`, `deployments`, `deployments/{id}/retry` |
| `mqtt-credentials` | `{id}/rotate`, `{id}/password` (SuperAdmin) |
| `reports` | `telemetry`, `alerts` (CSV) |
| `settings/smtp`, `settings/telegram` | GET/PUT, `test` (SuperAdmin) |
| `audit-log` | GET (SuperAdmin) |
Non-`/api`: SignalR `/hubs/realtime`; `/health`, `/health/live`, `/health/ready`; Prometheus scrape endpoint (anonymous).

## MQTT topics and accounts
| Topic | Direction | Notes |
|---|---|---|
| `protec/gateway/{code}/measurement` | gateway → cloud | telemetry JSON `{v,type,device_id,sequence,sensor,timestamp_utc,interval_s,values{},units{}}` |
| `protec/gateway/{code}/{health,status,ack,…}` | gateway → cloud | status → gateway health; any other one-level reply → live UI |
| `protec/gateway/{code}/command` | cloud → gateway | from `GatewaysController` |
| `protec/hubs/{hubId}/{heartbeat,snapshot,ack}` | hub → cloud | v2 envelope, `hub` must equal `{hubId}`; `config` etc. are cloud → hub |
| `iot/{tenant}/{site}/{device}/{telemetry,heartbeat,events,commands/ack}` | legacy device → cloud | connectors publish `…/telemetry` |
| `iot/…/{commands,config,firmware}` | cloud → legacy device | `MqttDeviceCommandDispatcher` |
| `bms/realtime/events` | ingestion → API | internal SignalR bridge |
| `bms/ingestion/bms-ingestion/status` | ingestion → API | retained liveness + LWT |
Accounts (ACL `infrastructure/mosquitto/acl`): `bms-ingestion` (reads `#`), `bms-api-realtime-bridge`, `bms-connectors` (writes telemetry only), `protec-platform-monitor` (monitoring; password only on the VM), plus company accounts from the `MqttCredentials` table. ACL `pattern` rules grant every authenticated user read+write on `protec/gateway/#` and `protec/hubs/#`.

## External services and config keys
- Services: Mosquitto (1883 plain, 8883 TLS), InfluxDB, SMTP (`SmtpEmailSender`, settings in DB), Telegram Bot API (`TelegramSender`), ArcGIS World Street Map tiles (Leaflet), Let's Encrypt (VM only, certbot container).
- Config (env form `Section__Key`): `ConnectionStrings:Postgres`; `InfluxDb:{Url,Org,Bucket,Token}`; `Mqtt:{Host,Port,UseTls,ClientId,Username,Password,SessionExpirySeconds,ReceiveMaximum,TelemetryTopicFilter,HeartbeatTopicFilter,CommandAckTopicFilter}`; `Jwt:{SecretKey,Issuer,Audience,AccessTokenMinutes,RefreshTokenDays}`; `Encryption:Key`; `Cors:AllowedOrigins:N`; `Firmware:{StoragePath,PublicBaseUrl}`; `Connectors:ServiceApiKey` (= Connectors' `ConnectorService:ApiKey`); `Bootstrap:{AdminEmail,AdminPassword}`; `HubManagement:ApplyEnabledHubIds`; `PostgresResilience:*`; `Metrics:PrometheusPort`.
- Local secrets: gitignored `appsettings.Development.json` (copy the `.example`), root `.env` for compose, frontend `.env.local` (`NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_WS_URL`, `NEXT_PUBLIC_ENABLE_REALTIME`).
