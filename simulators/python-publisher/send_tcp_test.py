#!/usr/bin/env python3
"""Send TCP test messages every 5 seconds. Stop with Ctrl+C; no extra packages needed."""

import argparse
import math
import socket
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="52.74.238.107")
    parser.add_argument("--port", type=int, default=12000)
    parser.add_argument("--message", default="TEST TCP DATA from Python")
    parser.add_argument("--interval", type=float, default=5, help="Seconds between messages (default: 5)")
    parser.add_argument("--once", action="store_true", help="Send one message, then exit")
    parser.add_argument("--check-only", action="store_true", help="Check TCP connectivity without sending data")
    args = parser.parse_args()

    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if not math.isfinite(args.interval) or args.interval <= 0:
        parser.error("--interval must be a finite number greater than zero")

    payload = (args.message + "\n").encode("utf-8")
    stage = "Connection"
    print(f"Connecting to {args.host}:{args.port} ...", flush=True)
    try:
        with socket.create_connection((args.host, args.port), timeout=10) as client:
            print("TCP connection successful.", flush=True)
            if args.check_only:
                print("Connection check complete; no data sent.")
                return 0
            stage = "Send"
            print("Check the EC2 receiver or tcp-test.log to confirm receipt. Stop with Ctrl+C.", flush=True)
            count = 0
            while True:
                client.sendall(payload)
                count += 1
                print(f"[{time.strftime('%H:%M:%S')}] Sent #{count}: {args.message} ({len(payload)} bytes)", flush=True)
                if args.once:
                    client.shutdown(socket.SHUT_WR)
                    break
                time.sleep(args.interval)
    except KeyboardInterrupt:
        print("\nStopped.", flush=True)
        return 0
    except OSError as error:
        print(f"{stage} failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
