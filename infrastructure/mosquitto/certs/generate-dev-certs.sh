#!/usr/bin/env bash
# Generates a throwaway CA + server certificate for the MQTTS (TLS)
# listener on port 8883 — for LOCAL DEVELOPMENT AND TESTING ONLY.
#
# These are self-signed: real devices/clients would need to be told to
# trust ca.crt explicitly (no real CA vouches for it), which is fine for
# testing the TLS listener itself but is NOT what a real deployment should
# ship. Production needs certificates from a real CA (Let's Encrypt via
# your actual domain, or your org's internal CA) — swap server.crt/
# server.key for those and this script is no longer needed.
#
# Usage: ./generate-dev-certs.sh
# Output (git-ignored, never commit private keys): ca.crt, server.crt, server.key

set -euo pipefail
cd "$(dirname "$0")"

echo "Generating dev-only CA..."
openssl genrsa -out ca.key 2048
openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 \
  -out ca.crt -subj "/CN=BMS Dev CA (not for production)"

echo "Generating dev-only server certificate for the Mosquitto listener..."
openssl genrsa -out server.key 2048
openssl req -new -key server.key -out server.csr -subj "/CN=localhost"
openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -out server.crt -days 825 -sha256 \
  -extfile <(printf "subjectAltName=DNS:localhost,DNS:mosquitto,IP:127.0.0.1")

rm -f server.csr ca.srl
chmod 600 ca.key server.key
echo "Done: ca.crt, ca.key, server.crt, server.key written to $(pwd)"
echo "These are git-ignored — regenerate with this script on any machine that needs them."
