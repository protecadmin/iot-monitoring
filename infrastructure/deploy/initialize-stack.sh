#!/usr/bin/env bash
set -euo pipefail
release_id="$1"
case "$release_id" in *[!0-9-]*|'') exit 2;; esac
release="/opt/protec/releases/$release_id"
if [ -f /opt/protec/shared/cloud.env ]; then echo 'A deployment already exists. Refusing fresh initialization; use the existing environment for upgrades.'; exit 1; fi
install -d -o ubuntu -g ubuntu -m 0750 "$release" /opt/protec/shared/mosquitto /opt/protec/shared/acme /opt/protec/shared/letsencrypt
tar -xzf /tmp/protec-release.tar.gz -C "$release"
chown -R ubuntu:ubuntu "$release"
install -o ubuntu -g ubuntu -m 0600 /tmp/protec-fresh.env /opt/protec/shared/cloud.env
sed -i 's/\r$//' /opt/protec/shared/cloud.env
rm /tmp/protec-fresh.env
ln -sfn "$release" /opt/protec/current
ln -sfn /opt/protec/shared/cloud.env "$release/.env"
set -a
source /opt/protec/shared/cloud.env
set +a
cd "$release"
dc() { docker compose -p protec --env-file /opt/protec/shared/cloud.env -f docker-compose.yml -f docker-compose.aws.yml "$@"; }
docker pull iegomez/mosquitto-go-auth:latest
python3 - <<'PY'
import os
from pathlib import Path
text=Path('infrastructure/mosquitto/mosquitto.conf.example').read_text()
lines=[];skip=False
for line in text.splitlines():
    if line.strip()=='listener 8883':skip=True
    if skip and line.strip()=='allow_anonymous false':skip=False
    if skip:continue
    values={'auth_opt_pg_host':'postgres','auth_opt_pg_dbname':os.environ['POSTGRES_DB'],'auth_opt_pg_user':os.environ['POSTGRES_USER'],'auth_opt_pg_password':os.environ['POSTGRES_PASSWORD']}
    key=line.split(' ',1)[0]
    if key in values:line=key+' '+values[key]
    lines.append(line)
Path('/opt/protec/shared/mosquitto/mosquitto.conf').write_text('\n'.join(lines)+'\n')
Path('/opt/protec/shared/nginx.conf').write_text('''server {
    listen 80;
    server_name 52.74.238.107;
    location ^~ /.well-known/acme-challenge/ { root /var/www/certbot; }
    location = /deployment-status { default_type text/plain; return 200 "Deployment in progress\\n"; }
    location / { return 503; }
}
''')
Path('/opt/protec/shared/acme/.well-known/acme-challenge').mkdir(parents=True,exist_ok=True)
Path('/opt/protec/shared/acme/.well-known/acme-challenge/protec-check').write_text('protec-acme-ready\n')
PY
chmod -R a+rX /opt/protec/shared/acme
pwhash() { docker run --rm --entrypoint /mosquitto/pw iegomez/mosquitto-go-auth:latest -h bcrypt -p "$1"; }
password_file=/opt/protec/shared/mosquitto/passwd
printf 'bms-api-realtime-bridge:%s\n' "$(pwhash "$MQTT_BRIDGE_PASSWORD")" > "$password_file"
printf 'bms-ingestion:%s\n' "$(pwhash "$MQTT_INGESTION_PASSWORD")" >> "$password_file"
printf 'bms-connectors:%s\n' "$(pwhash "$MQTT_CONNECTORS_PASSWORD")" >> "$password_file"
broker_uid=$(docker run --rm --entrypoint id iegomez/mosquitto-go-auth:latest -u mosquitto)
broker_gid=$(docker run --rm --entrypoint id iegomez/mosquitto-go-auth:latest -g mosquitto)
chown "$broker_uid:$broker_gid" /opt/protec/shared/mosquitto/mosquitto.conf "$password_file"
chmod 600 /opt/protec/shared/mosquitto/mosquitto.conf "$password_file"
dc config --quiet
dc up -d postgres influxdb proxy
dc ps
