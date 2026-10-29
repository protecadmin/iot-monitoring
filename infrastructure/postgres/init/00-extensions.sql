-- Runs once, automatically, the first time the postgres container
-- initializes its data directory (official postgres image behavior:
-- every *.sql/*.sh file under /docker-entrypoint-initdb.d is executed
-- in filename order).
--
-- Domain tables themselves are NOT created here — EF Core migrations
-- (introduced in Phase 3) own the schema. This file is only for
-- database-level setup that migrations don't manage.

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
