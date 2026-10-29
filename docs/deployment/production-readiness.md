# Production readiness

What has to be true before this platform carries real devices. Each section
says what to do, how to verify it, and — where it matters — what breaks if
you skip it.

Status of the work this document covers:

| # | Item | State |
|---|---|---|
| 1 | Raw message retention | **Done** — `RawMessageRetentionWorker`, on by default |
| 2 | Broker queue sizing | **Needs your numbers** — helper provided |
| 3 | Secrets and certificates | **Your environment** — this document |
| 4 | PostgreSQL / broker-auth coupling | **Done** — auth cache in `mosquitto.conf.example` |
| 5 | Backups and restore drill | **Script provided, drill outstanding** |
| 6 | Monitoring and alerting | **Endpoints done** — alert rules below |

---

## 1. Raw message retention

`RawDeviceMessages` holds the full payload of every message ever received,
and the acknowledgement path writes to it before anything else. Unbounded
growth does not merely cost disk: a full disk stops the one write that must
never fail.

`RawMessageRetentionWorker` runs inside Bms.Ingestion and deletes expired
rows oldest-first, in bounded batches so the live insert path is never
queued behind one long statement.

```json
"RawMessageRetention": {
  "Enabled": true,
  "RetentionDays": 30,
  "IntervalSeconds": 3600,
  "BatchSize": 1000,
  "MaxBatchesPerPass": 50
}
```

Pick `RetentionDays` from how far back you would realistically replay a
vendor's payload after a decoder fix. 30 days is a reasonable default.

**The one thing it will not do.** A `Persisted` row whose reading never
reached InfluxDB is never deleted, however old it is — the backfill still
needs it, and dropping it would lose the reading silently. Those rows are
counted and logged each pass:

> Retention left N message(s) older than 30d in place: their reading never
> reached the time-series store.

If that number climbs, the backfill is stuck and **the retention ceiling is
not holding**. Fix the backfill; do not raise the retention window.

**Verify:** table size flat over a week at production volume.

```sql
SELECT pg_size_pretty(pg_total_relation_size('"RawDeviceMessages"')),
       count(*) FILTER (WHERE "TimeSeriesWrittenAt" IS NULL
                          AND "ProcessingStatus" = 'Persisted') AS stuck
FROM "RawDeviceMessages";
```

**Optional follow-up:** time-partition the table by `ReceivedAt` so deletes
become partition drops rather than row deletes. Worth doing above roughly
1,000 messages/second; unnecessary below that.

---

## 2. Broker queue sizing

Past its limit, Mosquitto drops QoS 1 messages **silently** — no error, no
per-message log line, no recovery. The defaults (100,000 / 100 MiB) hold
roughly 10 minutes of outage at 10,000 devices reporting once a minute, and
roughly **60 seconds** at a 10-second interval.

```bash
python3 infrastructure/mosquitto/size-queue.py --devices 500 --interval 10 --outage 15m
```

Put the results in `.env` as `MOSQUITTO_MAX_QUEUED_MESSAGES` and
`MOSQUITTO_MAX_QUEUED_BYTES`, and size the `mosquitto-data` volume to match.

Three inputs you already know (device count, interval, the outage you want
covered) and one worth measuring rather than assuming — average payload
size, because the byte cap bites first when payloads are larger than
expected:

```bash
mosquitto_sub -h localhost -t 'iot/#' -F '%l' -C 20
```

**Whichever is smaller wins.** The 24 h session expiry is a separate limit:
once the session is discarded, its whole queue goes with it, regardless of
these numbers. Keep `MOSQUITTO_PERSISTENT_CLIENT_EXPIRATION` ≥
`MQTT_INGESTION_SESSION_EXPIRY_SECONDS`.

**Sizing is a floor, not a fix.** It covers the gap between something
breaking and someone noticing. The durable fixes are to stop the consumer
going down for long (MQTT 5 shared subscriptions across two or three
ingestion instances, so a deploy never halts consumption) and to buffer on
the device. Reach for a bigger queue only after those.

