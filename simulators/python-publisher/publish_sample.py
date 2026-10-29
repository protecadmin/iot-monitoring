#!/usr/bin/env python3
"""
MQTT sample publisher for the BMS Industrial IoT Platform — publishes the
way our own gateway does: JSON over MQTT, keyed by BMS parameter keys.

Unlike simulators/Bms.DeviceSimulator (a continuous background process
that generates random-walk drift), this sends exactly the values YOU
choose — either once and exit (the default), or continuously with
--interval, holding the connection open like a real device would.

Continuous mode (--interval) does NOT repeat the same fixed value forever:
each tick nudges every field by a small random amount (a bounded random
walk, like a real sensor drifting) starting from whatever you passed with
--field, and — because a real device doesn't always behave — once every
15 to 30 minutes (randomized) it spikes one field to a clearly abnormal
value for a single tick, so a long-running stream actually exercises your
alert rules instead of sitting at one number forever. One-shot mode (no
--interval) is unaffected: it sends exactly what you asked for, once.

Usage:
    python3 publish_sample.py --list
    python3 publish_sample.py --device-id device-001 --field temperature=55 --field humidity=70
    python3 publish_sample.py --device-id device-017 --field current=25 --field voltage=230 --field powerFactor=0.9
    python3 publish_sample.py --device-id device-018 --field runStatus=0      # pump stopped

    # Continuous, drifting, with a random anomaly every 15-30 min, until Ctrl+C:
    python3 publish_sample.py --device-id device-001 --field temperature=25 --interval 5

Needs paho-mqtt:
    python3 -m venv .venv && .venv/bin/pip install paho-mqtt
    .venv/bin/python3 publish_sample.py --device-id device-001 ...
"""

import argparse
import json
import random
import sys
import time

def gateway_fields(*keys: str) -> dict[str, str]:
    """Our gateway publishes BMS parameter keys as-is (no payload mapping in
    BMS), so each field goes on the wire under its own name."""
    return {key: key for key in keys}


# Tenant/site per seeded device (see
# Bms.Infrastructure.Persistence.DbSeeder). Every MQTT device sends the BMS
# parameter keys of its device type, exactly like our own gateway.
DEVICE_PROFILES = {
    "device-001": dict(tenant="whirlpool", site="chennai-factory", json_fields=gateway_fields("temperature", "humidity")),
    "device-005": dict(tenant="whirlpool", site="pune-factory", json_fields=gateway_fields("pressure", "temperature"),
                       defaults={"pressure": 1013.0, "temperature": 25.0}),
    "device-006": dict(tenant="abc-manufacturing", site="bangalore-factory", json_fields=gateway_fields("temperature", "vibration"),
                       defaults={"temperature": 30.0, "vibration": 2.5}),
    "device-008": dict(tenant="abc-manufacturing", site="bangalore-factory", json_fields=gateway_fields("temperature", "humidity")),
    "device-010": dict(tenant="demo-company", site="demo-site", json_fields=gateway_fields("waterLevel", "temperature"),
                       defaults={"waterLevel": 120.0, "temperature": 25.0}),
    # WBGT is published as sent by the meter, so --field wbgt=33 is taken
    # literally rather than recomputed from the other temperatures.
    "device-012": dict(tenant="abc-manufacturing", site="bangalore-factory",
                       json_fields=gateway_fields("wbgt", "dryBulb", "wetBulb", "globeTemp", "humidity"),
                       defaults={"wbgt": 28.5, "dryBulb": 32.0, "wetBulb": 25.5, "globeTemp": 37.0, "humidity": 55.0}),
    "device-013": dict(tenant="abc-manufacturing", site="bangalore-factory", json_fields=gateway_fields("noiseLevel", "noisePeak"),
                       defaults={"noiseLevel": 80.0, "noisePeak": 102.0}),
    # Chennai health & safety set — vibration, heat stress and noise.
    "device-014": dict(tenant="whirlpool", site="chennai-factory", json_fields=gateway_fields("temperature", "vibration"),
                       defaults={"temperature": 32.0, "vibration": 2.8}),
    "device-015": dict(tenant="whirlpool", site="chennai-factory",
                       json_fields=gateway_fields("wbgt", "dryBulb", "wetBulb", "globeTemp", "humidity"),
                       defaults={"wbgt": 29.5, "dryBulb": 34.0, "wetBulb": 26.5, "globeTemp": 39.0, "humidity": 65.0}),
    "device-016": dict(tenant="whirlpool", site="chennai-factory", json_fields=gateway_fields("noiseLevel", "noisePeak"),
                       defaults={"noiseLevel": 82.0, "noisePeak": 104.0}),
    # Energy Meter. counters=True: energy (kWh) is a cumulative register that
    # only climbs while streaming. activePower is sent only when passed as
    # --field activePower=2.5 — otherwise BMS calculates it, if a formula is set up.
    "device-017": dict(tenant="whirlpool", site="chennai-factory",
                       json_fields=gateway_fields("current", "voltage", "powerFactor", "energy", "activePower"),
                       defaults={"current": 12.0, "voltage": 230.0, "powerFactor": 0.95, "energy": 100000.0},
                       counters=True),
    # Pump Monitor — an on/off check. runStatus is digital (1 = running,
    # 0 = stopped) and flips now and then while streaming; a stopped pump
    # draws no motor current, so motorCurrent is sent as 0 while it's off.
    "device-018": dict(tenant="whirlpool", site="chennai-factory", json_fields=gateway_fields("runStatus", "motorCurrent"),
                       defaults={"runStatus": 1, "motorCurrent": 12.0},
                       idle_when_off={"runStatus": ["motorCurrent"]}),
}

