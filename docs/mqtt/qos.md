# MQTT QoS

## The three levels

| QoS | Guarantee | Cost |
|---|---|---|
| 0 | At most once — fire and forget, no ack | Cheapest, can silently lose messages |
| 1 | At least once — retried until acked | May deliver duplicates (handled by [deduplication](../architecture/data-flow.md)) |
| 2 | Exactly once — four-way handshake | Most expensive: extra round trips, more broker/client state |

## What this platform uses

| Message type | QoS | Why |
|---|---|---|
| `telemetry` | 1 | Losing a reading is worse than an occasional duplicate; duplicates are cheap to filter downstream. |
| `heartbeat` | 1 | Same reasoning — a dropped heartbeat could wrongly mark a device offline. |
| `commands` | 1 | A lost command silently fails to control a device; a duplicate is idempotent by command id. |
| Critical `events` | 1 or 2, per event | Only escalate to QoS 2 for events where a duplicate is actually harmful (e.g. a one-shot safety trip), not by default. |

**QoS 2 is not the default anywhere.** Its exactly-once guarantee is
achieved at real cost (more broker state, more round trips, lower
throughput), and this platform's deduplication stage
([data-flow.md](../architecture/data-flow.md)) already makes QoS 1's
occasional duplicate harmless. Reaching for QoS 2 "to be safe" everywhere
would pay that cost for no benefit — it's reserved for the rare case where
a duplicate is actively dangerous, not merely redundant.

## Retained messages

A retained message is stored by the broker and delivered immediately to any
new subscriber, even one that connects after the message was published —
useful for "what's the last known state," useless (and expensive) for a
firehose of readings.

- **Not used for high-volume telemetry.** Retaining every telemetry publish
  would mean the broker holds and immediately replays a value that's
  usually stale by the time anyone subscribes — telemetry consumers want
  history from InfluxDB, not "whatever the last MQTT message happened to
  be."
- **Reasonable for `status`** (and similar low-frequency, "current state"
  topics), where a newly-connecting monitoring tool benefits from
  immediately knowing a device's last reported status without waiting for
  its next publish.

This is a default, not a law — a specific use case that genuinely needs a
retained telemetry value would be a reason to revisit it, per
[core.txt](../../core.txt) section 8.

## Last Will and Testament (LWT)

Every device connection registers a will message (typically on its
`status` topic) that the broker publishes automatically if the connection
drops without a clean disconnect (crash, network loss, power loss). This is
what lets the platform detect "device went dark" independently of — and
faster than — the heartbeat-timeout mechanism in
[device lifecycle](../database/schema.md).

## Keep Alive, reconnect, and connection monitoring

Keep Alive is the interval within which the client must send *some* packet
(a PINGREQ if nothing else) or the broker considers the connection dead and
fires the LWT. The ingestion host's own broker connection uses the same
mechanism in reverse: automatic reconnect with backoff on disconnect, and
connection-state logging/health reporting so an operator can see "ingestion
lost its broker connection" as distinct from "a device went offline." See
[reliability.md](reliability.md) for the full reconnect/shutdown story.

## Duplicate handling

QoS 1 (and network retries in general) can deliver the same message more
than once — and after an ingestion outage, redelivery from the persistent
session is the normal recovery path. This is handled once, centrally, in the
[deduplication stage](../architecture/data-flow.md) of the pipeline rather
than by every consumer having to reason about MQTT delivery semantics itself.

A reading's identity comes only from values the device supplies, so a
redelivery (byte-identical) matches while a later reading does not:

| Device sends | Identity (`DedupKey`) | Duplicate when |
|---|---|---|
| sequence + timestamp | `s{seq}@{unix ms}` | same key already Persisted — enforced by a unique index on (DeviceId, DedupKey) |
| timestamp only | `@{unix ms}` | same, unique index |
| sequence only | none | same sequence **and** byte-identical payload received within `IngestionReliability:DuplicateWindowHours` (24 h) |
| neither | none | never — nothing distinguishes a redelivery from a new identical reading, so nothing is dropped |

A sequence counter that resets on reboot or wraps (16-bit binary frames) is
therefore not mistaken for a duplicate: its timestamp or its values differ.
Duplicates are recorded as Rejected rows ("Duplicate telemetry ...") and
acknowledged, and never reach InfluxDB, calculations, or alerts.
