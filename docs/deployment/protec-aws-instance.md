# Protec AWS deployment

The application is deployed at **https://52.74.238.107**, with a browser-trusted Let's Encrypt IP certificate. This is a fresh database: local companies and historical readings were not imported. A new Protec company, site, BW-NB-WM400 connector, and inclinometer 100000005 were configured. One simulated reading was sent for end-to-end verification; its first reading becomes the onboarding reference.

## Access

- SSH: `ssh -i protecdemo.pem ubuntu@52.74.238.107`
- Administrator login: `admin@protec.local`
- Generated password: private local file `C:\work\iot monitoring\.tools\aws-private\fresh-aws-access.txt`; not committed or included in this document.
- Runtime secrets: `/opt/protec/shared/cloud.env`, restricted to the deployment owner/root.
- Current application source: `/opt/protec/current`, a symlink into `/opt/protec/releases/`.
- Production Compose files: `docker-compose.yml` plus `docker-compose.aws.yml`, project name `protec`.

Keep the public IP stable. The certificate is issued for this IP; changing it requires new public configuration and a replacement certificate. Elastic IP association was not verified because no AWS control-plane role was available.

## Services and ports

Three backend services (`api`, `ingestion`, `connectors`), one standalone Next.js frontend, PostgreSQL, InfluxDB, Mosquitto and Nginx run in Docker. Only 80/443 and sensor TCP 12000 are published publicly. API 5000, frontend 3000 and ingestion metrics 9464 bind to loopback. PostgreSQL, InfluxDB and MQTT have no host port publishing. SSH remains governed by the existing EC2 security group.

The frontend calls the API and SignalR using the same HTTPS origin. Its Docker build explicitly embeds empty public URL prefixes; it does not call a user's localhost. Nginx forwards `/api/` and `/hubs/` to the API and other routes to Next.js. Request logs omit query strings so SignalR access tokens are not logged.

Production bootstrap creates five roles and one administrator with a generated password; demo seeding runs only in Development. Restarting production preserves existing users and passwords. An empty production database refuses startup without a strong bootstrap password. Influx and MQTT credentials are separate from application login credentials.

## Commands on the VM

```bash
sudo /usr/local/sbin/protec-compose ps
sudo /usr/local/sbin/protec-compose logs --tail=100 api ingestion connectors web proxy
curl -fsS https://52.74.238.107/health
sudo systemctl status protec-stack.service
sudo systemctl list-timers protec-cert-renew.timer protec-backup.timer
```

Compose services use `restart: unless-stopped`. `protec-stack.service` brings up the same project after reboot. The VM has 4 GB RAM, 4 GB swap, and a 30 GB root disk; build services sequentially. Logs rotate at 10 MB with three files per container.

Do not use `docker compose down -v`: that removes database volumes. Do not change the encryption key for an existing database; stored MQTT/SMTP credentials depend on it. Do not start a second ingestion instance using the same MQTT client ID.

## Certificates and backups

The IP certificate uses the Let's Encrypt `shortlived` profile. `protec-cert-renew.timer` checks hourly and reloads Nginx after a successful check. Both issuance and a renewal check were verified.

`protec-backup.timer` runs daily at 19:00 UTC (03:00 Singapore the following day), with up to ten minutes of jitter. Backups include PostgreSQL, InfluxDB, secrets and service configuration. Completed backups are under `/opt/protec/backups/<UTC timestamp>/COMPLETE`; older backups are retained for at least seven days. The first backup completed successfully.

These backups are on the same VM disk. **Off-server/S3 backup storage has not been configured.** An IAM role and an appropriate backup destination are needed before claiming protection against loss of the VM disk.

Manual checks:

```bash
sudo systemctl start protec-backup.service
sudo journalctl -u protec-backup.service -n 20 --no-pager
sudo systemctl start protec-cert-renew.service
sudo journalctl -u protec-cert-renew.service -n 20 --no-pager
```

## Sensor publishing

The connector expects joined `BW-NB-WM400,` records with ten CSV fields after the prefix. It maps X/Y/Z degrees, X/Y/Z acceleration, temperature and battery; the phone number is ignored. Daily tilt closes at 23:59 Asia/Singapore. Existing references and saved daily rows remain fixed.

From `simulators/python-publisher` on Windows:

```powershell
.\.venv\Scripts\python.exe send_tcp_fgwm400.py --host 52.74.238.107 --port 12000 --text-layout extended --prefix --phone --no-newline --interval 300 --x -16.6001 --y -0.2656 --z 77.0541 --temperature 31.0773 --battery 5
```

This sends ten simulated readings per burst and waits 300 seconds between bursts. Use `--once --burst 1` for one test reading. No continuous simulator was left running by deployment.

## Verification

- Docker builds succeeded for all three backend services and the standalone frontend.
- Fourteen API integration tests passed, including fresh production bootstrap, strong-password validation, no demo data and restart idempotence.
- Public HTTPS chain verified without disabling certificate checks.
- Production administrator login succeeded; the old default demo login failed.
- Exactly one fresh company, one device and one administrator were confirmed.
- Public TCP 12000 → MQTT → ingestion → InfluxDB → REST → secure SignalR WebSocket verified with one test reading.
- Three gauges and the daily table displayed on desktop; mobile viewport 390 px had no page overflow; no browser requests went to localhost and no runtime exceptions were observed.

Installer sources are in `infrastructure/deploy/`; they contain no runtime secrets. `initialize-stack.sh` is for an empty server only and refuses an existing deployment. Review actual settings and backups before any future upgrade.
