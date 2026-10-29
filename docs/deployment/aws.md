# Deploying BMS on AWS — end to end

This is the full path from an empty AWS account to real gateways publishing
into a production BMS, on **one EC2 instance running the Compose stack you
already run locally**. Follow it top to bottom; every section ends with how
to verify it, because "configured" and "working" are not the same thing.

It is deliberately the smallest architecture that can carry real devices.
See [What to do when you outgrow this](#13-what-to-outgrow-this-with-and-when)
for the next shapes (RDS, ECS, AWS IoT Core) and the signals that you need
them.

**Companion documents.** This guide covers *AWS*. It does not repeat the
sizing, retention, backup and alerting decisions, which are the same
wherever you host:

- [production-readiness.md](production-readiness.md) — what has to be true
  before real devices depend on it, and what breaks if you skip each item
- [go-live-checklist.md](go-live-checklist.md) — the same work as a
  dependency-ordered checklist with time estimates
- [docker.md](docker.md) — what each container is and why api/ingestion are
  separate deployables

---

## 0. What you are building

```
                              Internet
        443/80 │          8883 (MQTTS) │       12000-12099 (TCP) │
               ▼                       ▼                        ▼
 ┌────────────────────────────────────────────────────────────────────────┐
 │ EC2 · one instance · Elastic IP · Amazon Linux 2023 · Docker           │
 │                                                                        │
 │   caddy ──┬──► web  (Next.js :3000)                                    │
 │           └──► api  (ASP.NET :8080 · REST · SignalR)                   │
 │                                                                        │
 │   mosquitto  (:8883 devices · :1883 internal only)                     │
 │        ▲ publish            │ subscribe                                │
 │        │                    ▼                                          │
 │   connectors               ingestion  (worker · single instance)       │
 │   (:12000-12099)                │                                      │
 │                                 ▼                                      │
 │   postgres :5432                          influxdb :8086               │
 │   (domain data · raw messages ·            (telemetry)                 │
 │    the broker's device auth)                                           │
 │                                                                        │
 │   volumes pgdata · influxdata · mosquitto-data · firmware-data         │
 │           all on the EBS data volume mounted at /data                  │
 └────────────────────────────────────────────────────────────────────────┘
        │                      │                       │
        ▼                      ▼                       ▼
   Route 53 (DNS)      SSM Parameter Store        S3 (backups)
                          (every secret)     CloudWatch (logs · alarms)
```

Everything except DNS, secrets, backups and monitoring runs in containers on
one host. One host because:

- **Ingestion cannot be horizontally scaled as configured.** It connects
  with the fixed client id `bms-ingestion` and a persistent session — that is
  what makes the broker queue QoS 1 telemetry while it restarts. Two
  instances with the same id fight over the session and *lose messages*. See
  [docs/mqtt/reliability.md](../mqtt/reliability.md).
- **Mosquitto's persistence file is state.** It holds the only copy of
  messages a device has published but ingestion has not yet stored, so the
  broker is not a stateless service you can move freely.
- At one site-monitoring firm's scale (hundreds of devices, minute-scale
  intervals), one instance has an order of magnitude of headroom, and a
  second host buys complexity rather than availability.

**What this does not give you:** multi-AZ failover, zero-downtime deploys,
or surviving the loss of the instance without a restore. A deploy is a
~30 second gap in ingestion (the broker queues through it) and a few seconds
of dashboard downtime. If that is not acceptable, you are past this
architecture — jump to section 13.

### Rough cost

Singapore (`ap-southeast-1`), on-demand, **estimates — check the AWS
calculator before committing**:

| Item | Spec | ~US$/month |
|---|---|---|
| EC2 | `t3.large` (2 vCPU, 8 GB) | 75–80 |
| EBS root + data | 30 GB + 120 GB gp3 | 15 |
| Public IPv4 | 1 Elastic IP, in use | 4 |
| S3 backups | ~50 GB with lifecycle rules | 2 |
| CloudWatch | logs + a handful of alarms | 3–6 |
| Route 53 | 1 hosted zone | 1 |
| **Total** | | **~100–110** |

A 1-year Compute Savings Plan takes roughly 30% off the EC2 line. Start
on-demand; commit once the instance size has stopped changing.

---

## 1. Decisions to make before you touch AWS

