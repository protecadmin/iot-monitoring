#!/usr/bin/env bash
set -euo pipefail
cd /opt/protec/current
dc() { docker compose -p protec --env-file /opt/protec/shared/cloud.env -f docker-compose.yml -f docker-compose.aws.yml "$@"; }
for service in api ingestion connectors web; do
    printf '\nBuilding %s\n' "$service"
    BUILDKIT_PROGRESS=plain dc build "$service"
done
printf '\nAll application images built.\n'
df -h /
