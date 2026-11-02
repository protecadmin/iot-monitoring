#!/usr/bin/env bash
set -euo pipefail
base=/opt/protec/shared
run_certbot() {
  docker run --rm -v "$base/letsencrypt:/etc/letsencrypt" -v "$base/acme:/var/www/certbot" certbot/certbot:latest "$@"
}
run_certbot certonly --staging --non-interactive --agree-tos --register-unsafely-without-email --preferred-profile shortlived --webroot --webroot-path /var/www/certbot --ip-address 52.74.238.107 --cert-name protec-ip-test
run_certbot certonly --non-interactive --agree-tos --register-unsafely-without-email --preferred-profile shortlived --webroot --webroot-path /var/www/certbot --ip-address 52.74.238.107 --cert-name protec-ip
openssl x509 -in "$base/letsencrypt/live/protec-ip/fullchain.pem" -noout -issuer -dates -ext subjectAltName
cat > /usr/local/sbin/protec-renew-cert <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
exec 9>/run/protec-renew-cert.lock
flock -n 9 || exit 0
docker run --rm -v /opt/protec/shared/letsencrypt:/etc/letsencrypt -v /opt/protec/shared/acme:/var/www/certbot certbot/certbot:latest renew --quiet
docker exec protec-proxy-1 nginx -t
docker exec protec-proxy-1 nginx -s reload
EOF
chmod 750 /usr/local/sbin/protec-renew-cert
cat > /etc/systemd/system/protec-cert-renew.service <<'EOF'
[Unit]
Description=Renew Protec IP HTTPS certificate
After=docker.service network-online.target
Requires=docker.service
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/protec-renew-cert
EOF
cat > /etc/systemd/system/protec-cert-renew.timer <<'EOF'
[Unit]
Description=Check Protec short-lived HTTPS certificate every hour
[Timer]
OnBootSec=10min
OnUnitActiveSec=1h
RandomizedDelaySec=5min
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now protec-cert-renew.timer
