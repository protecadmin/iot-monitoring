# API Design

## Surface (implemented starting later phases; documented now for the boundary)

```
POST   /api/auth/login

GET    /api/tenants                       POST /api/tenants
GET    /api/sites                         POST /api/sites
GET    /api/devices                       POST /api/devices
GET    /api/devices/{id}                  PUT  /api/devices/{id}     DELETE /api/devices/{id}

GET    /api/devices/{id}/telemetry
GET    /api/telemetry/latest
GET    /api/telemetry/history

GET    /api/alerts
POST   /api/alerts/{id}/acknowledge
POST   /api/alerts/{id}/resolve

POST   /api/devices/{id}/commands

GET    /api/devices/{id}/configuration    PUT /api/devices/{id}/configuration

GET    /api/firmware                      POST /api/firmware
POST   /api/firmware/deploy


GET    /api/devices/{id}/raw-messages
POST   /api/raw-messages/{id}/replay

GET    /api/reports/telemetry
GET    /api/reports/alerts

```

## Conventions

- **DTOs only** — EF Core entities are never returned directly from an
  endpoint. A DTO decouples the wire contract from the persistence model,
  so a schema refactor doesn't automatically become a breaking API change.
- **Validation** at the API boundary (model binding + explicit rules)
  before anything reaches the application layer.
- **Pagination, filtering, sorting** on every list endpoint (`/api/devices`,
  `/api/alerts`, etc.) — returning an unbounded collection doesn't survive
  past a handful of devices.
- **Consistent error shape**:

```json
{
  "success": false,
  "message": "Device not found",
  "errorCode": "DEVICE_NOT_FOUND",
  "traceId": "..."
}
```

  via centralized exception handling (one middleware, not per-controller
  try/catch), covering 400/401/403/404/409/422/500. `traceId` ties an error
  back to structured logs — see [core.txt](../../core.txt) section 44.

## Authentication & authorization

JWT-based login (`POST /api/auth/login`), with refresh tokens where
appropriate, and role-based authorization:

**Roles**: SuperAdmin, Admin, Engineer, Viewer, Customer.

**Permissions** (checked per-endpoint, not per-role — roles are just named
permission sets): `Tenant.View`, `Tenant.Manage`, `Site.View`,
`Site.Manage`, `Device.View`, `Device.Manage`, `Telemetry.View`,
`Alert.View`, `Alert.Manage`, `Command.Execute`, `Report.Export`,
`Firmware.Manage`, `User.Manage`.

## Tenant isolation is mandatory

Every query that can return device/site/telemetry/alert data is scoped to
the caller's tenant(s) at the query layer — not just filtered in the API
response. A `Customer`-role user for tenant-001 must never be able to
retrieve tenant-002's data by guessing an id. This is enforced consistently
because the domain schema itself carries `TenantId` on every relevant
entity — see [schema.md](../database/schema.md).

## Real-time delivery

REST covers request/response (fetch history, issue a command). SignalR
covers push (live telemetry, alert notifications, command
acknowledgements) — see [data-flow.md](../architecture/data-flow.md). The
two are complementary, not overlapping: a client fetches initial state over
REST, then subscribes for updates over SignalR.