Phase 0 of the [go-live checklist](go-live-checklist.md#phase-0--before-you-touch-anything)
is the authoritative list. The ones that change what you build on AWS:

| Decision | Why it changes the infrastructure |
|---|---|
| **Region** | Put it closest to the devices — `ap-southeast-1` for Singapore. MQTT round trips and gateway reconnects are what you are optimising, not dashboard page loads. |
| **Three DNS names** | `app.` (dashboard), `api.` (REST + SignalR + firmware downloads), `mqtt.` (broker). Three names, one Elastic IP. The broker needs its own name because its certificate is issued separately from the web ones. |
| **Device count × interval × payload size** | Feeds broker queue sizing and therefore the EBS data volume size. `python3 infrastructure/mosquitto/size-queue.py --devices N --interval S --outage 15m` |
| **Raw message retention days** | The single biggest driver of PostgreSQL disk growth. 30 days is the default. |
| **PostgreSQL: container or RDS** | Start with the container (this guide). RDS when you want point-in-time recovery you did not write yourself — section 13. |

Write the three DNS names down now; they appear in five configuration values
later and a mismatch is the single most common cause of "the dashboard loads
but nothing works".

---

## 2. Account preparation

1. **Stop using the root user.** Create an IAM user (or an IAM Identity
   Center user) with `AdministratorAccess` for yourself, enable MFA on both
   it and root, and never log in as root again.
2. **Budget alarm.** Billing → Budgets → a monthly cost budget at roughly
   150% of the estimate above, alerting your email. This is the cheapest
   insurance against a misconfiguration that runs all month.
3. **Region.** Select your chosen region in the console and stay in it.
   Resources are regional; half a stack in Virginia is a common and
   annoying mistake.
4. **Key pair.** EC2 → Key Pairs → create `bms-prod`, type `ed25519`,
   format `.pem`. Store the private key in your password manager, not just
   `~/.ssh`.

> Prefer **SSM Session Manager** over SSH for day-to-day access (section 5):
> no inbound port 22, no key to lose, and every session is logged.

---

## 3. Network and firewall

The default VPC is fine. You need one public subnet, which the default VPC
already has in every AZ.

### 3.1 Elastic IP

Allocate one (EC2 → Elastic IPs → Allocate) **before** launching, and
associate it in section 5.

This matters more here than in most deployments: every gateway in the field
is configured with the broker's address. A stop/start of an instance without
an Elastic IP changes its public IP, which silently disconnects your entire
fleet and is fixed only by re-configuring each gateway. Treat the Elastic IP
as part of the device contract.

### 3.2 Security group

Create `bms-prod-sg` with these inbound rules and nothing else:

| Port | Source | Why |
|---|---|---|
| 443 | `0.0.0.0/0` | Dashboard + API + SignalR |
| 80 | `0.0.0.0/0` | ACME HTTP-01 challenge and the redirect to 443 |
| 8883 | your gateways' egress IPs if they are static, else `0.0.0.0/0` | MQTTS. Devices. |
| 12000–12099 | same as above | Connector Service — devices that speak their own format over TCP. Open the whole range **once**; adding a vendor then never needs an infrastructure change ([connectors.md](../operations/connectors.md)). |
| 22 | your office IP only, or omit entirely if using Session Manager | SSH |

**Deliberately absent: 1883, 5432, 8086, 9464.** Plain MQTT, PostgreSQL,
InfluxDB and the metrics endpoints must never be reachable from the
internet. The containers reach each other over the Compose network, not
through the host, so nothing in the stack needs these published at all —
section 7.4 binds them to loopback so that the security group is not the
only thing standing between the internet and your database.

Outbound: leave the default allow-all. Note that **AWS blocks outbound port
25**, so alert emails must go out over 587/465 through SES or your mail
provider (section 10.3).

### 3.3 DNS

In Route 53 (or your existing registrar), create three **A records** pointing
at the Elastic IP:

```
app.bms.example.com   A   <elastic-ip>
api.bms.example.com   A   <elastic-ip>
mqtt.bms.example.com  A   <elastic-ip>
```

**Verify:** `dig +short app.bms.example.com` returns the Elastic IP from
outside AWS. Do this before section 9 — Caddy's certificate request fails if
DNS has not propagated.

---

## 4. Storage layout

Two EBS volumes, both gp3:

| Volume | Size | Mount | Holds |
|---|---|---|---|
| Root | 30 GB | `/` | OS, Docker engine, container images (the .NET build caches are not small) |
| Data | 120 GB to start | `/data` | **Every Docker volume**: `pgdata`, `influxdata`, `mosquitto-data`, `firmware-data` |

Separating them is what lets you grow the data disk without rebuilding the
instance, and keeps a runaway log from filling the same filesystem as the
database.

**Sizing the data volume.** The three growing things:

- `RawDeviceMessages` — the full payload of every message, kept for
  `RetentionDays`. `devices × messages/day × payload bytes × days`, roughly
  tripled for indexes and row overhead. 300 devices at one message a minute
  with a 200-byte payload and 30 days ≈ 8 GB.
- InfluxDB — compresses well; assume a quarter of the above.
- `mosquitto-data` — at least `MOSQUITTO_MAX_QUEUED_BYTES` (default 100 MiB)
  with headroom.

gp3 volumes grow online (`modify-volume`, then `growpart` + `xfs_growfs`),
so start at 120 GB and extend rather than over-provisioning. Set a
CloudWatch alarm at 75% (section 10.2) — a full disk stops the raw-message
insert, which is the one write in this system that must never fail.

---

## 5. Launch the instance

EC2 → Launch instance:

| Field | Value |
|---|---|
| Name | `bms-prod` |
| AMI | **Amazon Linux 2023**, x86_64 |
| Instance type | `t3.large` |
| Key pair | `bms-prod` |
| Network | default VPC, a public subnet, **auto-assign public IP enabled** |
| Security group | `bms-prod-sg` |
| Storage | 30 GB gp3 root, **plus** a second 120 GB gp3 volume |
| IAM instance profile | `bms-prod-role` — create it in section 6 and attach after |

**Why x86_64 and not Graviton.** The broker image
(`iegomez/mosquitto-go-auth`, which provides the PostgreSQL-backed device
authentication the platform relies on) is not reliably published for arm64.
If you want `t4g`/`c7g` pricing, verify first and be ready to build that
image yourself:

```bash
docker manifest inspect iegomez/mosquitto-go-auth:latest | grep -A2 architecture
```

`t3` is burstable. Ingestion at a few hundred devices sits far below the
baseline, but **image builds burn CPU credits** — build on the instance only
occasionally, or move builds to ECR (section 12.1). If `CPUCreditBalance`
trends to zero in normal operation, switch to `m7i-flex.large`.

### 5.1 First boot

Associate the Elastic IP, then connect (Session Manager, or
`ssh -i bms-prod.pem ec2-user@<eip>`):

```bash
sudo dnf update -y
sudo dnf install -y docker git jq

# Docker Compose v2 is not in the AL2023 repos — install the CLI plugin.
# v2.24+ is required: the production override file uses the !override tag.
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64" \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

sudo usermod -aG docker ec2-user      # log out and back in for this to apply
sudo systemctl enable --now docker
docker compose version                # expect v2.24 or newer
```

### 5.2 Mount the data volume and point Docker at it

```bash
lsblk                                  # find the empty 120 GB device, e.g. nvme1n1
sudo mkfs -t xfs /dev/nvme1n1          # ONLY if it is empty — this destroys data
sudo mkdir -p /data
echo "UUID=$(sudo blkid -s UUID -o value /dev/nvme1n1) /data xfs defaults,nofail 0 2" | sudo tee -a /etc/fstab
sudo mount -a && df -h /data
```

`nofail` is not optional: without it, a detached or renamed data volume
leaves the instance unbootable rather than merely degraded.

Move Docker's data root onto `/data` so that every named volume lands there
with no change to `docker-compose.yml`:

```bash
sudo systemctl stop docker
sudo mkdir -p /data/docker
printf '{\n  "data-root": "/data/docker",\n  "log-driver": "json-file",\n  "log-opts": {"max-size": "20m", "max-file": "5"}\n}\n' \
  | sudo tee /etc/docker/daemon.json
sudo systemctl start docker
docker info | grep "Docker Root Dir"   # expect /data/docker
```

The `log-opts` there are the second half of the disk-safety story: Docker's
default `json-file` driver has **no size limit**, and a chatty container can
fill a disk on its own.

**Verify:** `df -h` shows `/data` mounted, `docker info` reports the new root,
and `sudo reboot` followed by `docker info` shows both still correct.

---

## 6. Secrets in SSM Parameter Store

Nothing in this stack has a hardcoded credential, and nothing should acquire
one on AWS either. `.env` on the instance is **generated at boot** from
Parameter Store, so the authoritative copy of every secret is in AWS, with
an audit trail, and a rebuilt instance needs no files copied to it.

### 6.1 Generate and store

Standard-tier `SecureString` parameters are free; use the default KMS key.

```bash
# Run these from your laptop, with your admin credentials.
PREFIX=/bms/prod
put() { aws ssm put-parameter --name "$PREFIX/$1" --type SecureString --value "$2" --overwrite; }

put POSTGRES_PASSWORD        "$(openssl rand -base64 36)"
put INFLUXDB_ADMIN_PASSWORD  "$(openssl rand -base64 36)"
put INFLUXDB_ADMIN_TOKEN     "$(openssl rand -base64 36)"
put MQTT_INGESTION_PASSWORD  "$(openssl rand -base64 36)"
put MQTT_BRIDGE_PASSWORD     "$(openssl rand -base64 36)"
put MQTT_CONNECTORS_PASSWORD "$(openssl rand -base64 36)"
put CONNECTOR_SERVICE_API_KEY "$(openssl rand -base64 32)"
put JWT_SECRET_KEY           "$(openssl rand -base64 48)"
put ENCRYPTION_KEY           "$(openssl rand -base64 32)"   # MUST decode to exactly 32 bytes
```

One generated value per secret, never one value reused across several.

Two of these deserve attention:

- **`ENCRYPTION_KEY`** is a base64-encoded **32-byte** AES-256 key. The API
  and ingestion both decrypt with it (tenant MQTT credentials you can reveal
  in the UI, the SMTP password). `openssl rand -base64 32` is exactly right;
  anything else fails at startup with *"Encryption:Key must decode to exactly
  32 bytes"*. **Losing it means every stored MQTT credential and the SMTP
  password become unreadable** — back it up where you back up nothing else.
- **`POSTGRES_PASSWORD`** is used by the API, ingestion **and the broker's
  auth plugin**. Rotating it is a three-place change (section 8.2).

Per-tenant **device** passwords are not in here: they are generated by the
API when you create a tenant and stored in the `MqttCredentials` table
(bcrypt for the broker, AES for readback). See
[onboarding.md](../operations/onboarding.md).

### 6.2 Instance role

IAM → Roles → create `bms-prod-role` for EC2, attach the AWS-managed
`AmazonSSMManagedInstanceCore` (this is what gives you Session Manager), plus
this inline policy. Replace the account id, region and bucket name.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Sid": "ReadOwnSecrets",
      "Effect": "Allow",
      "Action": ["ssm:GetParameter", "ssm:GetParameters", "ssm:GetParametersByPath"],
      "Resource": "arn:aws:ssm:ap-southeast-1:<account-id>:parameter/bms/prod/*" },
    { "Sid": "DecryptThem",
      "Effect": "Allow",
      "Action": "kms:Decrypt",
      "Resource": "*",
      "Condition": { "StringEquals": { "kms:ViaService": "ssm.ap-southeast-1.amazonaws.com" } } },
    { "Sid": "Backups",
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:GetObject", "s3:ListBucket"],
      "Resource": ["arn:aws:s3:::bms-prod-backups", "arn:aws:s3:::bms-prod-backups/*"] },
    { "Sid": "Telemetry",
      "Effect": "Allow",
      "Action": ["logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents",
                 "logs:DescribeLogStreams", "cloudwatch:PutMetricData"],
      "Resource": "*" }
  ]
}
```

Attach it to the instance (EC2 → Actions → Security → Modify IAM role). The
instance now reads its own secrets and needs no access keys anywhere on
disk.

**Verify:** on the instance,
`aws ssm get-parameter --name /bms/prod/JWT_SECRET_KEY --with-decryption --query Parameter.Value --output text`
prints the value, and the same command for a parameter outside `/bms/prod/`
is denied.

---

## 7. Changes the repository needs before it can be deployed

The Compose stack is built for local development. Four things must change for
a public deployment. **Make these in Git before you deploy** (the fourth is
server-side only).

### 7.1 The dashboard bakes its API URL at build time — required

`lib/api/client.ts` and `lib/websocket/useRealtime.ts` read
`process.env.NEXT_PUBLIC_API_URL` / `NEXT_PUBLIC_WS_URL`. Next.js **inlines
`NEXT_PUBLIC_*` into the browser bundle at build time**, but
`docker-compose.yml` passes them as *runtime* environment variables, which
the already-built bundle cannot see. Deploy as-is and every browser request
goes to `http://localhost:5000` — the dashboard loads, the login fails, and
nothing in the logs says why.

