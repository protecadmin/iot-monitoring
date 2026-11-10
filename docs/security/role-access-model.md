# Pro-Tec role and access model

This document describes the implemented four-role model and the rollout rules for existing accounts. The API is the security boundary; the navigation changes only hide actions that the API would reject.

## Roles

| Role | Scope | Intended access |
| --- | --- | --- |
| `SuperAdmin` | Platform-wide | Technical administration, all tenants/sites, users, gateways, hubs, connectors, device types, firmware, rules, audit and notification settings. |
| `Executive` | All companies | Read-only business visibility across tenants: dashboard, companies, sites, monitoring, alerts and reports. No technical configuration or user management. |
| `CompanyAdmin` | One tenant | Manage the tenant's sites and users (only `SiteAdmin` users), view devices/telemetry/reports, acknowledge alerts. No platform infrastructure or technical configuration. |
| `SiteAdmin` | One tenant plus explicit site assignments | View and monitor assigned sites/devices, telemetry, reports and alerts; acknowledge alerts for assigned sites. No tenant-wide administration or technical configuration. |

Legacy `Admin`, `Engineer`, `Viewer` and `Customer` role rows are preserved for review. They are not silently migrated and cannot authenticate after the access middleware is enabled until a SuperAdmin explicitly reviews and changes each account to one of the four roles.

## Scope enforcement

- The authenticated user's role, tenant and site assignments are reloaded from the database on every API request. Stale JWT role claims cannot grant access after a role, activation state or assignment changes.
- CompanyAdmin and SiteAdmin requests are tenant-scoped. SiteAdmin queries are additionally restricted to `UserSiteAccess` assignments.
- Site and device IDs, exports, history, realtime events and direct URLs are checked against the same scope. A forged tenant/site/device ID returns `403`.
- Site assignments must be non-empty for SiteAdmin users and every assigned site must belong to the user's tenant. Cross-tenant assignments are rejected.
- CompanyAdmin can create and manage SiteAdmin accounts in its own tenant. Only SuperAdmin can create Executive or CompanyAdmin accounts and perform platform-wide user changes.
- Realtime delivery re-evaluates current database scope for each event, so removing an assignment or deactivating a user stops subsequent events without relying on an old socket claim.

## Navigation and route matrix

| Area | SuperAdmin | Executive | CompanyAdmin | SiteAdmin |
| --- | --- | --- | --- | --- |
| Dashboard / companies / sites / monitoring / reports | Read + manage as applicable | Read | Own tenant | Assigned sites |
| Alerts | Read, acknowledge, resolve | Read | Read + acknowledge | Read + acknowledge assigned |
| Users | All roles and tenants | None | SiteAdmins in own tenant | None |
| Tenant/site administration | All | Read-only company/site view | Own tenant sites | None |
| Gateways, hubs, connectors | Full | None | None | None |
| Device types, firmware, MQTT/connector settings | Full | None | None | None |
| Commands, device configuration, calculations/rules | Full | None | None | None |
| Audit log and notification/platform settings | Full | None | None | None |

The frontend uses the same capability names for navigation and button visibility, but those checks are defense-in-depth. Backend policy/filter checks remain authoritative.

## Database and migration

The additive migration `20261106061507_AddRoleSiteAccess`:

- creates the four final role rows when absent;
- adds `UserSiteAccess(UserId, SiteId)` with a composite key and foreign keys;
- leaves existing role rows and users unchanged;
- does not add site IDs to JWTs or local storage.

Assignments are read from the database for each request and realtime dispatch. Fresh development seeding creates a SuperAdmin, CompanyAdmin and SiteAdmin examples plus an Executive account; production users are not reseeded.

## Production rollout checklist

1. Review the existing user roster and each legacy/Customer account's tenant, sites and required role.
2. Create or update users with the Users API, assigning SiteAdmin site IDs explicitly.
3. Verify CompanyAdmin and SiteAdmin accounts can only access their intended tenant/sites; verify Executive is read-only.
4. Apply the additive migration during a maintenance window. Existing legacy accounts remain blocked until reviewed, preventing accidental privilege grants.
5. Re-authenticate reviewed users so the client receives the current role; no frontend local-storage value is treated as authorization.
6. Run the integration suite and smoke-test direct URLs, exports, realtime events and alert acknowledgement.

The production database was only inspected during implementation; no user roles or assignments were changed automatically. The current read-only review found one active SuperAdmin and four Customer rows (three active), all requiring an explicit migration decision before rollout.

## Verification

- Backend integration tests: **31 passed, 0 failed**.
- Frontend TypeScript check: passed.
- Frontend production build: passed.
- Targeted frontend lint: passed with the existing non-blocking `TenantScope` effect warning.

Known non-blocking build warnings are existing package vulnerability-feed/network warnings, the existing Program type-name collision, and an existing xUnit analyzer warning.