DEFAULT_MQTT_FIELDS = {"temperature": 25.0, "humidity": 50.0}

# Per-field: how much a single tick can drift (+/-), the sane clamp range,
# and the value an injected anomaly jumps to. Anomaly values are chosen to
# clearly cross the seeded demo alert thresholds (e.g. "High Temperature"
# fires above 50) while still landing inside the device type's own
# Min/Max, so the reading is a believable fault, not something the
# platform's own validation would just reject outright.
FIELD_META = {
    "temperature": dict(drift=0.6, min=-10, max=60, anomaly=70),
    "humidity": dict(drift=1.5, min=10, max=100, anomaly=95),
    "pressure": dict(drift=0.8, min=950, max=1050, anomaly=850),
    "battery": dict(drift=0.3, min=0, max=100, anomaly=10),
    "current": dict(drift=0.8, min=0, max=50, anomaly=45),
    "voltage": dict(drift=1.5, min=180, max=260, anomaly=255),
    "powerFactor": dict(drift=0.02, min=0, max=1, anomaly=0.3),
    "co2": dict(drift=15, min=350, max=2000, anomaly=1800),
    "pm25": dict(drift=2, min=0, max=300, anomaly=250),
    "voc": dict(drift=10, min=0, max=2000, anomaly=1800),
    "waterLevel": dict(drift=1, min=0, max=500, anomaly=480),
    "vibration": dict(drift=0.3, min=0.2, max=11, anomaly=9.5),
    "wbgt": dict(drift=0.3, min=18, max=38, anomaly=33.5),
    "dryBulb": dict(drift=0.4, min=20, max=45, anomaly=43),
    "wetBulb": dict(drift=0.3, min=15, max=35, anomaly=31),
    "globeTemp": dict(drift=0.5, min=20, max=60, anomaly=55),
    "noiseLevel": dict(drift=1.5, min=55, max=105, anomaly=96),
    "noisePeak": dict(drift=1.5, min=70, max=150, anomaly=140),
    # Digital: no drift — each tick has a `flip` chance of toggling 0 <-> 1.
    "runStatus": dict(digital=True, flip=0.03, anomaly=0),
    "motorCurrent": dict(drift=0.5, min=5, max=40, anomaly=35),
}

ANOMALY_MIN_SECONDS = 15 * 60
ANOMALY_MAX_SECONDS = 30 * 60


