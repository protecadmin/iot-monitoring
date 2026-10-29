# Common Issues (Production Failure Scenarios)

This platform is designed against a specific list of things that *will* go
wrong in production ([core.txt](../../core.txt) section 50). For each, the
pattern is the same: **detect, log, recover, notify, keep processing every
other device.** No single device's or vendor's misbehavior should ever
degrade the rest of the fleet.

| Scenario | Detected by | Recovery |
|---|---|---|
| MQTT broker unavailable | Connection/reconnect logging ([reliability.md](../mqtt/reliability.md)) | Automatic reconnect with backoff |
| HTTP invalid request | API model validation | 400 with structured error body ([api-design.md](../api/api-design.md)) |
| Malformed / unknown-structure / missing-field / wrong-type JSON | Decoder stage ([payload-decoding.md](../protocols/payload-decoding.md)) | Message rejected, raw payload preserved for replay |
| Invalid binary payload (CRC failure, truncated frame) | Binary decoder checksum/length checks | Frame rejected, not silently accepted with garbage values |
| Unknown device / unknown tenant | Identification stage can't resolve identity | Message rejected and logged — never guessed or defaulted |
| Duplicate telemetry | Deduplication stage ([data-flow.md](../architecture/data-flow.md)) | Extra copies silently dropped |
| Out-of-order / future / stale timestamp | Validation stage | Rejected per configured tolerance, logged |
| PostgreSQL / InfluxDB unavailable | Persistence call failure | Retried/backed off at the persistence boundary; ingestion for other devices continues independent of one store's availability |
| Alert flooding | Cooldown/debounce/duplicate-prevention in the alert engine ([core.txt](../../core.txt) section 31) | Repeated triggers within a cooldown window suppressed, not re-notified every time |
| Device offline / reconnect | Heartbeat timeout ([schema.md](../database/schema.md)) | `ONLINE -> OFFLINE` on timeout, `OFFLINE -> ONLINE` on resumed heartbeat |
| Command timeout | `DeviceCommand` status tracking | Marked `TIMEOUT`, distinct from `FAILED` (device responded with an error) |
| FOTA failure | Deployment status per device ([core.txt](../../core.txt) section 39) | Tracked independently per device; one device's failure doesn't affect others' deployments |

## Debugging with raw messages

Every one of the payload-related failures above is debuggable *after the
fact* because `RawDeviceMessage` is never discarded, even on decode/mapping
failure — see [payload-decoding.md](../protocols/payload-decoding.md) and
[core.txt](../../core.txt) section 11. Once a decoder or mapping bug is
fixed, the stored raw payload can be replayed through the corrected
pipeline (`core.txt` section 51) rather than the affected telemetry being
permanently lost.

## This document will grow

Phase 1 establishes *why* the architecture is shaped to make each of these
detectable and recoverable; the specific mechanisms (reconnect logic,
CRC checks, dedup keys, cooldown timers) are added in the phases that
implement each subsystem, and this document should gain concrete "if you
see X, check Y" entries as those land.