In `infrastructure/docker/web.Dockerfile`, add the arguments to the **build**
stage, before `npm run build`:

```dockerfile
 FROM node:22-alpine AS build
 WORKDIR /app
+ARG NEXT_PUBLIC_API_URL
+ARG NEXT_PUBLIC_WS_URL
+ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL \
+    NEXT_PUBLIC_WS_URL=$NEXT_PUBLIC_WS_URL
 COPY --from=deps /app/node_modules ./node_modules
 COPY . .
 RUN npm run build
```

They are then passed in the production override (section 7.4).

### 7.2 `Encryption__Key` is never passed to the containers — required

`docker-compose.yml` passes `Jwt__SecretKey` but not `Encryption__Key`, and
`appsettings.json` defaults it to empty. The API throws
`InvalidOperationException` the first time it needs it — which is when you
create your first tenant, i.e. the first thing you do after deploying.

Both `api` and `ingestion` need it, **with the same value**:

```yaml
 api:
     environment:
       Jwt__SecretKey: ${JWT_SECRET_KEY:?JWT_SECRET_KEY must be set in .env}
+      Encryption__Key: ${ENCRYPTION_KEY:?ENCRYPTION_KEY must be set in .env}
```

Add the identical line to `ingestion`. The `:?` form is deliberate: the stack
refuses to start rather than coming up subtly broken.

