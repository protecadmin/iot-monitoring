# PostgreSQL Responsibility

## What PostgreSQL owns

PostgreSQL is the **source of truth for application/domain data** — the
things that are relatively low-volume, relational, and transactional:
tenants, sites, devices, device types, users/roles, payload
mapping configuration, alert rules and alert records, commands, device
configuration, firmware and firmware deployments, audit log, and raw
message *metadata*. See [schema.md](schema.md) for the full entity list.

## What it does not own

High-volume time-series telemetry readings live in InfluxDB, not
PostgreSQL — see [influxdb.md](influxdb.md) and
[the split below](#why-split-across-two-databases-at-all). A
`Telemetry`-adjacent row may exist in PostgreSQL, but as domain-level
metadata (e.g. "this device's current/latest known reading" for dashboard
summary queries), not as the append-only stream of every reading.

## Why split across two databases at all

```
.NET Processing
      +--> PostgreSQL   (domain data: relational, transactional, moderate volume)
      +--> InfluxDB      (telemetry: time-series, high volume, append-only)
```

A relational database optimizes for exactly what domain data needs:
foreign keys, constraints, transactions, joins across a handful of
entities per query. It does not optimize for "millions of timestamped
readings, queried mostly by time range and downsampled/aggregated" — that
is InfluxDB's job. Forcing both into PostgreSQL would mean either a
telemetry table that dwarfs and slows down every other query, or premature
partitioning/tuning work that a time-series-native database already solves.
Forcing domain data into InfluxDB would mean losing relational integrity
(foreign keys, transactions) for data that genuinely needs it. Splitting
by actual access pattern, not "one database is simpler," is the point.

This is not data duplication for its own sake — most rows belong to
exactly one of the two databases, per the entity list in
[schema.md](schema.md).

## EF Core usage

- **Migrations** are the only way schema changes reach PostgreSQL —
  committed to source control, applied on startup/deploy, never
  hand-edited on a running database.
- **Constraints and indexes** are defined in EF Core configuration classes
  (`IEntityTypeConfiguration<T>`), not data annotations scattered across
  entities, keeping `Bms.Domain` free of EF Core references (see
  [system-architecture.md](../architecture/system-architecture.md)).
- **Transactions** wrap multi-entity writes that must succeed or fail
  together (e.g. creating a device + its initial configuration record).
- **Seed data** provides a usable local-dev tenant/site/device set without
  manual setup.

## Connection configuration

The connection string is never hardcoded — it's read from
`ConnectionStrings:Postgres`, supplied via environment variable
(`ConnectionStrings__Postgres`, ASP.NET Core's double-underscore
convention for nested config keys) in `.env`/Docker Compose, or via User
Secrets for local non-Docker development. See
[deployment/docker.md](../deployment/docker.md).