**Verify:** stop ingestion for your target outage length under production
load, restart, and confirm the message count published equals the count
persisted.

---

## 3. Secrets and certificates

`.env.example` ships `change-me` placeholders. Every one of these needs a
generated value held in a secret store (AWS Secrets Manager or SSM Parameter
Store), injected as environment variables at deploy time — never committed:

| Variable | Used by |
|---|---|
| `POSTGRES_PASSWORD` | API, ingestion, **and the broker's auth plugin** |
| `INFLUXDB_ADMIN_PASSWORD` | InfluxDB |
| `INFLUXDB_ADMIN_TOKEN` | API, ingestion, backups |
| `MQTT_INGESTION_PASSWORD` | ingestion's broker login |
| `MQTT_BRIDGE_PASSWORD` | the API's realtime-bridge login |
| `JWT__Key` / `Encryption__Key` | API |
| per-tenant device passwords | devices, stored hashed in `MqttCredentials` |

```bash
openssl rand -base64 36      # one per secret, not one for all of them
```

Write down who can read them, and how they get rotated. Rotation is not
optional-forever: it is the only remedy if one ever leaks.

### TLS on 8883

`infrastructure/mosquitto/certs/generate-dev-certs.sh` produces development
certificates. **Do not ship them.** Production needs a certificate chain the
devices will actually trust — either a public CA (Let's Encrypt, if the
broker has a public hostname) or your own internal CA with the root
pre-loaded onto devices.

Also decide the fate of the plain 1883 listener. In production it should be
closed, or bound only to the private network between containers. Anything
reaching the broker from outside should arrive on 8883.

**Verify:**

```bash
openssl s_client -connect <broker-host>:8883 -CAfile <your-ca.crt> </dev/null | head -20
mosquitto_pub -h <broker-host> -p 1883 -t test -m x    # should FAIL from outside
```

---

## 4. PostgreSQL and broker-auth coupling

PostgreSQL is both the acknowledgement boundary **and** the broker's device
authentication backend (mosquitto-go-auth). Without caching, every device
connection re-queries it — so a database outage does not merely pause
processing, it starts **refusing device connections**, pushing devices off
the broker during exactly the outage the broker exists to absorb.

`mosquitto.conf.example` now enables the plugin's cache:

```
auth_opt_cache true
auth_opt_cache_type go-cache
auth_opt_auth_cache_seconds 300
auth_opt_acl_cache_seconds 300
auth_opt_auth_jitter_seconds 15
auth_opt_acl_jitter_seconds 15
auth_opt_cache_refresh true
```

Mirror this into your live `mosquitto.conf` (it is gitignored, so the
template change does not reach it automatically).

Understand what it does and does not buy:

- it covers **already-seen** devices, for an outage **shorter than the TTL**
- a device connecting for the first time during an outage is still refused
- **revocation is delayed by up to the TTL** — disabling a device takes
  effect within 300 s, not instantly. Shorten the TTL if that trade matters
  more to you than outage tolerance.

The alternatives, if 300 s is not enough: a read replica dedicated to auth,
or accepting the coupling and specifying device-side buffering to your
vendors. Any of the three is defensible. Not having decided is not.

---

## 5. Backups and the restore drill

Three stateful volumes: `pgdata`, `influxdata`, `mosquitto-data`.

Mosquitto is on that list deliberately — its queue is the only copy of a
message a device has published but ingestion has not yet stored. Restoring
without it silently loses whatever was in flight.

```bash
S3_BUCKET=s3://acme-bms-backups ./infrastructure/scripts/backup.sh
```

Schedule it (cron or a systemd timer), off-host, with a retention policy.

### The drill

A backup that has never been restored is not a backup. Do this once before
go-live, against a *separate* stack, and **write down how long it took** —
that number is your actual recovery time objective, whatever the plan says.

