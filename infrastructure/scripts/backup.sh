#!/usr/bin/env bash
# Back up everything stateful: PostgreSQL (operational data + every raw
# message), InfluxDB (the telemetry bucket) and the Mosquitto persistence
# file (queued QoS 1 messages for a disconnected session).
#
# Mosquitto is included deliberately. Its queue is the only copy of a
# message that a device has published but ingestion has not yet stored, so a
# restore without it silently loses whatever was in flight.
#
# Usage:
#   ./backup.sh                     # -> ./backups/<timestamp>/
#   BACKUP_DIR=/mnt/backups ./backup.sh
#   S3_BUCKET=s3://acme-bms-backups ./backup.sh
#
# Restore is the other half, and it is not real until you have rehearsed it:
# see docs/deployment/production-readiness.md.
set -euo pipefail

cd "$(dirname "$0")/../.."

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
OUT="${BACKUP_DIR}/${STAMP}"
RETAIN_DAYS="${RETAIN_DAYS:-14}"

POSTGRES_DB="${POSTGRES_DB:-bms_platform}"
POSTGRES_USER="${POSTGRES_USER:-bms_app}"
INFLUXDB_ORG="${INFLUXDB_ORG:-bms-platform}"
INFLUXDB_BUCKET="${INFLUXDB_BUCKET:-telemetry}"

# .env holds the credentials the containers already use; don't duplicate them.
if [ -f .env ]; then
    set -a; . ./.env; set +a
fi

compose() { docker compose "$@"; }

fail() { echo "backup: $*" >&2; exit 1; }

mkdir -p "$OUT"
echo "backup: writing to $OUT"

# ---- PostgreSQL -----------------------------------------------------------
# Custom format (-Fc): compressed, and restorable table-by-table with
# pg_restore, which matters when you only need one table back.
echo "backup: postgres ..."
compose exec -T postgres pg_dump \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    --format=custom --compress=9 \
    > "$OUT/postgres.dump" || fail "pg_dump failed"

# ---- InfluxDB -------------------------------------------------------------
echo "backup: influxdb ..."
compose exec -T influxdb influx backup /tmp/influx-backup \
    --org "$INFLUXDB_ORG" \
    --bucket "$INFLUXDB_BUCKET" \
    --token "${INFLUXDB_ADMIN_TOKEN:?INFLUXDB_ADMIN_TOKEN must be set}" >/dev/null || fail "influx backup failed"
compose exec -T influxdb tar -C /tmp -cf - influx-backup > "$OUT/influxdb.tar" || fail "influx tar failed"
compose exec -T influxdb rm -rf /tmp/influx-backup

# ---- Mosquitto persistence ------------------------------------------------
# Ask the broker to flush first: the file on disk is only as fresh as the
# last autosave (60s by default), so without this you can lose a minute of
# queued messages from the backup even though they were in the broker.
echo "backup: mosquitto ..."
compose kill -s SIGUSR1 mosquitto >/dev/null 2>&1 || echo "backup: could not signal mosquitto, file may be up to autosave_interval stale"
sleep 2
compose exec -T mosquitto tar -C /mosquitto -cf - data > "$OUT/mosquitto-data.tar" || fail "mosquitto tar failed"

# ---- Manifest -------------------------------------------------------------
{
    echo "created_utc=$STAMP"
    echo "git_commit=$(git rev-parse HEAD 2>/dev/null || echo unknown)"
    echo "postgres_db=$POSTGRES_DB"
    echo "influx_org=$INFLUXDB_ORG"
    echo "influx_bucket=$INFLUXDB_BUCKET"
} > "$OUT/manifest.txt"

( cd "$OUT" && shasum -a 256 ./* > SHA256SUMS 2>/dev/null || sha256sum ./* > SHA256SUMS )

echo "backup: done"
du -sh "$OUT"/* | sed 's/^/  /'

# ---- Off-host copy --------------------------------------------------------
# A backup on the same host as the database is not a backup; it dies with
# the disk it was protecting against.
if [ -n "${S3_BUCKET:-}" ]; then
    echo "backup: uploading to ${S3_BUCKET}/${STAMP}/ ..."
    aws s3 sync "$OUT" "${S3_BUCKET}/${STAMP}/" --only-show-errors || fail "s3 sync failed"
    echo "backup: uploaded"
else
    echo "backup: S3_BUCKET not set — this copy is still on the same host as the data it protects"
fi

# ---- Local retention ------------------------------------------------------
find "$BACKUP_DIR" -maxdepth 1 -type d -name '20*Z' -mtime "+${RETAIN_DAYS}" -exec rm -rf {} + 2>/dev/null || true