### 7.3 The seeded demo accounts — required hardening, section 10.1

`DbSeeder` runs on **any** empty database, not only in Development, so a
fresh production database is created with `superadmin@bms.local`, three demo
tenants, 13 demo devices and the password `Passw0rd!`. This is a deliberate
first-run convenience, and it is a live credential on a public IP until you
change it. Section 10.1 is not optional.

### 7.4 The production Compose override — required

Create `docker-compose.prod.yml` next to `docker-compose.yml`. It does four
things: pulls the public ports back to loopback, points the app at the real
domains, passes the build arguments from 7.1, and caps log sizes.

```yaml
# docker-compose.prod.yml — used as:
#   docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
# `!override` replaces the base file's port list instead of appending to it
# (Compose v2.24+). Without it you get both bindings and a port conflict.
x-logging: &logging
  driver: json-file
  options: { max-size: "20m", max-file: "5" }

services:
  postgres:
    ports: !override ["127.0.0.1:5432:5432"]
    logging: *logging

  influxdb:
    ports: !override ["127.0.0.1:8086:8086"]
    logging: *logging

  mosquitto:
    # 1883 stays reachable from the host for debugging only; devices use 8883.
    ports: !override ["127.0.0.1:1883:1883", "8883:8883"]
    logging: *logging

  api:
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      # Enables ForwardedHeadersMiddleware so the API can read Caddy's
      # X-Forwarded-Proto/For. ASP.NET trusts those headers only from known
      # proxies, and this proxy is another container — getting real client
      # IPs into the logs also needs KnownNetworks/KnownProxies relaxed in
      # Program.cs. Harmless as it stands: with no HTTPS port bound inside
      # the container, UseHttpsRedirection is already a no-op.
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true"
      Cors__AllowedOrigins__0: https://app.bms.example.com
      # Devices fetch firmware from here, so it must resolve from their network.
      Firmware__PublicBaseUrl: https://api.bms.example.com
    ports: !override ["127.0.0.1:5000:8080"]
    logging: *logging

  ingestion:
    environment:
      DOTNET_ENVIRONMENT: Production
    ports: !override ["127.0.0.1:9464:9464"]
    logging: *logging

  connectors:
    logging: *logging

  web:
    build:
      args:
        NEXT_PUBLIC_API_URL: https://api.bms.example.com
        NEXT_PUBLIC_WS_URL: https://api.bms.example.com
    ports: !override ["127.0.0.1:3000:3000"]
    logging: *logging

  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./infrastructure/caddy/Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data        # issued certificates — keep this volume
      - caddy-config:/config
    depends_on: [api, web]
    logging: *logging

volumes:
  caddy-data:
  caddy-config:
```

And `infrastructure/caddy/Caddyfile`:

```
{
	email ops@bms.example.com   # Let's Encrypt expiry notices
}

app.bms.example.com {
	reverse_proxy web:3000
}

api.bms.example.com {
	# WebSockets (SignalR, /hubs/realtime) pass through reverse_proxy as-is.
	reverse_proxy api:8080
}

# No HTTP service lives here; this block exists only so Caddy obtains and
# renews a certificate for the broker's hostname (section 8.3).
mqtt.bms.example.com {
	respond "mqtt" 200
}
```

Caddy gets and renews certificates automatically over HTTP-01, which is why
port 80 is open. Replace `bms.example.com` with your domain in all five
places.

---

## 8. Broker configuration — the part specific to this platform

`infrastructure/mosquitto/mosquitto.conf` and `passwd` are **git-ignored**,
so they do not arrive with the clone. They have to be created on the
instance, and the defaults in the example files are development defaults.

### 8.1 Service account passwords

The broker runs the `mosquitto-go-auth` plugin, whose password file is
hashed with **its own `pw` utility (bcrypt)** — `mosquitto_passwd` hashes are
a different format and will not verify. (`passwd.example` and
[docker.md](docker.md) still show the `mosquitto_passwd` commands; they are
stale. The authoritative usage is in `tests/reliability/run-reliability-tests.sh`.)

Three service accounts, matching the ACL in `infrastructure/mosquitto/acl`:

```bash
cd ~/BMS
pwhash() { docker run --rm --entrypoint /mosquitto/pw iegomez/mosquitto-go-auth:latest -h bcrypt -p "$1"; }

: > infrastructure/mosquitto/passwd
for user in bms-ingestion bms-api-realtime-bridge bms-connectors; do
  case $user in
    bms-ingestion)            pw=$(aws ssm get-parameter --name /bms/prod/MQTT_INGESTION_PASSWORD  --with-decryption --query Parameter.Value --output text) ;;
    bms-api-realtime-bridge)  pw=$(aws ssm get-parameter --name /bms/prod/MQTT_BRIDGE_PASSWORD     --with-decryption --query Parameter.Value --output text) ;;
    bms-connectors)           pw=$(aws ssm get-parameter --name /bms/prod/MQTT_CONNECTORS_PASSWORD --with-decryption --query Parameter.Value --output text) ;;
  esac
  echo "$user:$(pwhash "$pw")" >> infrastructure/mosquitto/passwd
done
chmod 600 infrastructure/mosquitto/passwd
```

Device credentials are **not** in this file. They live in PostgreSQL
(`MqttCredentials`), one per tenant, created by the API — which is why the
broker talks to the database at all.