```bash
# 1. fresh stack, nothing in it
docker compose -p bms-restore-drill up -d postgres influxdb mosquitto

# 2. PostgreSQL
docker compose -p bms-restore-drill exec -T postgres \
    pg_restore --username bms_app --dbname bms_platform --clean --if-exists < backups/<stamp>/postgres.dump

# 3. InfluxDB
tar -xf backups/<stamp>/influxdb.tar -C /tmp
docker compose -p bms-restore-drill cp /tmp/influx-backup influxdb:/tmp/influx-backup
docker compose -p bms-restore-drill exec -T influxdb influx restore /tmp/influx-backup --full --token "$INFLUXDB_ADMIN_TOKEN"

# 4. Mosquitto queue
docker compose -p bms-restore-drill stop mosquitto
tar -xf backups/<stamp>/mosquitto-data.tar -C /tmp
docker compose -p bms-restore-drill cp /tmp/data/mosquitto.db mosquitto:/mosquitto/data/mosquitto.db
docker compose -p bms-restore-drill start mosquitto
```

**Verify:** device count, alert-rule count and the most recent telemetry
timestamp all match the source. Then tear the drill stack down.

---

## 6. Monitoring and alerting

The counters have existed since the pipeline was written; nothing read them.
Both services now expose Prometheus endpoints:

| Service | Endpoint | Default |
|---|---|---|
| Bms.Ingestion | `http://<host>:9464/metrics` | `Metrics__PrometheusPort=9464` |
| Bms.Api | `http://<host>:5000/metrics` | `Metrics__Enabled=true` |

Both are unauthenticated by design — a scraper has no JWT — so keep them on
the private network. The compose file binds the ingestion port to
`127.0.0.1` by default; override with `INGESTION_METRICS_BIND`.

`docker compose up -d && curl -s localhost:9464/metrics | head` should show
`bms_ingestion_*` series.

### The alerts worth having

| Alert | Condition | Why |
|---|---|---|
| **Ingestion offline** | no `bms_ingestion_*` scrape for 2 min, or the retained `bms/ingestion/+/status` topic says offline | The broker queue starts filling the moment this is true. It is your clock. |
| **Broker queue filling** | queue depth > 60% of `MOSQUITTO_MAX_QUEUED_MESSAGES` | The only warning before silent drops. Needs a Mosquitto exporter (`$SYS/broker/...`). |
| **Nothing acknowledged** | `rate(bms_ingestion_messages_acknowledged_total[5m]) == 0` while received > 0 | PostgreSQL is unreachable or the pipeline is stuck. |
| **Rejections climbing** | `rate(bms_ingestion_messages_rejected_total[15m])` above your baseline | A device or decoder has started producing garbage. |
| **Time-series writes failing** | `rate(bms_ingestion_timeseries_write_failures_total[10m]) > 0` | InfluxDB is unhealthy; backfill debt is accumulating. |
| **Backfill not draining** | the retention warning above, or a rising stuck count | The retention ceiling has stopped holding. |
| **Processing latency** | p95 of `bms_ingestion_processing_latency` above your baseline | Early warning that one instance is running out of headroom. |

The first two matter most. Everything else is a refinement.

---

## Go-live checklist

- [ ] Retention on, `RetentionDays` agreed, table size flat for a week
- [ ] Queue sized from real numbers; volume sized to match; drop test passed
- [ ] Every `change-me` replaced from a secret store; rotation documented
- [ ] Real certificate on 8883; plain 1883 closed to the outside
- [ ] Auth cache mirrored into the live `mosquitto.conf`; TTL trade accepted
- [ ] Backups scheduled, off-host, **and one restore rehearsed with the time written down**
- [ ] Both `/metrics` endpoints scraped; "ingestion offline" and "queue filling" alerts firing to somewhere a human reads

### Deliberately not blocking

Shared subscriptions, batched InfluxDB writes and a load test at target
device count are scale work, not launch work — with one exception: run the
load test early anyway. Every capacity number in this document is
arithmetic, and that test is what replaces it with measurement.
