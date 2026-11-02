#!/usr/bin/env bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y ca-certificates curl git openssl jq python3
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
. /etc/os-release
printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu %s stable\n' "$(dpkg --print-architecture)" "$VERSION_CODENAME" > /etc/apt/sources.list.d/docker.list
apt-get update -qq
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker
if ! swapon --show | grep -q . && [ ! -e /swapfile-protec ]; then
  fallocate -l 4G /swapfile-protec
  chmod 600 /swapfile-protec
  mkswap /swapfile-protec
  swapon /swapfile-protec
  printf '/swapfile-protec none swap sw 0 0\n' >> /etc/fstab
fi
install -d -o ubuntu -g ubuntu -m 0750 /opt/protec /opt/protec/releases /opt/protec/shared /opt/protec/backups
docker --version
docker compose version
free -h
df -h /
