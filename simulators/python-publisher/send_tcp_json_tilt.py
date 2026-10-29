#!/usr/bin/env python3
"""
JSON tilt-sensor simulator — a second inclinometer brand ("TiltPro") that
sends one JSON message per line over raw TCP to a BMS connector port.
Same kind of device as send_tcp_fgwm400.py (an Inclinometer), different
vendor and format, so both can be tested side by side.

Each message:
    {"id":"TP-2001","tilt":{"x":12.5,"y":-0.4,"z":89.1},"temp":27.3,"bat":8,"rssi":-70}

No packages needed (plain sockets). See docs/operations/connectors.md.

Examples:
    # One message, then exit
    python3 send_tcp_json_tilt.py --once

    # A message every 30 s, until Ctrl+C
    python3 send_tcp_json_tilt.py --interval 30

    # Make the wall lean: +0.05 degrees X per message — watch the tilt alerts fire
    python3 send_tcp_json_tilt.py --interval 30 --lean 0.05

    # Another sensor, or a burst of 5 readings per wake-up
    python3 send_tcp_json_tilt.py --once --sensor-id TP-2002
    python3 send_tcp_json_tilt.py --interval 30 --burst 5
"""

import argparse
import json
import random
import socket
import sys
import time


def message(args, state) -> dict:
    """One reading, with the tiny noise a real inclinometer has."""
    return {
        "id": args.sensor_id,
        "tilt": {
            "x": round(state["x"] + random.uniform(-0.002, 0.002), 4),
            "y": round(state["y"] + random.uniform(-0.002, 0.002), 4),
            "z": round(state["z"] + random.uniform(-0.002, 0.002), 4),
        },
        "temp": round(state["temp"], 1),
        "bat": state["battery"],
        "rssi": random.randint(-80, -60),
    }


def send(args, state) -> None:
    """One wake-up: connect, send the reading(s), close."""
    with socket.create_connection((args.host, args.port), timeout=10) as sock:
        for i in range(args.burst):
            msg = message(args, state)
            sock.sendall((json.dumps(msg) + "\n").encode())
            label = f"{i + 1}/{args.burst} " if args.burst > 1 else ""
            print(f"[{time.strftime('%H:%M:%S')}] sent {label}{json.dumps(msg)}")
            if i + 1 < args.burst:
                time.sleep(args.gap_ms / 1000)


def main() -> None:
    p = argparse.ArgumentParser(description="JSON tilt-sensor simulator over raw TCP")
    p.add_argument("--host", default="localhost", help="Connector Service host (default localhost)")
    p.add_argument("--port", type=int, default=12001, help="The connector's port (default 12001)")
    p.add_argument("--sensor-id", default="TP-2001", help="Sensor ID (default TP-2001)")
    p.add_argument("--burst", type=int, default=1, help="Readings per wake-up (default 1)")
    p.add_argument("--gap-ms", type=int, default=200, help="Gap between readings in a burst (default 200 ms)")
    p.add_argument("--interval", type=float, default=60, help="Seconds between wake-ups (default 60)")
    p.add_argument("--once", action="store_true", help="Send once and exit")
    p.add_argument("--x", type=float, default=12.5, help="Starting X tilt, degrees")
    p.add_argument("--y", type=float, default=-0.4, help="Starting Y tilt, degrees")
    p.add_argument("--z", type=float, default=89.1, help="Starting Z tilt, degrees")
    p.add_argument("--lean", type=float, default=0.0, help="Degrees added to X every wake-up — a structure starting to move")
    p.add_argument("--battery", type=int, default=8, help="Battery level 0-9 (default 8)")
    args = p.parse_args()

    state = {"x": args.x, "y": args.y, "z": args.z, "temp": 27.3, "battery": max(0, min(9, args.battery))}
    print(f"JSON tilt simulator -> {args.host}:{args.port}, sensor {args.sensor_id}"
          + ("" if args.once else f", every {args.interval:g} s (Ctrl+C to stop)"))
    try:
        while True:
            try:
                send(args, state)
            except OSError as e:
                print(f"[{time.strftime('%H:%M:%S')}] can't reach {args.host}:{args.port} — {e}. "
                      "Is the Connector Service running and the connector enabled?", file=sys.stderr)
                if args.once:
                    sys.exit(1)
            if args.once:
                break
            state["x"] += args.lean
            state["temp"] += random.uniform(-0.2, 0.2)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        print("\nstopped.")


if __name__ == "__main__":
    main()
