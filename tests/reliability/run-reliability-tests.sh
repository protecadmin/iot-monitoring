#!/usr/bin/env bash
# End-to-end failure/recovery tests for MQTT ingestion durability, run
# against a real, isolated Docker stack (project "bms-reliability", its own
# volumes and non-default ports — it never touches your dev stack or the
# local PostgreSQL). Uses the production docker-compose.yml plus
# tests/reliability/docker-compose.reliability.yml.
#
#   tests/reliability/run-reliability-tests.sh            # full run (~10 min)
#   OUTAGE_SECONDS=10 tests/reliability/run-reliability-tests.sh
#   KEEP_STACK=1 tests/reliability/run-reliability-tests.sh  # leave it running
#
# Exit code is non-zero if any scenario fails.
set -euo pipefail
cd "$(dirname "$0")/../.."

OUTAGE_SECONDS="${OUTAGE_SECONDS:-60}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-180}"
C=(docker compose --env-file tests/reliability/reliability.env -f docker-compose.yml -f tests/reliability/docker-compose.reliability.yml)
TOPIC="iot/whirlpool/chennai-factory/device-001/telemetry"
DEVICE_PASSWORD="reliability-device-pw"
SERVICE_PASSWORD="reliability-test-only"
INFLUX_TOKEN="reliability-test-only-influx-token"
SEQ_BASE=$(( $(date +%s) * 1000 ))
RESULTS=()
FAILED=0

log() { printf '\n\033[1m[%s] %s\033[0m\n' "$(date +%H:%M:%S)" "$*"; }
pass() { RESULTS+=("PASS  $1 — $2"); printf '  \033[32mPASS\033[0m %s — %s\n' "$1" "$2"; }
fail() { RESULTS+=("FAIL  $1 — $2"); FAILED=1; printf '  \033[31mFAIL\033[0m %s — %s\n' "$1" "$2"; }

sql() { "${C[@]}" exec -T postgres psql -U bms_app -d bms_platform -Atc "$1"; }
container() { "${C[@]}" ps -q "$1"; }

# Persisted / Rejected rows for device-001 telemetry within a sequence range.
count_status() { sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"ProcessingStatus\" = '$1' and \"SequenceNumber\" between $2 and $3"; }

wait_until() { # description, timeout, command...
    local what=$1 timeout=$2; shift 2
    local deadline=$(( $(date +%s) + timeout ))
    until "$@" >/dev/null 2>&1; do
        if (( $(date +%s) > deadline )); then return 1; fi
        sleep 2
    done
}

persisted_equals() { [[ "$(count_status Persisted "$2" "$3")" == "$1" ]]; }

# Publishes QoS 1 / MQTT 5 telemetry for sequences FROM..TO, each with its
# own device timestamp — exactly as a device would.
publish_range() {
    "${C[@]}" exec -T mosquitto bash -c "
        for i in \$(seq $1 $2); do
            ts=\$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)
            mosquitto_pub -h 127.0.0.1 -p 1883 -u whirlpool -P '$DEVICE_PASSWORD' -V mqttv5 -q 1 -t '$TOPIC' \
                -m \"{\\\"deviceId\\\":\\\"device-001\\\",\\\"temp\\\":22.5,\\\"hum\\\":50,\\\"seq\\\":\$i,\\\"timestamp\\\":\\\"\$ts\\\"}\"
        done"
}

publish_raw() { # payload
    "${C[@]}" exec -T mosquitto mosquitto_pub -h 127.0.0.1 -p 1883 -u whirlpool -P "$DEVICE_PASSWORD" -V mqttv5 -q 1 -t "$TOPIC" -m "$1"
}

ingestion_logs_since() { docker logs --since "$1" "$(container ingestion)" 2>&1; }

