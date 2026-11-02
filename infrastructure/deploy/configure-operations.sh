#!/usr/bin/env bash
set -euo pipefail
cat > /usr/local/sbin/protec-compose <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
cd /opt/protec/current
exec docker compose -p protec --env-file /opt/protec/shared/cloud.env -f docker-compose.yml -f docker-compose.aws.yml "$@"
EOF
chmod 750 /usr/local/sbin/protec-compose
cat > /etc/systemd/system/protec-stack.service <<'EOF'
[Unit]
Description=Protec Smart Monitoring Docker stack
Requires=docker.service
After=docker.service network-online.target
[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=/opt/protec/current
ExecStart=/usr/local/sbin/protec-compose up -d --no-build --pull never
ExecStop=/usr/local/sbin/protec-compose stop
TimeoutStartSec=240
TimeoutStopSec=90
[Install]
WantedBy=multi-user.target
EOF
cat > /usr/local/sbin/protec-backup <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
umask 077
exec 9>/run/protec-backup.lock
flock -n 9 || exit 0
source /opt/protec/shared/cloud.env
stamp=$(date -u +%Y%m%dT%H%M%SZ)
target="/opt/protec/backups/$stamp"
mkdir -m 0700 "$target"
docker exec protec-postgres-1 pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > "$target/postgres.dump"
docker exec -e INFLUX_TOKEN="$INFLUXDB_ADMIN_TOKEN" protec-influxdb-1 influx backup "/tmp/protec-backup-$stamp"
docker cp "protec-influxdb-1:/tmp/protec-backup-$stamp" "$target/influx"
docker exec protec-influxdb-1 rm -rf "/tmp/protec-backup-$stamp"
cp /opt/protec/shared/cloud.env "$target/cloud.env"
cp /opt/protec/shared/onboarding.json "$target/onboarding.json"
tar -czf "$target/service-config.tar.gz" -C /opt/protec/shared mosquitto nginx.conf
printf 'complete\n' > "$target/COMPLETE"
find /opt/protec/backups -mindepth 1 -maxdepth 1 -type d -name '20*T*Z' -mtime +7 -exec rm -rf -- {} +
printf 'Backup complete: %s\n' "$stamp"
EOF
chmod 750 /usr/local/sbin/protec-backup
cat > /etc/systemd/system/protec-backup.service <<'EOF'
[Unit]
Description=Back up Protec databases and configuration
After=docker.service
Requires=docker.service
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/protec-backup
EOF
cat > /etc/systemd/system/protec-backup.timer <<'EOF'
[Unit]
Description=Daily Protec database backup
[Timer]
OnCalendar=*-*-* 19:00:00 UTC
RandomizedDelaySec=10min
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now protec-stack.service
systemctl enable --now protec-backup.timer
systemctl start protec-backup.service
systemctl start protec-cert-renew.service
systemctl is-active protec-stack.service protec-backup.timer protec-cert-renew.timer
curl -fsS http://127.0.0.1:3000/login > /dev/null
curl -fsS https://52.74.238.107/health