### 8.2 Point the auth plugin at the right database

Copy the example and fix three values:

```bash
cp infrastructure/mosquitto/mosquitto.conf.example infrastructure/mosquitto/mosquitto.conf
```

| Setting | Development value | Production value |
|---|---|---|
| `auth_opt_pg_host` | `host.docker.internal` | `postgres` — the Compose service, not the Docker host |
| `auth_opt_pg_password` | `devpassword` | your `POSTGRES_PASSWORD` |
| `auth_opt_pg_sslmode` | `disable` | `disable` is acceptable on the Compose network; use `verify-full` if PostgreSQL ever moves to RDS |

Mosquitto cannot read environment variables, so the password is written
literally into this file. `chmod 600` it, and remember it is the third place
`POSTGRES_PASSWORD` appears.

Two settings in that file you should not change without reading
[production-readiness.md §4](production-readiness.md#4-postgresql-and-broker-auth-coupling):
the **`auth_opt_pg_aclquery`** (omitting it silently grants every
authenticated client access to every topic) and the **auth cache** (without
it, a PostgreSQL restart starts *refusing device connections* instead of
merely pausing ingestion).

### 8.3 A real certificate on 8883

The dev script (`certs/generate-dev-certs.sh`) produces a self-signed CA for
`localhost`. Do not ship it: gateways will either refuse the connection or
have to be told to trust a CA you now have to distribute.

Caddy already holds a real certificate for `mqtt.bms.example.com` (section
7.4). Copy it into place, converting to the filenames `mosquitto.conf`
already expects:

```bash
sudo tee /usr/local/bin/bms-sync-broker-cert >/dev/null <<'EOS'
#!/usr/bin/env bash
# Copies Caddy's certificate for the broker hostname into the Mosquitto
# cert mount and restarts the broker if it changed. Mosquitto reads its
# certificate once at startup, so a renewal needs the restart.
set -euo pipefail
HOST=mqtt.bms.example.com
REPO=/home/ec2-user/BMS
SRC=$(docker compose -f $REPO/docker-compose.yml -f $REPO/docker-compose.prod.yml \
        exec -T caddy sh -c "cat /data/caddy/certificates/*/$HOST/$HOST.crt")
KEY=$(docker compose -f $REPO/docker-compose.yml -f $REPO/docker-compose.prod.yml \
        exec -T caddy sh -c "cat /data/caddy/certificates/*/$HOST/$HOST.key")
DEST=$REPO/infrastructure/mosquitto/certs
printf '%s\n' "$SRC" > $DEST/server.crt.new
printf '%s\n' "$KEY" > $DEST/server.key.new
if ! cmp -s $DEST/server.crt.new $DEST/server.crt; then
  mv $DEST/server.crt.new $DEST/server.crt
  mv $DEST/server.key.new $DEST/server.key
  # The broker drops privileges to the mosquitto user and must read the key.
  chmod 644 $DEST/server.crt $DEST/server.key
  docker compose -f $REPO/docker-compose.yml -f $REPO/docker-compose.prod.yml restart mosquitto
  echo "broker certificate updated and broker restarted"
else
  rm -f $DEST/server.crt.new $DEST/server.key.new
fi
EOS
sudo chmod +x /usr/local/bin/bms-sync-broker-cert
```

Run it daily (it is a no-op until Caddy renews, ~30 days before expiry):

```bash
sudo tee /etc/systemd/system/bms-broker-cert.service >/dev/null <<'EOS'
[Unit]
Description=Sync the broker TLS certificate from Caddy
[Service]
Type=oneshot
ExecStart=/usr/local/bin/bms-sync-broker-cert
EOS
sudo tee /etc/systemd/system/bms-broker-cert.timer >/dev/null <<'EOS'
[Unit]
Description=Daily broker certificate sync
[Timer]
OnCalendar=daily
RandomizedDelaySec=1h
Persistent=true
[Install]
WantedBy=timers.target
EOS
sudo systemctl enable --now bms-broker-cert.timer
```

Finally, in `mosquitto.conf`, **comment out `cafile`** on the 8883 listener.
It exists to verify *client* certificates; this deployment does not use
mutual TLS, and leaving it pointed at the dev CA is misleading at best.
`certfile`/`keyfile` keep their existing paths, now holding the real
certificate.

The restart in that script disconnects every device for a second or two.
They reconnect with backoff, and their QoS 1 messages are queued by the
broker session, so this is a blip rather than data loss — but it is why the
timer is randomised rather than firing at a fixed busy minute.

---

## 9. First deploy

```bash
# On the instance
cd ~ && git clone <your-repo-url> BMS && cd BMS
git checkout main      # or your release tag
```

### 9.1 Render `.env` from Parameter Store

```bash
sudo tee /usr/local/bin/bms-render-env >/dev/null <<'EOS'
#!/usr/bin/env bash
# Writes /home/ec2-user/BMS/.env from SSM Parameter Store. The file is a
# cache of AWS state, never the source of truth: it is rewritten on every
# boot and every deploy, and editing it by hand will be silently undone.
set -euo pipefail
PREFIX=/bms/prod
OUT=/home/ec2-user/BMS/.env
umask 077

get() { aws ssm get-parameter --name "$PREFIX/$1" --with-decryption --query Parameter.Value --output text; }

cat > "$OUT" <<EOF
# GENERATED by bms-render-env — do not edit. Secrets live in SSM $PREFIX/*.
POSTGRES_DB=bms_platform
POSTGRES_USER=bms_app
POSTGRES_PASSWORD=$(get POSTGRES_PASSWORD)
POSTGRES_PORT=5432

INFLUXDB_ORG=bms-platform
INFLUXDB_BUCKET=telemetry
INFLUXDB_ADMIN_USER=admin
INFLUXDB_ADMIN_PASSWORD=$(get INFLUXDB_ADMIN_PASSWORD)
INFLUXDB_ADMIN_TOKEN=$(get INFLUXDB_ADMIN_TOKEN)
INFLUXDB_PORT=8086

MQTT_PORT=1883
MQTTS_PORT=8883
MQTT_BRIDGE_USERNAME=bms-api-realtime-bridge
MQTT_BRIDGE_PASSWORD=$(get MQTT_BRIDGE_PASSWORD)
MQTT_INGESTION_USERNAME=bms-ingestion
MQTT_INGESTION_PASSWORD=$(get MQTT_INGESTION_PASSWORD)
MQTT_CONNECTORS_PASSWORD=$(get MQTT_CONNECTORS_PASSWORD)
CONNECTOR_SERVICE_API_KEY=$(get CONNECTOR_SERVICE_API_KEY)
CONNECTOR_PORT_RANGE=12000-12099

# Sized from YOUR fleet — see production-readiness.md §2. These are defaults.
MQTT_INGESTION_SESSION_EXPIRY_SECONDS=86400
MOSQUITTO_MAX_QUEUED_MESSAGES=100000
MOSQUITTO_MAX_QUEUED_BYTES=104857600
MOSQUITTO_AUTOSAVE_INTERVAL=60
MOSQUITTO_PERSISTENT_CLIENT_EXPIRATION=1d

ASPNETCORE_ENVIRONMENT=Production
API_PORT=5000
WEB_PORT=3000
JWT_SECRET_KEY=$(get JWT_SECRET_KEY)
ENCRYPTION_KEY=$(get ENCRYPTION_KEY)
EOF
chown ec2-user:ec2-user "$OUT"
EOS
sudo chmod +x /usr/local/bin/bms-render-env
sudo /usr/local/bin/bms-render-env
```

Note the queue numbers at the bottom: they are the defaults, which hold
roughly ten minutes of outage at 10,000 devices on a one-minute interval and
roughly **sixty seconds** at a ten-second interval. Replace them with your
own figures from `size-queue.py` before go-live — past the limit the broker
drops QoS 1 messages **silently**.

### 9.2 Start it

```bash
cd ~/BMS
# Every command below uses this; add it to ~/.bashrc so it survives logout.
alias dc='docker compose -f docker-compose.yml -f docker-compose.prod.yml'
dc config >/dev/null      # validates interpolation and the !override tags
dc up -d --build          # first build takes 5-10 minutes
dc ps
```

`api` and `ingestion` both run EF Core migrations on startup (idempotent,
safe when they start together); `api` also seeds — see section 10.1
immediately.

### 9.3 Survive a reboot

The containers restart themselves (`restart: unless-stopped`), but `.env`
must be rendered *before* Compose starts, which systemd should own:

```bash
sudo tee /etc/systemd/system/bms.service >/dev/null <<'EOS'
[Unit]
Description=BMS platform (docker compose)
Requires=docker.service
After=docker.service network-online.target
[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=/home/ec2-user/BMS
ExecStartPre=/usr/local/bin/bms-render-env
ExecStart=/usr/local/lib/docker/cli-plugins/docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
ExecStop=/usr/local/lib/docker/cli-plugins/docker-compose -f docker-compose.yml -f docker-compose.prod.yml stop
[Install]
WantedBy=multi-user.target
EOS
sudo systemctl daemon-reload && sudo systemctl enable --now bms.service
```

**Verify — the only test that counts:** `sudo reboot`, wait two minutes, then
confirm the dashboard loads and `dc ps` shows every service up without you
having typed anything.

---

## 10. Harden, then connect the first real device

### 10.1 Deal with the seeded accounts — do this before anything else

The seeder created `superadmin@bms.local` with the password `Passw0rd!`, plus
`admin@whirlpool.example`, `admin@abc-manufacturing.example` and
`viewer@demo-company.example` with the same, and 13 demo devices across three
demo tenants.

1. Log in to `https://app.bms.example.com` as `superadmin@bms.local` /
   `Passw0rd!`.
2. Create your own SuperAdmin user with a real email and a strong password.
3. Log in as that user, then **change or delete every seeded account**,
   starting with `superadmin@bms.local`.
4. Delete the three demo tenants (which takes their sites, devices and MQTT
   credentials with them), unless you want them as a reference.

**Verify:** logging in with `Passw0rd!` fails for every account.

### 10.2 Onboard a real tenant

Follow [onboarding.md](../operations/onboarding.md). In short: create the
tenant (its slug becomes both the MQTT username and the first topic segment,
so choose it as carefully as a primary key), read its generated password from
**MQTT Credentials**, then add the site, device type and devices.

### 10.3 Notifications

SMTP and Telegram are configured in the dashboard (Settings), not in `.env` —
they live in the database, encrypted with `ENCRYPTION_KEY`. Because **AWS
blocks outbound port 25**, use a provider on 587 (SES SMTP endpoint, or your
own mail provider). Use the *Send test* buttons; a silent failure here is a
silent failure of your alerting.

### 10.4 Point a gateway at it

```
host      mqtt.bms.example.com
port      8883 (TLS, no client certificate)
username  <tenant slug>
password  <from MQTT Credentials>
topic     iot/<tenant-slug>/<site-code>/<device-code>/telemetry
QoS       1
payload   flat JSON of BMS parameter keys, e.g. {"temperature": 24.6, "battery": 91}
```

The ACL confines each tenant credential to `iot/<its own slug>/#`, so a
wrong slug fails to publish rather than landing in someone else's data.

### 10.5 End-to-end verification

Run these from your laptop, not the instance — you are testing the path a
device takes.

```bash
# 1. TLS on the broker, from a real CA
openssl s_client -connect mqtt.bms.example.com:8883 </dev/null 2>/dev/null \
  | openssl x509 -noout -subject -issuer -dates

# 2. Plain MQTT must NOT be reachable
nc -z -w3 mqtt.bms.example.com 1883 && echo "FAIL: 1883 is open" || echo "ok: 1883 closed"

# 3. PostgreSQL and InfluxDB must NOT be reachable
for p in 5432 8086 9464; do nc -z -w3 app.bms.example.com $p && echo "FAIL: $p open" || echo "ok: $p closed"; done

# 4. API health
curl -fsS https://api.bms.example.com/health && echo

# 5. Publish one real reading as a tenant
# --capath suits Linux; on macOS use --cafile /etc/ssl/cert.pem instead.
mosquitto_pub -h mqtt.bms.example.com -p 8883 --capath /etc/ssl/certs \
  -u '<tenant-slug>' -P '<tenant-password>' -q 1 -d \
  -t 'iot/<tenant-slug>/<site-code>/<device-code>/telemetry' \
  -m '{"temperature": 24.6}'
```

Then, in the dashboard: the device shows **Online**, the reading appears on
its Live tab **without a refresh** (that proves ingestion → internal MQTT
bridge → SignalR → browser), and the History tab shows it (that proves
InfluxDB). If the reading is rejected, the device's **Raw Messages** tab says
why — usually a parameter key that is not on the device's type.

On the instance:

```bash
curl -s localhost:9464/metrics | grep -E 'messages_received_total|messages_acknowledged_total'
```

Received and acknowledged should both have increased by one. A received
count that climbs while acknowledged does not means the pipeline is stuck
before the PostgreSQL commit.

---

## 11. Monitoring

The platform exposes everything already; AWS just has to collect it.
[production-readiness.md §6](production-readiness.md#6-monitoring-and-alerting)
lists the alerts worth having and why — the two that matter are **ingestion
offline** (the moment it is true, the broker queue starts filling: that is
your clock) and **broker queue filling** (the only warning you get before
silent drops).

### 11.1 CloudWatch agent

```bash
sudo dnf install -y amazon-cloudwatch-agent
```

Write `/opt/aws/amazon-cloudwatch-agent/etc/config.json`:

```json
{
  "agent": { "metrics_collection_interval": 60 },
  "metrics": {
    "namespace": "BMS/Host",
    "append_dimensions": { "InstanceId": "${aws:InstanceId}" },
    "metrics_collected": {
      "mem":  { "measurement": ["mem_used_percent"] },
      "disk": { "measurement": ["used_percent"], "resources": ["/", "/data"] }
    }
  },
  "logs": {
    "logs_collected": {
      "files": {
        "collect_list": [
          { "file_path": "/data/docker/containers/*/*-json.log",
            "log_group_name": "/bms/prod/containers",
            "retention_in_days": 30 }
        ]
      }
    }
  }
}
```

```bash
sudo /opt/aws/amazon-cloudwatch-agent/bin/amazon-cloudwatch-agent-ctl \
  -a fetch-config -m ec2 -c file:/opt/aws/amazon-cloudwatch-agent/etc/config.json -s
```

Tailing Docker's JSON log files (rather than switching the containers to the
`awslogs` driver) is a deliberate choice: `docker compose logs` keeps
working, which is what you actually reach for at 3am. Memory and disk are
there because EC2 does not report either natively.

### 11.2 Alarms

| Alarm | Metric | Threshold |
|---|---|---|
| Instance unhealthy | `StatusCheckFailed` | ≥ 1 for 2 minutes |
| Data disk filling | `BMS/Host disk used_percent` on `/data` | > 75% |
| Memory pressure | `mem_used_percent` | > 90% for 10 minutes |
| CPU credits draining | `CPUCreditBalance` | < 50 |
| Ingestion offline | see below | no data for 2 minutes |

For **ingestion offline**, the honest minimum is a cron job that scrapes the
local metrics endpoint and publishes one custom metric:

```bash
# /usr/local/bin/bms-ingestion-heartbeat, run every minute by a systemd timer
curl -fsS --max-time 5 localhost:9464/metrics >/dev/null && v=1 || v=0
aws cloudwatch put-metric-data --namespace BMS/Prod \
  --metric-name IngestionUp --value "$v" --unit Count
```

Alarm on `IngestionUp < 1` for 2 datapoints, and route it to an SNS topic
with your phone number and email on it. The retained
`bms/ingestion/+/status` MQTT topic carries the same truth if you prefer to
read it from the broker. Queue depth needs a Mosquitto `$SYS` exporter; it is
worth adding once the fleet is real.

**Verify:** `dc stop ingestion`, wait three minutes, confirm the alarm fires
and reaches your phone, `dc start ingestion`. An alarm nobody receives is not
monitoring.

---

## 12. Day-two operations

### 12.1 Deploying a new version

```bash
cd ~/BMS && git pull
dc up -d --build          # migrations run on api/ingestion startup
dc ps && curl -fsS https://api.bms.example.com/health
```

What happens during it: ingestion stops (it has a 30 second grace period to
publish its offline status and finish the message in flight), the broker
queues telemetry for its persistent session, and ingestion drains the queue
on restart. Devices notice nothing as long as the gap stays well inside your
queue sizing.

Building on the instance competes with the running stack for CPU and burns
`t3` credits. Once deploys get frequent, build in CI and push to **ECR**,
then replace `build:` with `image:` in the production override — the instance
pulls instead of compiling, and a deploy becomes seconds.

**Rolling back** is `git checkout <previous-tag> && dc up -d --build`, with
one caveat: EF Core migrations are not reversed automatically. A release
containing a destructive migration is only rollback-safe via a restore, so
read migrations before you ship them.

### 12.2 Backups

`infrastructure/scripts/backup.sh` already does the right thing (PostgreSQL
`pg_dump -Fc`, an InfluxDB backup, and the Mosquitto persistence file after
asking the broker to flush it — that last one is the only copy of messages
published but not yet stored). It needs an S3 bucket and a schedule.

```bash
aws s3api create-bucket --bucket bms-prod-backups \
  --region ap-southeast-1 --create-bucket-configuration LocationConstraint=ap-southeast-1
aws s3api put-public-access-block --bucket bms-prod-backups \
  --public-access-block-configuration "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
aws s3api put-bucket-versioning --bucket bms-prod-backups \
  --versioning-configuration Status=Enabled
```

Add a lifecycle rule (Glacier Instant Retrieval after 30 days, expire at
365) and a nightly timer:

```bash
sudo tee /etc/systemd/system/bms-backup.service >/dev/null <<'EOS'
[Unit]
Description=BMS nightly backup to S3
[Service]
Type=oneshot
User=ec2-user
WorkingDirectory=/home/ec2-user/BMS
Environment=S3_BUCKET=s3://bms-prod-backups
Environment=BACKUP_DIR=/data/backups
ExecStart=/home/ec2-user/BMS/infrastructure/scripts/backup.sh
EOS
sudo tee /etc/systemd/system/bms-backup.timer >/dev/null <<'EOS'
[Unit]
Description=Nightly BMS backup
[Timer]
OnCalendar=*-*-* 18:00:00 UTC
Persistent=true
[Install]
WantedBy=timers.target
EOS
sudo systemctl enable --now bms-backup.timer
```

Add **EBS snapshots** via Data Lifecycle Manager (daily, 7 days retained) as
a second line of defence — they recover the whole instance in minutes, where
`backup.sh` recovers the data precisely. Snapshots of a running database are
crash-consistent, so restore from the dump when you have the choice.

**The restore drill is not optional, and it is not done until it has been
done.** Follow
[production-readiness.md §5](production-readiness.md#5-backups-and-the-restore-drill)
against a separate stack — a second EC2 instance from the same AMI is the
honest venue — and **write down how long it took**. That number, not the
plan, is your recovery time objective.

### 12.3 Routine chores

| What | How often | Command |
|---|---|---|
| Patch the host | monthly | `sudo dnf update -y && sudo reboot` (section 9.3 brings it back) |
| Reclaim image space | monthly | `docker image prune -af` |
| Check retention is holding | weekly, first month | the `RawDeviceMessages` query in [production-readiness.md §1](production-readiness.md#1-raw-message-retention) |
| Rotate secrets | per your policy, immediately on suspicion | update SSM → `bms-render-env` → `dc up -d`. `POSTGRES_PASSWORD` also needs `mosquitto.conf` (§8.2); tenant MQTT passwords are a **site visit**, since one credential serves a whole company's devices |
| Confirm backups exist | monthly | `aws s3 ls s3://bms-prod-backups/ \| tail` |

### 12.4 Where to look when something breaks

| Symptom | First look |
|---|---|
| Dashboard loads, API calls fail | Browser console. If requests go to `localhost:5000`, the web image was built without the build args (§7.1) |
| Login works, no live updates | `dc logs api \| grep -i signalr`, then the realtime bridge: `dc logs api \| grep -i bridge` |
| Devices cannot connect | `dc logs mosquitto`. "unauthorised" with correct credentials usually means the auth plugin cannot reach PostgreSQL (§8.2) |
| Devices connect, no data appears | `dc logs ingestion`, then the device's Raw Messages tab — a rejected payload is recorded with its reason |
| Everything stalls | disk. `df -h /data` |

[troubleshooting/common-issues.md](../troubleshooting/common-issues.md) has
the platform-level version of this table.

---

## 13. What to outgrow this with, and when

Each of these is a response to a specific signal. Do not do them pre-emptively.

| Signal | Change | What it costs you |
|---|---|---|
| You want point-in-time recovery without running the drill yourself | **PostgreSQL → RDS** (Multi-AZ). Change the connection strings and `auth_opt_pg_host`; keep the broker's auth cache, because the broker now depends on a network service for device logins. | ~$60+/month, and a slightly longer auth path |
| Deploys must not pause ingestion | **MQTT 5 shared subscriptions** across two or three ingestion instances. This is a code change, not an infrastructure one — the fixed client id and persistent session are what currently forbid a second instance. | Real work, re-earning the reliability tests |
| One host is no longer enough CPU, or you want rolling deploys | **ECS on EC2** for api/web/connectors, with ingestion pinned to a single task. Mosquitto's persistence file wants a stable host or EFS. | Noticeably more moving parts |
| The broker itself is the operational burden — queue sizing, certificate renewal, backing up a persistence file | **AWS IoT Core + SQS**, already designed: [BMS on AWS IoT Core](../architecture/BMS%20on%20AWS%20IoT%20Core.html). SQS holds 14 days with no sizing exercise, and device logins stop depending on your database. | Per-device X.509 certificates means touching every gateway in the field, the ingestion worker is rewritten around SQS, and a managed broker can never run inside a plant |
| Influx retention is becoming the dominant cost | Downsampling tasks first; managed time-series only after measuring | — |

---

## 14. Checklist

Infrastructure:

- [ ] Region chosen for device proximity; budget alarm set; root locked down
- [ ] Elastic IP allocated **and associated** — the fleet's address
- [ ] Security group: 443, 80, 8883, connector range, SSH restricted. **1883, 5432, 8086, 9464 absent**
- [ ] Three DNS A records resolving from outside AWS
- [ ] `/data` volume mounted with `nofail`; Docker data-root on it; log caps in `daemon.json`
- [ ] Instance role attached; every secret in SSM; `.env` generated, never hand-edited

Application:

- [ ] `web.Dockerfile` build args (§7.1) — else the dashboard calls localhost
- [ ] `Encryption__Key` passed to **both** api and ingestion (§7.2)
- [ ] `docker-compose.prod.yml`: Production environment, real CORS origin, real `Firmware__PublicBaseUrl`, ports on loopback
- [ ] Broker: `passwd` hashed with the plugin's `pw`, `auth_opt_pg_host=postgres`, real password, ACL query present, auth cache on
- [ ] Real certificate on 8883, renewal timer installed and tested; `cafile` commented out
- [ ] **Every seeded account changed or deleted**; demo tenants removed
- [ ] Queue sized from your own numbers, not the defaults in `bms-render-env`

Operations:

- [ ] `bms.service` enabled, and a reboot verified end to end
- [ ] CloudWatch agent collecting container logs, memory and disk
- [ ] Alarms firing to a human: ingestion offline, `/data` > 75%, status check
- [ ] Nightly backup to S3 verified present; EBS snapshots on
- [ ] **One restore rehearsed, with the elapsed time written down**
- [ ] Notifications tested through a provider on port 587, not 25

Then work through [go-live-checklist.md](go-live-checklist.md) — it covers
the decisions this guide assumes you have already made.
