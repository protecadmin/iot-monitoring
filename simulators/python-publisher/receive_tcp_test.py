#!/usr/bin/env python3
"""Log raw TCP bytes from multiple devices. Run on EC2; stop with Ctrl+C."""

import argparse
import logging
from logging.handlers import RotatingFileHandler
from pathlib import Path
import socketserver
import sys
import threading
import time


logger = logging.getLogger("tcp-receiver")


class Receiver(socketserver.BaseRequestHandler):
    def handle(self):
        peer = f"{self.client_address[0]}:{self.client_address[1]}"
        logger.info("CONNECTED %s", peer)
        try:
            while True:
                data = self.request.recv(4096)
                if not data:
                    break
                logger.info("FROM %s | BYTES=%d | TEXT=%r | HEX=%s",
                            peer, len(data), data.decode("utf-8", errors="replace"), data.hex(" "))
        except OSError as error:
            logger.warning("CONNECTION ERROR %s: %s", peer, error)
        finally:
            logger.info("DISCONNECTED %s", peer)


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True
    request_queue_size = 32

    def __init__(self, address, handler):
        self.slots = threading.BoundedSemaphore(32)
        super().__init__(address, handler)

    def process_request(self, request, address):
        if not self.slots.acquire(blocking=False):
            logger.warning("CONNECTION LIMIT: rejecting %s", address)
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.slots.release()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="0.0.0.0", help="Local bind address (default: all IPv4 interfaces)")
    parser.add_argument("--port", type=int, default=12000)
    parser.add_argument("--log", type=Path, default=Path.home() / "tcp-test.log")
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")

    try:
        file_handler = RotatingFileHandler(args.log, maxBytes=5 * 1024 * 1024,
                                           backupCount=3, encoding="utf-8")
    except OSError as error:
        parser.error(f"Cannot open log: {error}")
    formatter = logging.Formatter("%(asctime)s UTC | %(message)s", datefmt="%Y-%m-%d %H:%M:%S")
    formatter.converter = time.gmtime
    for handler in (logging.StreamHandler(sys.stdout), file_handler):
        handler.setFormatter(formatter)
        logger.addHandler(handler)
    logger.setLevel(logging.INFO)

    try:
        with Server((args.host, args.port), Receiver) as server:
            logger.info("LISTENING %s:%d | LOG=%s | Ctrl+C to stop", args.host, args.port, args.log.resolve())
            logger.info("Raw byte chunks only; no message framing, authentication, TLS or acknowledgements.")
            server.serve_forever()
    except KeyboardInterrupt:
        logger.info("STOPPED")
    except OSError as error:
        logger.error("Receiver failed: %s", error)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
