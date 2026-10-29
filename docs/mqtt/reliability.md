# MQTT Reliability

This is the operational counterpart to [qos.md](qos.md) — QoS controls
per-message delivery guarantees; this document covers keeping the
*connection* itself reliable and secure, and what happens to telemetry when
a component fails.

## The durability chain

```
Device ──QoS 1──▶ Mosquitto ──QoS 1──▶ Bms.Ingestion ──▶ PostgreSQL (RawDeviceMessage)
                   │ persistent session        │                 │
                   │ "bms-ingestion" queues    │  PUBACK only    ▼
                   │ while ingestion is away   │  after this ─▶ ACK ─▶ InfluxDB / calculations /
                   ▼                           │                       alerts / real-time
              mosquitto.db (volume)            ▼
                                     transient failure ⇒ no ACK ⇒ redelivered
```

Three things together make a temporary ingestion outage lossless:

1. **Persistent session.** `MqttTransportAdapter` connects with MQTT 5,
   Clean Start = false, a fixed client id (`bms-ingestion`) and
   `Mqtt:SessionExpirySeconds` = 86400. MQTT 5's default Session Expiry
   Interval is **0**, which discards the session — and every message
   published while the client is away — the moment the connection drops,
   whatever Clean Start says. With a non-zero expiry the broker keeps the
   subscriptions and queues every QoS 1 message for them, for up to 24 h.
   A clean `docker compose stop ingestion` keeps the session too (expiry
   applies to any disconnect). The client id must never be randomized, and
   two ingestion instances must never share it (they would kick each other
   off and split the queue).
2. **Acknowledge only what is durable.** Received messages are acknowledged
   manually (`AutoAcknowledge = false`). The telemetry pipeline acknowledges
   the moment the `RawDeviceMessage` row is committed in PostgreSQL — as
   Persisted, Rejected, or Duplicate — and before InfluxDB, calculations,
   and alerts. A transient infrastructure failure before that point
   (`ITransientFailureClassifier`: PostgreSQL unreachable, timeouts,
   connection resets) is **not** acknowledged: `MqttIngestionWorker` retries
   it in place with backoff (`IngestionReliability:TransientRetry*`), which
   also holds back the messages behind it via the receive window
   (`Mqtt:ReceiveMaximum`, default 100), until it succeeds, the connection
   drops (the broker then redelivers it from the session), or the host
   stops. A failure that is about the message itself (malformed payload,
   failed validation, unknown device, an unexpected non-transient bug) is
   recorded as Rejected where possible, logged with the payload, and
   acknowledged — it can never block the pipeline forever.
3. **Sized broker queue on a persistent volume.** See "Sizing the broker
   queue" below.

Messages are processed one at a time per connection. That is deliberate:
QoS 1 acknowledgements must go back in the order messages arrived, and
calculations such as totalizer deltas depend on per-device order.
`Mqtt:ReceiveMaximum` sets how many messages are prefetched, not processing
parallelism.

## Connection lifecycle

The ingestion host's MQTT client (`MqttTransportAdapter`, see
[system-architecture.md](../architecture/system-architecture.md)) must
handle, explicitly:

- **Connect** — with credentials (see Authentication below) and a
  configured Last Will and Testament.
- **Reconnect** — on any unexpected disconnect, with backoff, without
  operator intervention. A broker restart or network blip must not require
  redeploying the ingestion service.
- **Graceful shutdown** — on host shutdown, publish a retained "offline"
  status, then disconnect cleanly (so the LWT does *not* fire). A message
  still in flight is simply not acknowledged; the session redelivers it on
  the next start (`stop_grace_period: 30s` in docker-compose.yml).
- **Single connect loop** — one `SemaphoreSlim` guards the connect/retry
  loop, so a disconnect during an initial connect never starts a second,
  parallel loop. Backoff is `Mqtt:ReconnectMinDelaySeconds` doubling to
  `Mqtt:ReconnectMaxDelaySeconds`.
- **Subscriptions** — on reconnect every remembered filter is re-subscribed
  (QoS 1, retained messages only for genuinely new subscriptions). On a
  resumed session this replaces, never duplicates, the subscription; on a
  new session (expired, or broker state lost) it recreates it. The log says
  which: "resumed persistent session" vs "NEW session ... nothing was
  queued".
