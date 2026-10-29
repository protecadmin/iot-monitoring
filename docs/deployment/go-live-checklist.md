# Go-live checklist

Work top to bottom. The order is dependency-driven: later items need
decisions or numbers that earlier ones produce.

Every item says how to **verify** it, because "configured" and "working" are
not the same thing — three items on this list were written, looked correct,
and turned out to be broken when actually run.

Detail for each item is in [production-readiness.md](production-readiness.md).

---

## Phase 0 — Before you touch anything

Nobody can do the work until these exist. Half an hour, mostly decisions.

- [ ] **Device count at launch**, and at 6 months  →  _________
- [ ] **Reporting interval** per device (seconds)  →  _________
- [ ] **Longest outage the queue must cover** — your realistic worst deploy
      or restart window  →  _________
- [ ] **Raw message retention window** — how far back would you replay a
      vendor payload after a decoder fix?  →  _________ days
- [ ] **Who owns the secret store**, and who may read production secrets
- [ ] **Where metrics and alerts land** — Grafana, CloudWatch, something else
- [ ] **Who gets paged** when ingestion goes offline at 3am
- [ ] **PostgreSQL coupling decision** (see item 6 below) — pick one of the
      three options and write it down

> The first three feed item 1. Without them, queue sizing is guesswork.

---

## Phase 1 — Blockers

### 1. Size the broker queue · ~1 hour

Past its limit Mosquitto drops QoS 1 messages **silently**. Defaults hold
roughly 60 seconds at 10k devices on a 10-second interval.

- [ ] Measure a real payload: `mosquitto_sub -h <host> -t 'iot/#' -F '%l' -C 20`
- [ ] `python3 infrastructure/mosquitto/size-queue.py --devices N --interval S --outage 15m --payload B`
- [ ] Set `MOSQUITTO_MAX_QUEUED_MESSAGES` and `MOSQUITTO_MAX_QUEUED_BYTES` in `.env`
- [ ] Size the `mosquitto-data` volume for the byte figure, with headroom
- [ ] Confirm `MOSQUITTO_PERSISTENT_CLIENT_EXPIRATION` ≥ `MQTT_INGESTION_SESSION_EXPIRY_SECONDS`
      — whichever is smaller is the real bound

**Verify:** stop ingestion for your target outage length under load, restart,
and confirm messages published == messages persisted. Zero loss or the
number is wrong.

### 2. Secrets into a real store · ~half a day

`.env.example` ships `change-me` for every one of these.

- [ ] Generate: `POSTGRES_PASSWORD`, `INFLUXDB_ADMIN_PASSWORD`,
      `INFLUXDB_ADMIN_TOKEN`, `MQTT_INGESTION_PASSWORD`,
      `MQTT_BRIDGE_PASSWORD`, `JWT__Key`, `Encryption__Key`,
      and every per-tenant device password
      — `openssl rand -base64 36`, one per secret
- [ ] Store in AWS Secrets Manager / SSM; inject as env at deploy
- [ ] Confirm no secret is committed: `git grep -nE "change-me|BEGIN PRIVATE KEY"`
- [ ] Write down the rotation procedure and who performs it

**Verify:** a fresh deploy from the secret store alone, with no `.env` on
the host, comes up healthy.

### 3. TLS on 8883 · ~half a day

- [ ] Real certificate chain the devices will trust (public CA, or your own
      CA with the root pre-loaded onto devices). **Not** `generate-dev-certs.sh`
- [ ] Close plain 1883 to anything outside the container network
- [ ] Confirm the certificate's expiry is monitored

**Verify:**
```bash
openssl s_client -connect <host>:8883 -CAfile <ca.crt> </dev/null | head -20
mosquitto_pub -h <host> -p 1883 -t test -m x     # must FAIL from outside
```

### 4. Backups, off-host · ~2 hours

- [ ] Schedule `infrastructure/scripts/backup.sh` (cron or systemd timer)
- [ ] Set `S3_BUCKET` — a backup on the same host is not a backup
- [ ] Set a retention policy on the bucket
- [ ] Confirm all three artefacts appear: `postgres.dump`, `influxdb.tar`,
      `mosquitto-data.tar`

### 5. Rehearse one restore · ~half a day · **do not skip**

An untested backup is not a backup. Against a *separate* stack:

- [ ] Restore PostgreSQL, InfluxDB and the Mosquitto queue (procedure in
      production-readiness.md §5)
- [ ] Confirm device count, alert-rule count and latest telemetry timestamp
      match the source
- [ ] **Write down how long it took.** That number is your real recovery
      time, whatever the plan claims

### 6. Decide the PostgreSQL coupling · ~1 hour

PostgreSQL is both the acknowledgement boundary and the broker's device-auth
backend. The cache is now enabled, which covers already-seen devices for
short outages — it does not cover first-time connections during an outage,
and it delays revocation by up to the TTL.

- [ ] Pick one: keep the 300 s cache / add a read replica for auth / accept
      it and specify device-side buffering to vendors
- [ ] Mirror `mosquitto.conf.example`'s auth-cache block into your live
      `mosquitto.conf` on every environment (it is gitignored, so it does
      **not** travel with the repo)
- [ ] Record the decision in production-readiness.md §4

**Verify (this is still untested):** stop PostgreSQL, confirm an
already-connected device can reconnect, and that a *new* device cannot.

### 7. Finish monitoring · ~half a day

Both services expose `/metrics` and that is verified working. Nothing is
scraping them yet, so today an outage still lasts until a user complains.

- [ ] Point a scraper at ingestion `:9464/metrics` and API `/metrics`
- [ ] Keep both on the private network — they are unauthenticated by design
- [ ] Alert: **ingestion offline** (no scrape 2 min, or the retained
      `bms/ingestion/+/status` topic says offline)
- [ ] Alert: **broker queue above 60%** of the limit set in item 1
- [ ] Alert: nothing acknowledged while messages are arriving
- [ ] Alert: time-series write failures sustained
- [ ] Route all of the above to whoever Phase 0 named

**Verify:** stop ingestion and confirm a human actually receives the page.

---

## Phase 2 — Known issues to resolve or accept

- [ ] **VendorC nested-JSON payloads are rejected.**
      `HttpIngestionTests.PostVendorCPayload_NestedJson_IsDecodedMappedAndPersisted`
      fails with *"No recognized telemetry fields present after mapping"* —
      pre-existing on this branch, confirmed against a clean checkout. If any
      real device uses that format, every one of its messages is rejected.
      Fix it, or confirm no launch device uses that payload shape.
- [ ] **OpenTelemetry Prometheus exporters are `1.19.1-beta.1`.** Widely used,
      but if a beta dependency is unacceptable, switch to the stable OTLP
      exporter pushing to a collector (~1 hour).
- [ ] **Retention's unflushed-row warning.** If the log line *"Retention left
      N message(s) older than Xd in place"* ever appears, the backfill is
      stuck and the table has no ceiling. Fix the backfill — do not raise the
      retention window.

---

## Phase 3 — Cutover day

- [ ] `dotnet test` — unit and integration suites, from the deploy commit
- [ ] Migrations applied; confirm `IX_RawDeviceMessages_RetentionSweep` exists
- [ ] All six containers healthy
- [ ] One real device publishes end-to-end: appears in PostgreSQL, in
      InfluxDB, and on the dashboard live
- [ ] Retention worker logged `Raw message retention started: keeping N day(s)`
- [ ] Both `/metrics` endpoints returning data to the scraper
- [ ] Deliberately stop ingestion for 60 seconds; confirm the alert fires and
      the backlog drains with zero loss on restart

---

## Phase 4 — First week

- [ ] Raw message table size flat (retention is holding):
      ```sql
      SELECT pg_size_pretty(pg_total_relation_size('"RawDeviceMessages"')),
             count(*) FILTER (WHERE "TimeSeriesWrittenAt" IS NULL
                                AND "ProcessingStatus" = 'Persisted') AS stuck
      FROM "RawDeviceMessages";
      ```
- [ ] Peak broker queue depth well under the limit
- [ ] Rejection rate baselined, so you know what "abnormal" looks like
- [ ] p95 processing latency recorded as the baseline for later comparison
- [ ] One backup restored from the scheduled set, not the manual one

---

## Deliberately not blocking launch

Shared subscriptions for multiple ingestion instances, batched InfluxDB
writes, and time-partitioning the raw table are all scale work. Revisit when
the Phase 4 numbers say so — not before.

The exception: **run a load test at target device count early anyway.** Every
capacity figure in these documents is arithmetic, and that test is what
replaces it with measurement.
