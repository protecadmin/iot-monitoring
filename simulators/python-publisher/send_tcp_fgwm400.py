#!/usr/bin/env python3
"""
FG-WM400 4G tilt-sensor simulator — sends over raw TCP, exactly like the
real sensor, to a BMS connector port (see docs/operations/connectors.md).

The real sensor wakes every few minutes, opens a TCP connection, sends a
burst of ~10 text lines ~200 ms apart, and closes it. It sends no time.
This does the same, so the connector, the Connector Service and BMS see
what they will see in the field.

No packages needed (plain sockets).

Examples:
    # One burst, then exit
    python3 send_tcp_fgwm400.py --once

    # A burst every 30 s (the real sensor: --interval 300), until Ctrl+C
    python3 send_tcp_fgwm400.py --interval 30

    # Make the wall lean: +0.05 degrees X per burst — watch the tilt-change alert fire
    python3 send_tcp_fgwm400.py --interval 30 --lean 0.05

    # The sensor's binary (21-byte Modbus) mode, or a sensor not registered yet
    python3 send_tcp_fgwm400.py --once --format binary
    python3 send_tcp_fgwm400.py --once --sensor-id 100000009
"""

import argparse
import random
import socket
import sys
import time


def text_line(sensor_id: str, x: float, y: float, z: float, temp: float, battery: int, args) -> bytes:
    """ID, X/Y/Z tilt (deg), AX/AY/AZ (g), temperature (C), battery 0-9 [, phone]."""
    fields = [sensor_id, f"{x:+09.4f}", f"{y:+09.4f}", f"{z:+09.4f}",
              f"{random.uniform(-0.2, 0.2):+09.4f}", f"{random.uniform(-0.05, 0.05):+09.4f}", f"{random.uniform(0.97, 0.99):+09.4f}",
              f"{temp:+09.4f}", str(battery)]
    if args.phone:
        fields.append("1440033931560")
    if args.prefix:
        fields.insert(0, "BW-NB-WM400")
    return (",".join(fields) + "\n").encode()


def crc16_modbus(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc


def binary_frame(address: int, x: float, y: float, z: float, temp: float, battery: int) -> bytes:
    """21 bytes: address, 03, 10, eight big-endian registers, CRC-16/Modbus (little-endian)."""
    def reg(value: float, scale: float) -> bytes:
        return int(round(value * scale + 20000)).to_bytes(2, "big")
    body = bytes([address & 0xFF, 0x03, 0x10]) + reg(x, 100) + reg(y, 100) + reg(z, 100) \
        + reg(random.uniform(-0.2, 0.2), 10000) + reg(random.uniform(-0.05, 0.05), 10000) + reg(0.98, 10000) \
        + reg(temp, 100) + battery.to_bytes(2, "big")
    crc = crc16_modbus(body)
    return body + bytes([crc & 0xFF, crc >> 8])


def send_burst(args, state) -> None:
    """One wake-up: connect, send the burst, close — like the real sensor."""
    with socket.create_connection((args.host, args.port), timeout=10) as sock:
        for i in range(args.burst):
            # Tiny noise around the current position, as a real inclinometer reads.
            x = state["x"] + random.uniform(-0.002, 0.002)
            y = state["y"] + random.uniform(-0.002, 0.002)
            z = state["z"] + random.uniform(-0.002, 0.002)
            if args.format == "binary":
                payload = binary_frame(int(args.sensor_id) % 256, x, y, z, state["temp"], state["battery"])
                shown = payload.hex(" ").upper()
            else:
                payload = text_line(args.sensor_id, x, y, z, state["temp"], state["battery"], args)
                shown = payload.decode().strip()
            sock.sendall(payload)
            print(f"[{time.strftime('%H:%M:%S')}] sent {i + 1}/{args.burst}: {shown}")
            if i + 1 < args.burst:
                time.sleep(args.gap_ms / 1000)


def main() -> None:
    p = argparse.ArgumentParser(description="FG-WM400 tilt-sensor simulator over raw TCP")
    p.add_argument("--host", default="localhost", help="Connector Service host (default localhost)")
    p.add_argument("--port", type=int, default=12000, help="The connector's port (default 12000)")
    p.add_argument("--sensor-id", default="100000005", help="Sensor ID on the label (default 100000005)")
    p.add_argument("--format", choices=["text", "binary"], default="text", help="text (CSV with acceleration) or binary (21-byte Modbus)")
    p.add_argument("--prefix", action="store_true", help="Prefix mode: lines start with BW-NB-WM400,")
    p.add_argument("--phone", action="store_true", help="Append the phone/SIM field, as some sensors do")
    p.add_argument("--burst", type=int, default=10, help="Readings per wake-up (default 10)")
    p.add_argument("--gap-ms", type=int, default=200, help="Gap between readings in a burst (default 200 ms)")
    p.add_argument("--interval", type=float, default=300, help="Seconds between wake-ups (real sensor: 300)")
    p.add_argument("--once", action="store_true", help="Send one burst and exit")
    p.add_argument("--x", type=float, default=22.2149, help="Starting X tilt, degrees")
    p.add_argument("--y", type=float, default=0.5842, help="Starting Y tilt, degrees")
    p.add_argument("--z", type=float, default=88.8487, help="Starting Z tilt, degrees")
    p.add_argument("--lean", type=float, default=0.0, help="Degrees added to X every burst — simulates a structure starting to move")
    p.add_argument("--battery", type=int, default=7, help="Battery level 0-9 (default 7)")
    args = p.parse_args()

    state = {"x": args.x, "y": args.y, "z": args.z, "temp": 28.5, "battery": max(0, min(9, args.battery))}
    print(f"FG-WM400 simulator -> {args.host}:{args.port}, sensor {args.sensor_id}, {args.format}, "
          f"{args.burst} readings per burst" + ("" if args.once else f", every {args.interval:g} s (Ctrl+C to stop)"))
    try:
        while True:
            try:
                send_burst(args, state)
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