generate_credentials() {
    mkdir -p tests/reliability/.generated
    : > tests/reliability/.generated/passwd
    for entry in "bms-ingestion:$SERVICE_PASSWORD" "bms-api-realtime-bridge:$SERVICE_PASSWORD" "whirlpool:$DEVICE_PASSWORD"; do
        local user=${entry%%:*} pw=${entry#*:}
        local hash
        hash=$(docker run --rm --entrypoint /mosquitto/pw iegomez/mosquitto-go-auth:latest -h bcrypt -p "$pw")
        echo "$user:$hash" >> tests/reliability/.generated/passwd
    done
}

cleanup() {
    if [[ "${KEEP_STACK:-0}" != "1" ]]; then
        log "Tearing down the reliability stack"
        "${C[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
log "Starting isolated stack (fresh volumes)"
"${C[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
generate_credentials
"${C[@]}" up -d --build postgres influxdb mosquitto api ingestion

wait_until "API live" 300 curl -sf "http://localhost:55000/health/live" || { echo "API never became live"; "${C[@]}" logs api | tail -50; exit 1; }
wait_until "ingestion connected" 120 bash -c "docker logs $(container ingestion) 2>&1 | grep -q 'MQTT connected'" || { "${C[@]}" logs ingestion | tail -50; exit 1; }
DEVICE_ID=$(sql "select \"Id\" from \"Devices\" where \"DeviceCode\" = 'device-001'")
echo "  device-001 = $DEVICE_ID"

# --- Baseline ---------------------------------------------------------------
log "Baseline: 5 messages with everything up"
S=$((SEQ_BASE)); publish_range $S $((S+4))
if wait_until "" 60 persisted_equals 5 $S $((S+4)); then pass "Baseline" "5/5 persisted"; else fail "Baseline" "$(count_status Persisted $S $((S+4)))/5 persisted"; fi

# --- TEST 1: ingestion stopped while devices publish ------------------------
log "TEST 1: stop ingestion for ${OUTAGE_SECONDS}s while devices publish"
S=$((SEQ_BASE + 1000))
"${C[@]}" stop ingestion >/dev/null
publish_range $S $((S+9))
sleep "$OUTAGE_SECONDS"
publish_range $((S+10)) $((S+19))
SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${C[@]}" start ingestion >/dev/null
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 20 $S $((S+19)); then
    pass "TEST 1 ingestion stopped" "20/20 published during the outage were queued by Mosquitto and persisted after restart"
else
    fail "TEST 1 ingestion stopped" "$(count_status Persisted $S $((S+19)))/20 persisted"
fi
if ingestion_logs_since "$SINCE" | grep -q "resumed persistent session"; then pass "TEST 1 session" "ingestion resumed its persistent MQTT session"; else fail "TEST 1 session" "no 'resumed persistent session' log"; fi

# --- TEST 2: ingestion process killed (crash) --------------------------------
log "TEST 2: kill -9 the ingestion process (outside Docker's control) and rely on the restart policy"
S=$((SEQ_BASE + 2000))
CID=$(container ingestion)
RESTARTS_BEFORE=$(docker inspect -f '{{.RestartCount}}' "$CID")
HOST_PID=$(docker inspect -f '{{.State.Pid}}' "$CID")
docker run --rm --privileged --pid=host alpine:3 kill -9 "$HOST_PID" >/dev/null 2>&1 || docker kill -s KILL "$CID" >/dev/null
publish_range $S $((S+9))
if wait_until "" 120 bash -c "[[ \$(docker inspect -f '{{.RestartCount}}' $CID) -gt $RESTARTS_BEFORE && \$(docker inspect -f '{{.State.Running}}' $CID) == true ]]"; then
    pass "TEST 2 restart policy" "Docker restarted the killed ingestion container (RestartCount $RESTARTS_BEFORE -> $(docker inspect -f '{{.RestartCount}}' "$CID"))"
else
    fail "TEST 2 restart policy" "container not restarted (RestartCount $(docker inspect -f '{{.RestartCount}}' "$CID"), running=$(docker inspect -f '{{.State.Running}}' "$CID"))"
    "${C[@]}" start ingestion >/dev/null
fi
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 10 $S $((S+9)); then pass "TEST 2 crash" "10/10 published while it was dead were processed after restart"; else fail "TEST 2 crash" "$(count_status Persisted $S $((S+9)))/10 persisted"; fi

# --- TEST 3: PostgreSQL unavailable --------------------------------------------
log "TEST 3: stop PostgreSQL while devices publish (ingestion stays up)"
S=$((SEQ_BASE + 3000))
SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${C[@]}" stop postgres >/dev/null
publish_range $S $((S+9))
# EF Core's own retries run first (up to ~1 min), then the worker's.
if wait_until "" 150 bash -c "docker logs --since $SINCE $(container ingestion) 2>&1 | grep -q 'NOT acknowledged'"; then
    pass "TEST 3 no ack" "ingestion refused to acknowledge while PostgreSQL was down"
else
    fail "TEST 3 no ack" "no 'NOT acknowledged' log while PostgreSQL was down"
fi
"${C[@]}" start postgres >/dev/null
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 10 $S $((S+9)); then
    pass "TEST 3 PostgreSQL down" "10/10 processed after PostgreSQL returned; $(count_status Rejected $S $((S+9))) rejected"
else
    fail "TEST 3 PostgreSQL down" "$(count_status Persisted $S $((S+9)))/10 persisted after PostgreSQL returned"
fi

# --- TEST 4: InfluxDB unavailable --------------------------------------------
log "TEST 4: stop InfluxDB while devices publish"
S=$((SEQ_BASE + 4000))
"${C[@]}" stop influxdb >/dev/null
publish_range $S $((S+9))
wait_until "" 120 persisted_equals 10 $S $((S+9)) || true
PENDING=$(sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"SequenceNumber\" between $S and $((S+9)) and \"TimeSeriesWrittenAt\" is null")
if [[ "$(count_status Persisted $S $((S+9)))" == "10" && "$PENDING" == "10" ]]; then
    pass "TEST 4 raw kept" "10/10 raw messages persisted in PostgreSQL, all 10 marked pending for InfluxDB"
else
    fail "TEST 4 raw kept" "persisted=$(count_status Persisted $S $((S+9))) pendingInflux=$PENDING"
fi
"${C[@]}" start influxdb >/dev/null
pending_zero() { [[ "$(sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"SequenceNumber\" between $S and $((S+9)) and \"TimeSeriesWrittenAt\" is null")" == "0" ]]; }
if wait_until "" "$WAIT_TIMEOUT" pending_zero; then pass "TEST 4 backfill" "all 10 backfilled into InfluxDB"; else fail "TEST 4 backfill" "still pending after ${WAIT_TIMEOUT}s"; fi
RAW_ROWS=$(sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"SequenceNumber\" between $S and $((S+9))")
[[ "$RAW_ROWS" == "10" ]] && pass "TEST 4 no duplicates" "exactly 10 raw rows" || fail "TEST 4 no duplicates" "$RAW_ROWS raw rows"
PG_TOTAL=$(sql "select count(*) from \"RawDeviceMessages\" where \"DeviceId\" = '$DEVICE_ID' and \"ProcessingStatus\" = 'Persisted'")
INFLUX_TOTAL=$("${C[@]}" exec -T influxdb influx query --org bms-platform --token "$INFLUX_TOKEN" --raw \
    "from(bucket: \"telemetry\") |> range(start: -6h) |> filter(fn: (r) => r._measurement == \"telemetry\" and r.deviceId == \"$DEVICE_ID\" and r._field == \"temperature\") |> count()" \
    | tr -d '\r' | awk -F, '/^,,/ {print $6}' | tail -1)
[[ "$INFLUX_TOTAL" == "$PG_TOTAL" ]] && pass "TEST 4 consistency" "InfluxDB points ($INFLUX_TOTAL) == persisted raw rows ($PG_TOTAL)" || fail "TEST 4 consistency" "InfluxDB points=$INFLUX_TOTAL persisted rows=$PG_TOTAL"

# --- TEST 5: Mosquitto restarts ----------------------------------------------
log "TEST 5: restart Mosquitto while ingestion is running"
S=$((SEQ_BASE + 5000))
SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${C[@]}" restart mosquitto >/dev/null
wait_until "" 120 bash -c "docker logs --since $SINCE $(container ingestion) 2>&1 | grep -q 'MQTT connected'" || true
publish_range $S $((S+9))
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 10 $S $((S+9)); then pass "TEST 5 broker restart" "ingestion reconnected, subscriptions intact, 10/10 persisted"; else fail "TEST 5 broker restart" "$(count_status Persisted $S $((S+9)))/10 persisted"; fi

log "TEST 5b: messages queued for a stopped ingestion survive a Mosquitto restart"
S=$((SEQ_BASE + 5500))
"${C[@]}" stop ingestion >/dev/null
publish_range $S $((S+9))
"${C[@]}" restart mosquitto >/dev/null
"${C[@]}" start ingestion >/dev/null
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 10 $S $((S+9)); then pass "TEST 5b queue persistence" "10/10 queued messages survived the broker restart"; else fail "TEST 5b queue persistence" "$(count_status Persisted $S $((S+9)))/10 persisted"; fi

# --- TEST 6: whole stack restart ---------------------------------------------
log "TEST 6: restart the entire stack (with messages queued for ingestion)"
S=$((SEQ_BASE + 6000))
BEFORE=$(sql "select count(*) from \"RawDeviceMessages\"")
"${C[@]}" stop ingestion >/dev/null
publish_range $S $((S+9))
"${C[@]}" stop >/dev/null
"${C[@]}" up -d postgres influxdb mosquitto api ingestion >/dev/null
wait_until "" 300 curl -sf "http://localhost:55000/health/live" || true
if wait_until "" "$WAIT_TIMEOUT" persisted_equals 10 $S $((S+9)); then pass "TEST 6 stack restart" "10/10 queued messages delivered after full restart"; else fail "TEST 6 stack restart" "$(count_status Persisted $S $((S+9)))/10 persisted"; fi
AFTER=$(sql "select count(*) from \"RawDeviceMessages\"")
(( AFTER >= BEFORE + 10 )) && pass "TEST 6 data retained" "raw rows $BEFORE -> $AFTER (nothing lost from PostgreSQL)" || fail "TEST 6 data retained" "raw rows $BEFORE -> $AFTER"

# --- TEST 7: same message twice ------------------------------------------------
log "TEST 7: the same MQTT message delivered twice"
S=$((SEQ_BASE + 7000))
DUP="{\"deviceId\":\"device-001\",\"temp\":22.5,\"hum\":50,\"seq\":$S,\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%S.000Z)\"}"
publish_raw "$DUP"; publish_raw "$DUP"
rows_for_seq_equal() { [[ "$(sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"SequenceNumber\" = $2")" == "$1" ]]; }
wait_until "" 60 rows_for_seq_equal 2 $S || true
P=$(count_status Persisted $S $S); R=$(count_status Rejected $S $S)
[[ "$P" == "1" && "$R" == "1" ]] && pass "TEST 7 duplicate" "1 Persisted + 1 recorded as duplicate" || fail "TEST 7 duplicate" "persisted=$P rejected=$R"

# --- TEST 8: malformed payload -------------------------------------------------
log "TEST 8: malformed payload followed by a valid one"
S=$((SEQ_BASE + 8000))
publish_raw '{this is not json'
publish_range $S $S
if wait_until "" 60 persisted_equals 1 $S $S; then
    BAD=$(sql "select count(*) from \"RawDeviceMessages\" where \"Topic\" = '$TOPIC' and \"ProcessingStatus\" = 'Rejected' and \"ProcessingError\" like 'Decode failed%' and \"ReceivedAt\" > now() - interval '5 minutes'")
    (( BAD >= 1 )) && pass "TEST 8 malformed" "recorded as Rejected (decode failed); the next valid message was processed" || fail "TEST 8 malformed" "no Rejected decode-failure row"
else
    fail "TEST 8 malformed" "valid message after the malformed one was not processed"
fi

# --- TEST 9: sequence reset ----------------------------------------------------
log "TEST 9: device sequence counter reset / wraparound"
S=$((SEQ_BASE + 9000))
publish_raw "{\"deviceId\":\"device-001\",\"temp\":22.5,\"hum\":50,\"seq\":$S,\"timestamp\":\"$(date -u -d '-10 minutes' +%Y-%m-%dT%H:%M:%S.000Z 2>/dev/null || date -u -v-10M +%Y-%m-%dT%H:%M:%S.000Z)\"}"
publish_raw "{\"deviceId\":\"device-001\",\"temp\":23.5,\"hum\":51,\"seq\":$S,\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%S.000Z)\"}"
# Sequence-only device (no timestamp): a byte-identical redelivery is a
# duplicate, a different reading reusing the number (wraparound) is not.
publish_raw "{\"deviceId\":\"device-001\",\"temp\":24.0,\"hum\":52,\"seq\":$((S+1))}"
publish_raw "{\"deviceId\":\"device-001\",\"temp\":24.0,\"hum\":52,\"seq\":$((S+1))}"
publish_raw "{\"deviceId\":\"device-001\",\"temp\":24.5,\"hum\":53,\"seq\":$((S+1))}"
sleep 10
P0=$(count_status Persisted $S $S); P1=$(count_status Persisted $((S+1)) $((S+1))); R1=$(count_status Rejected $((S+1)) $((S+1)))
[[ "$P0" == "2" ]] && pass "TEST 9 reset" "same sequence, new device timestamp: both readings kept" || fail "TEST 9 reset" "persisted=$P0 (expected 2)"
[[ "$P1" == "2" && "$R1" == "1" ]] && pass "TEST 9 seq-only" "identical redelivery dropped, wrapped sequence with new values kept" || fail "TEST 9 seq-only" "persisted=$P1 rejected=$R1 (expected 2 and 1)"

# --- TEST 10: device-offline checks pause while ingestion is down -----------
log "TEST 10: API pauses device-silence checks while ingestion is down"
# Start from a steady state: checks running (earlier restarts may still be
# inside their recovery grace period).
sweep_running() { [[ "$(docker logs "$(container api)" 2>&1 | grep -E 'evaluation (PAUSED|RESUMED)' | tail -1)" == *RESUMED* ]]; }
wait_until "" 120 sweep_running || true
SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
"${C[@]}" stop ingestion >/dev/null
sleep 15
"${C[@]}" start ingestion >/dev/null
wait_until "" 120 bash -c "docker logs --since $SINCE $(container api) 2>&1 | grep -q 'evaluation RESUMED'" || true
API_LOG=$(docker logs --since "$SINCE" "$(container api)" 2>&1)
if grep -q "evaluation PAUSED" <<<"$API_LOG" && grep -q "evaluation RESUMED" <<<"$API_LOG"; then
    pass "TEST 10 offline suppression" "paused while ingestion was offline, resumed after the recovery grace period"
else
    fail "TEST 10 offline suppression" "$(grep -E 'PAUSED|RESUMED' <<<"$API_LOG" | head -3)"
fi

# ---------------------------------------------------------------------------
log "Summary"
printf '%s\n' "${RESULTS[@]}"
exit $FAILED
