#!/usr/bin/env python3
"""
Compute MOSQUITTO_MAX_QUEUED_MESSAGES / _BYTES for a deployment.

The broker queue is what protects telemetry while ingestion is down. Past
its limit Mosquitto drops QoS 1 messages SILENTLY — there is no error, no
log line per message, and no way to recover them afterwards. The defaults
(100,000 / 100 MiB) are a starting point, not an answer: at 10,000 devices
reporting every 10 seconds they hold about 60 seconds of outage.

Note what this does NOT size: the 24 h session expiry is a separate limit,
and whichever of the two is smaller is the real bound.

Usage:
    python3 size-queue.py --devices 500 --interval 10 --outage 15m
    python3 size-queue.py --devices 10000 --interval 60 --outage 1h --payload 400

Payload size is the one input worth measuring rather than assuming: the
byte cap bites before the message cap whenever payloads are larger than
expected. Capture a real one with:

    mosquitto_sub -h localhost -t 'iot/#' -F '%l' -C 20
"""

import argparse
import sys

MIB = 1024 * 1024
GIB = MIB * 1024


def parse_duration(text: str) -> int:
    """'90' -> 90s, '15m' -> 900s, '2h' -> 7200s, '1d' -> 86400s."""
    units = {"s": 1, "m": 60, "h": 3600, "d": 86400}
    text = text.strip().lower()
    if text and text[-1] in units:
        value, unit = text[:-1], units[text[-1]]
    else:
        value, unit = text, 1
    try:
        seconds = float(value) * unit
    except ValueError:
        sys.exit(f"could not read a duration from {text!r} (try 90, 15m, 2h, 1d)")
    if seconds <= 0:
        sys.exit("outage must be greater than zero")
    return int(seconds)


def human_bytes(n: int) -> str:
    if n >= GIB:
        return f"{n / GIB:.2f} GiB"
    return f"{n / MIB:.1f} MiB" if n >= MIB else f"{n / 1024:.0f} KiB"


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--devices", type=int, required=True, help="Devices publishing to this broker")
    p.add_argument("--interval", type=float, required=True, metavar="SECONDS", help="Seconds between readings, per device")
    p.add_argument("--outage", required=True, help="Longest outage to cover, e.g. 90, 15m, 2h, 1d")
    p.add_argument("--payload", type=int, default=250, metavar="BYTES",
                   help="Average MQTT payload in bytes (default 250). Measure this rather than trusting the default.")
    p.add_argument("--topic", type=int, default=60, metavar="BYTES", help="Average topic length in bytes (default 60)")
    p.add_argument("--safety", type=float, default=1.5, metavar="FACTOR", help="Headroom multiplier (default 1.5)")
    args = p.parse_args()

    if args.interval <= 0:
        sys.exit("--interval must be greater than zero")

    outage = parse_duration(args.outage)
    per_second = args.devices / args.interval
    messages = per_second * outage * args.safety
    # ~30 B of MQTT framing per message on top of topic and payload.
    per_message = args.payload + args.topic + 30
    total_bytes = messages * per_message

    messages = int(messages) + 1
    total_bytes = int(total_bytes) + 1

    print()
    print(f"  {args.devices:,} devices, one reading every {args.interval:g}s")
    print(f"  = {per_second:,.1f} messages/second")
    print(f"  x {outage:,}s outage x {args.safety:g} safety factor")
    print()
    print(f"  MOSQUITTO_MAX_QUEUED_MESSAGES={messages}")
    print(f"  MOSQUITTO_MAX_QUEUED_BYTES={total_bytes}        # {human_bytes(total_bytes)}")
    print()
    print(f"  Queue disk needed on the mosquitto-data volume: ~{human_bytes(total_bytes)}")
    print(f"  (plus headroom — the persistence file is rewritten, not appended)")
    print()

    if outage > 86400:
        print("  ! The outage you asked to cover is longer than the 24 h MQTT session")
        print("    expiry. The session — and its whole queue — is discarded first, so")
        print("    this queue size buys nothing. Raise Mqtt__SessionExpirySeconds and")
        print("    MOSQUITTO_PERSISTENT_CLIENT_EXPIRATION to match, or cover less.")
        print()

    if messages > 1_000_000:
        print("  ! Over a million queued messages is a lot to hold in a broker whose")
        print("    queue lives in memory. Prefer fixing the cause: run two or more")
        print("    ingestion instances behind an MQTT 5 shared subscription so a")
        print("    deploy never stops consumption, and buffer on the device for the")
        print("    rest. See docs/mqtt/reliability.md.")
        print()


if __name__ == "__main__":
    main()
