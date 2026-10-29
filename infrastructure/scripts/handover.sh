#!/usr/bin/env bash
# Print the installation handover pack for a site: every device, with the
# exact connection settings its installer needs.
#
# Run this BEFORE the site visit and check it against what you expect —
# a wrong topic or a missing payload mapping fails silently once you are
# on site, and looks identical to a dead device.
#
# Usage:
#   ./handover.sh abc-manufacturing bangalore-factory
#   PGHOST=db.internal ./handover.sh abc-manufacturing bangalore-factory
#
# The MQTT password is deliberately NOT printed. It is per tenant, and
# lives in the dashboard under MQTT Credentials — fetch it from there and
# send it over a different channel than this sheet.
set -euo pipefail

cd "$(dirname "$0")/../.."

TENANT="${1:-}"
SITE="${2:-}"

if [ -z "$TENANT" ] || [ -z "$SITE" ]; then
    echo "usage: $0 <tenant-slug> <site-code>" >&2
    echo >&2
    echo "Sites available:" >&2
    SHOW_SITES=1
fi

if [ -f .env ]; then
    set -a; . ./.env; set +a
fi

export PGHOST="${PGHOST:-127.0.0.1}"
export PGPORT="${PGPORT:-${POSTGRES_PORT:-5432}}"
export PGUSER="${PGUSER:-${POSTGRES_USER:-bms_app}}"
export PGPASSWORD="${PGPASSWORD:-${POSTGRES_PASSWORD:-}}"
PGDB="${POSTGRES_DB:-bms_platform}"

if [ "${SHOW_SITES:-0}" = "1" ]; then
    psql -d "$PGDB" -c '
        SELECT t."Slug" AS tenant_slug, s."Code" AS site_code,
               t."Name" AS company, s."Name" AS site, count(d."Id") AS devices
        FROM "Tenants" t
        JOIN "Sites" s ON s."TenantId" = t."Id"
        LEFT JOIN "Devices" d ON d."SiteId" = s."Id"
        GROUP BY 1,2,3,4 ORDER BY 3,4;'
    exit 1
fi

psql -d "$PGDB" -v tenant="$TENANT" -v site="$SITE" <<'SQL'
\pset border 2
\echo
\echo '=============================================================='
\echo ' INSTALLATION HANDOVER PACK'
\echo '=============================================================='

SELECT t."Name" AS company, t."Slug" AS tenant_slug,
       s."Name" AS site, s."Code" AS site_code,
       count(d."Id") AS device_count
FROM "Tenants" t
JOIN "Sites" s ON s."TenantId" = t."Id"
LEFT JOIN "Devices" d ON d."SiteId" = s."Id"
WHERE t."Slug" = :'tenant' AND s."Code" = :'site'
GROUP BY 1,2,3,4;

\echo '--- MQTT login (same for every device of this company) --------'

SELECT mc."Username" AS mqtt_username,
       CASE WHEN mc."Id" IS NULL
            THEN '!! NO CREDENTIAL — devices cannot connect. Create it first.'
            ELSE 'password: see dashboard > MQTT Credentials' END AS password
FROM "Tenants" t
LEFT JOIN "MqttCredentials" mc ON mc."TenantId" = t."Id"
WHERE t."Slug" = :'tenant';

\echo '--- Per device ------------------------------------------------'

SELECT d."DeviceCode"  AS device_code,
       d."Name"        AS device_name,
       dt."Name"       AS device_type,
       'iot/' || t."Slug" || '/' || s."Code" || '/' || d."DeviceCode" || '/telemetry' AS mqtt_topic,
       (SELECT string_agg(p."Key", ', ' ORDER BY p."Key") FROM "DeviceTypeParameters" p
         WHERE p."DeviceTypeId" = d."DeviceTypeId") AS gateway_sends_keys,
       d."Status" AS status
FROM "Devices" d
JOIN "Sites" s        ON s."Id"  = d."SiteId"
JOIN "Tenants" t      ON t."Id"  = s."TenantId"
JOIN "DeviceTypes" dt ON dt."Id" = d."DeviceTypeId"
WHERE t."Slug" = :'tenant' AND s."Code" = :'site'
ORDER BY d."DeviceCode";

\echo '--- Pre-visit checks ------------------------------------------'

-- Each row returned here is a problem to fix BEFORE travelling.
SELECT d."DeviceCode" AS device_code, problem
FROM "Devices" d
JOIN "Sites" s   ON s."Id" = d."SiteId"
JOIN "Tenants" t ON t."Id" = s."TenantId"
CROSS JOIN LATERAL (
    SELECT unnest(ARRAY[
        CASE WHEN NOT EXISTS (SELECT 1 FROM "DeviceTypeParameters" p
                              WHERE p."DeviceTypeId" = d."DeviceTypeId")
             THEN 'device type has no parameters — every field will be dropped' END,
        CASE WHEN s."Code" <> lower(s."Code")
             THEN 'site code is not lowercase — MQTT topics are case-sensitive' END,
        CASE WHEN d."DeviceCode" <> lower(d."DeviceCode")
             THEN 'device code is not lowercase — MQTT topics are case-sensitive' END
    ]) AS problem
) checks
WHERE t."Slug" = :'tenant' AND s."Code" = :'site' AND problem IS NOT NULL
ORDER BY 1;

\echo '(no rows above = nothing blocking; see docs/operations/onboarding.md)'
\echo
SQL