class DriftingValues:
    """Random-walks a starting set of values tick by tick, and once every
    15-30 minutes (randomized, re-rolled after each occurrence) spikes one
    field to FIELD_META's anomaly value for a single tick."""

    def __init__(self, initial: dict[str, float], device_id: str):
        self.values = dict(initial)
        self.device_id = device_id
        self._schedule_next_anomaly()

    def _schedule_next_anomaly(self) -> None:
        self.next_anomaly_at = time.time() + random.uniform(ANOMALY_MIN_SECONDS, ANOMALY_MAX_SECONDS)

    def tick(self) -> dict[str, float]:
        for name, value in self.values.items():
            meta = FIELD_META.get(name)
            if meta is None:
                continue
            if meta.get("digital"):
                if random.random() < meta["flip"]:
                    self.values[name] = 0 if value else 1
                    print(f"[{self.device_id}] {name} -> {'ON' if self.values[name] else 'OFF'}")
                continue
            value += random.uniform(-meta["drift"], meta["drift"])
            self.values[name] = max(meta["min"], min(meta["max"], value))

        if time.time() >= self.next_anomaly_at:
            name = random.choice(list(self.values.keys()))
            meta = FIELD_META.get(name)
            if meta is not None:
                self.values[name] = meta["anomaly"]
                print(f"[{self.device_id}] ** ANOMALY ** {name} -> {meta['anomaly']}")
            self._schedule_next_anomaly()

        return self.values


def parse_fields(pairs: list[str]) -> dict[str, float]:
    fields = {}
    for pair in pairs:
        if "=" not in pair:
            sys.exit(f"--field must be key=value, got: {pair!r}")
        key, value = pair.split("=", 1)
        try:
            fields[key.strip()] = float(value)
        except ValueError:
            sys.exit(f"--field {pair!r}: {value!r} is not a number")
    return fields


def build_mqtt_payload(profile: dict, values: dict[str, float]) -> str:
    body = {}
    # Fields that read 0 while their on/off field is off (a stopped pump draws no current).
    idle = {f for switch, fields in profile.get("idle_when_off", {}).items() if not values.get(switch, 1) for f in fields}
    for name, wire_key in profile["json_fields"].items():
        if name not in values:
            continue
        if FIELD_META.get(name, {}).get("digital"):
            body[wire_key] = 1 if values[name] else 0
            continue
        value = 0.0 if name in idle else values[name]
        # The energy counter climbs by a fraction of a kWh per tick — 2 decimals
        # would turn its steady rise into occasional 0.01 jumps.
        body[wire_key] = round(value, 4 if profile.get("counters") and name == "energy" else 2)
    body["seq"] = int(time.time())
    return json.dumps(body)


def advance_counters(values: dict[str, float], interval: float) -> None:
    """Moves a meter's cumulative registers on by one tick. Kept apart from
    DriftingValues because a random walk would let them fall, which no real
    meter does."""
    # Energy consumed this tick in kWh: V x I x PF x seconds / 3.6e6.
    power_watts = max(0.0, values.get("voltage", 0.0) * values.get("current", 0.0) * values.get("powerFactor", 0.0))
    values["energy"] = values.get("energy", 0.0) + power_watts * interval / 3_600_000


