# InfluxDB

No config file is needed here for Phase 1 — the official `influxdb:2` image
is initialized entirely through environment variables (`DOCKER_INFLUXDB_INIT_*`)
set in `docker-compose.yml` / `.env`, which create the initial org, bucket,
admin user, and admin token on first startup.

The `data/` and `config/` subdirectories are created at runtime as Docker
volumes and are git-ignored.

See [docs/database/influxdb.md](../../docs/database/influxdb.md) for what
InfluxDB is responsible for in this platform.