- **Connection monitoring** — connect/disconnect/reconnect events are
  structured-logged (see
  [observability, core.txt section 44](../../core.txt)) so "broker
  unreachable" is diagnosable without guessing from missing telemetry.

## Keep Alive

The client and broker agree on an interval; if no packet arrives within
1.5x that interval, the broker treats the connection as dead and fires the
LWT. Set short enough that a real outage is detected promptly, long enough
that normal network jitter doesn't cause false disconnects.

## Authentication

`allow_anonymous false` is set in
[infrastructure/mosquitto/mosquitto.conf](../../infrastructure/mosquitto/mosquitto.conf)
— every client, device or backend service, authenticates with a
username/password from Mosquitto's password file (generated with
`mosquitto_passwd`, never committed — see
[infrastructure/mosquitto/passwd.example](../../infrastructure/mosquitto/passwd.example)).
Credentials are supplied to the API/ingestion host via environment
variables (`Mqtt__Username` / `Mqtt__Password`), never hardcoded — see
[core.txt](../../core.txt) section 41.

Device-facing credentials are one per tenant, not one shared credential
for every device: the username is the tenant slug, and
[infrastructure/mosquitto/acl](../../infrastructure/mosquitto/acl) uses
Mosquitto's `%u` pattern substitution to restrict each one to
`iot/{tenant-slug}/#` — a device can never be configured to publish under
a tenant it doesn't belong to, which is what actually matters for
multi-tenant isolation (one customer's device can't inject fake telemetry
into another customer's dashboard). It does **not** stop one device from
impersonating a different device within the *same* tenant — that needs
credentials issued and validated per device, which static password-file
auth can't provision dynamically as devices are created/deleted through
the API. The broker now runs the `mosquitto-go-auth` plugin with a PostgreSQL
backend (`MqttCredentials` table), so credentials are managed live; they are
still per tenant, not per device.

## MQTTS / TLS

Plain MQTT (port 1883) is acceptable on a trusted/dev network. Production
and any untrusted network path uses MQTTS (port 8883, TLS) so credentials
and telemetry aren't sent in cleartext. The listener is defined but
defined in `mosquitto.conf`, using the self-signed development certificates
from `infrastructure/mosquitto/certs/generate-dev-certs.sh`; production swaps
in certificates from a real CA.

## Duplicate handling

Covered in [qos.md](qos.md#duplicate-handling) — QoS 1 retries can produce
duplicates at the transport level; they're resolved once, centrally, during
[deduplication](../architecture/data-flow.md), not by the MQTT layer.
Redelivery after an outage is now the *normal* recovery path, so the
pipeline's duplicate identity is backed by a PostgreSQL unique index.

## Sizing the broker queue

Mosquitto holds queued messages for a disconnected persistent session **in
memory**, snapshots them to `mosquitto.db` on the `mosquitto-data` volume
every `autosave_interval` seconds and on clean shutdown, and **drops** new
messages for that client once either limit below is reached (logged by the
broker). The limits are per client, and everything ingestion subscribes to
counts: telemetry, heartbeats, events, command acks.

```
required max_queued_messages = devices × messages/second/device × longest outage (s) × safety factor
required max_queued_bytes    = required max_queued_messages × (average payload + topic + ~30 B overhead)
```

Worked example — 500 devices, telemetry every 10 s plus a heartbeat every
30 s (≈ 0.133 msg/s/device), 1 h outage, safety factor 1.5:
500 × 0.133 × 3600 × 1.5 ≈ **360 000 messages**; at ~250 B payload + ~50 B
topic/overhead ≈ **108 MB**. The broker host needs that much free RAM on
top of its normal footprint.

The defaults (`max_queued_messages 100000`, `max_queued_bytes 100 MiB`)
cover about 20 minutes for that example fleet. Set them per deployment via
environment variables — Mosquitto cannot read environment variables itself,
so `infrastructure/mosquitto/docker-entrypoint.sh` renders them into
`/mosquitto/conf.d/10-durability.conf` at start:

| Variable | Mosquitto option | Default |
|---|---|---|
| `MOSQUITTO_MAX_QUEUED_MESSAGES` | `max_queued_messages` | 100000 |
| `MOSQUITTO_MAX_QUEUED_BYTES` | `max_queued_bytes` | 104857600 |
| `MOSQUITTO_AUTOSAVE_INTERVAL` | `autosave_interval` | 60 s |
| `MOSQUITTO_PERSISTENT_CLIENT_EXPIRATION` | `persistent_client_expiration` | 1d |
| `MQTT_INGESTION_SESSION_EXPIRY_SECONDS` | ingestion's Session Expiry Interval | 86400 |

Keep `persistent_client_expiration` ≥ the ingestion session expiry. An
outage longer than the session expiry loses the session and its queue.

## What survives which failure

| Failure | Telemetry published during it | Notes |
|---|---|---|
| Ingestion stopped, crashed, or restarting | Queued by Mosquitto, delivered on reconnect | Up to the queue limits and the 24 h session expiry. Docker restarts a crashed container (`restart: unless-stopped`). |
| PostgreSQL down | Not acknowledged; retried in place, then held in the broker queue | Processing resumes when PostgreSQL returns. In production the broker's device authentication also uses PostgreSQL (go-auth). The authentication cache (`auth_opt_cache`, 300 s in `mosquitto.conf.example`) covers already-seen devices for the length of a short outage; a device connecting for the first time during one, or any device once the TTL lapses, is still refused and must buffer itself. |
| InfluxDB down | Persisted in PostgreSQL, acknowledged | `TimeSeriesWrittenAt` stays null; `TimeSeriesBackfillWorker` writes it later. |
| Mosquitto restarted cleanly | Queue saved on shutdown and restored | Ingestion reconnects and resumes its session. Devices must buffer while the broker itself is down. |
| Mosquitto crashed / killed | Queue restored from the last autosave | Up to `autosave_interval` (60 s) of queued messages can be lost. |
| Whole server down | As for a broker crash, plus whatever devices could not send | All containers come back automatically when Docker starts. |

Nothing on the server side can recover a message a device never managed to
hand to the broker: when the broker is unreachable, only device-side
buffering (store and forward with the device's own timestamp and sequence
number) prevents loss.

## Device-offline checks during an ingestion outage

While ingestion is down every device looks silent at once. Ingestion
publishes a retained status on `bms/ingestion/bms-ingestion/status`
(`online` every 15 s, `degraded` while it cannot record messages,
`offline` on clean stop and as its Last Will). Bms.Api's
`IngestionHealthTracker` reads it, and `OfflineSweepWorker` stops counting
device silence while ingestion is not healthy, and for
`IngestionHealth:RecoveryGraceSeconds` (180 s) after it recovers so the
queued backlog can drain.

Silence is frozen rather than ignored: the sweep keeps running, but it
measures each device's silence only up to the instant ingestion stopped
being trustworthy. A device that fell quiet with the outage stops ageing
and keeps its status until ingestion is back; one that had already been
silent past `DeviceLifecycle:OfflineAfterSeconds` **before** the outage
still flips to Offline and still raises its alerts, because the outage
does not explain that silence. This is what keeps a device nobody has
heard from in days from showing as Online through a long outage — or
through one that outlives the Bms.Api process, since the freeze point
restarts with it.

The freeze and resume are logged. Set
`IngestionHealth:PauseDeviceSilenceChecksWhenUnhealthy` to false to count
silence unconditionally.

## Observability

Structured logs cover connect, disconnect, reconnect attempts, session
resumed versus new, every transient retry ("NOT acknowledged"), permanent
failures (with a payload preview), backfill passes, and sweep pause/resume.
Counters on the `Bms.Ingestion` meter (`dotnet-counters monitor --counters
Bms.Ingestion -n Bms.Ingestion`): `messages_received`,
`messages_redelivered`, `messages_persisted`, `messages_rejected`,
`messages_duplicate`, `messages_acknowledged`, `transient_failures`,
`permanent_failures`, `timeseries_write_failures`, `timeseries_backfilled`,
`broker_reconnects`, and the `processing_latency` histogram. The broker's
own queue depth for the ingestion client is visible with Mosquitto's
`$SYS/broker/store/messages/count` topic.

## Testing

`tests/reliability/run-reliability-tests.sh` runs each failure above
against an isolated Docker stack and checks PostgreSQL and InfluxDB
afterwards.

## Why this matters for a "learning" platform

Every one of these — reconnect, graceful shutdown, LWT, keep-alive tuning,
auth, TLS — is a thing that *will* go wrong in a real deployment (a broker
restart during a deploy, a device with a flaky cell modem, a forgotten
credential rotation). Building them in from Phase 5 rather than bolting them
on later is the difference between "the demo works" and "this survives
production."