def publish_mqtt(device_id: str, profile: dict, fields: dict[str, float], host: str, port: int, password: str, interval: float | None, heartbeat: float = 30) -> None:
    try:
        import paho.mqtt.client as mqtt
    except ImportError:
        sys.exit(
            "paho-mqtt isn't installed for this Python interpreter.\n"
            "Safe local install (won't touch system Python):\n"
            "  python3 -m venv .venv && .venv/bin/pip install paho-mqtt\n"
            "  .venv/bin/python3 " + " ".join(sys.argv)
        )

    # The broker's ACL (infrastructure/mosquitto/acl) restricts each
    # tenant-slug username to its own iot/{tenant}/# namespace — using
    # anything but this device's own tenant here would be rejected by the
    # broker, not just semantically wrong.
    username = profile["tenant"]
    topic = f"iot/{profile['tenant']}/{profile['site']}/{device_id}/telemetry"
    merged = {**profile.get("defaults", DEFAULT_MQTT_FIELDS), **fields}

    print(f"[{device_id}] connecting to MQTT {host}:{port} ...")
    client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=f"sample-{device_id}-{int(time.time())}")
    client.username_pw_set(username, password)

    # paho reports a refused login only through this callback — without it
    # a wrong password leaves the script "streaming" into the void.
    connack = {}
    client.on_connect = lambda _c, _u, _f, reason_code, _p: connack.setdefault("rc", reason_code)
    client.connect(host, port, keepalive=10)
    client.loop_start()
    deadline = time.time() + 5
    while "rc" not in connack and time.time() < deadline:
        time.sleep(0.1)
    rc = connack.get("rc")
    if rc is None or rc.is_failure:
        client.loop_stop()
        sys.exit(
            f"[{device_id}] MQTT connection refused ({rc or 'no answer from broker'}) for user '{username}'.\n"
            "Pass the tenant's MQTT password: --mqtt-password <password> (see MQTT Credentials in the dashboard)."
        )

    try:
        if interval is None:
            payload = build_mqtt_payload(profile, merged)
            client.publish(topic, payload, qos=1).wait_for_publish(timeout=5)
            print(f"[{device_id}] published to {topic}: {payload}")
            return

        drift = DriftingValues(merged, device_id)
        # Heartbeats run on their own clock, like a real device's: BMS only
        # notes that one arrived (the payload is ignored), which keeps the
        # device Online between readings and tells "alive but not reporting"
        # (No Telemetry alert) apart from "dead" (Offline).
        heartbeat_topic = topic.rsplit("/", 1)[0] + "/heartbeat"
        hb_note = f", heartbeat every {heartbeat:g}s" if heartbeat > 0 else ", no heartbeat"
        print(f"[{device_id}] streaming every {interval}s to {topic}{hb_note}, drifting with a random anomaly every 15-30min — Ctrl+C to stop")
        next_telemetry = time.time()
        next_heartbeat = time.time() if heartbeat > 0 else float("inf")
        while True:
            now = time.time()
            if now >= next_telemetry:
                values = drift.tick()
                if profile.get("counters"):
                    advance_counters(values, interval)
                payload = build_mqtt_payload(profile, values)
                client.publish(topic, payload, qos=1)
                print(f"[{device_id}] sent at {time.strftime('%H:%M:%S')}: {payload}")
                next_telemetry += interval
            if now >= next_heartbeat:
                client.publish(heartbeat_topic, "{}", qos=1)
                print(f"[{device_id}] heartbeat at {time.strftime('%H:%M:%S')}")
                next_heartbeat += heartbeat
            time.sleep(max(0.0, min(next_telemetry, next_heartbeat) - time.time()))
    except KeyboardInterrupt:
        print(f"\n[{device_id}] stopped.")
    finally:
        client.loop_stop()
        client.disconnect()


def print_device_list() -> None:
    print(f"{'DEVICE':<12} FIELDS")
    for device_id, profile in DEVICE_PROFILES.items():
        print(f"{device_id:<12} {', '.join(profile['json_fields'])}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Publish sample telemetry to a device's MQTT topic, once or continuously.")
    parser.add_argument("--list", action="store_true", help="List devices and the field names each one accepts")
    parser.add_argument("--device-id", help="e.g. device-001")
    parser.add_argument("--field", action="append", default=[], metavar="key=value", help="Repeatable, e.g. --field temperature=55")
    parser.add_argument("--interval", type=float, default=None, metavar="SECONDS", help="Stream continuously (drifting, with a random anomaly every 15-30min) instead of publishing once, until Ctrl+C")
    parser.add_argument("--heartbeat", type=float, default=30, metavar="SECONDS", help="MQTT streaming only: send a heartbeat every N seconds alongside the telemetry (default 30; 0 turns it off)")
    parser.add_argument("--mqtt-host", default="localhost")
    parser.add_argument("--mqtt-port", type=int, default=1883)
    parser.add_argument("--mqtt-password", default="devicepassword", help="Username is derived automatically from the device's tenant (see infrastructure/mosquitto/acl)")
    args = parser.parse_args()

    if args.list or not args.device_id:
        print_device_list()
        if not args.device_id:
            print("\nPass --device-id to publish, e.g.: --device-id device-001 --field temperature=55")
        return

    profile = DEVICE_PROFILES.get(args.device_id)
    if profile is None:
        sys.exit(f"Unknown device '{args.device_id}'. Run --list to see available devices.")
    fields = parse_fields(args.field)
    publish_mqtt(args.device_id, profile, fields, args.mqtt_host, args.mqtt_port, args.mqtt_password, args.interval, args.heartbeat)


if __name__ == "__main__":
    main()
