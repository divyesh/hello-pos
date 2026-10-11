# Quickstart Results: Tenancy and API Infrastructure

**Run**: 2026-10-11, Windows 11, Podman 5.4, PostgreSQL 18.6
**Guide**: [quickstart.md](../quickstart.md)

## User Story 1: migrations (automated)

`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`: 20/20, including:

| Test | Result |
|------|--------|
| Empty database migrated, second run applies nothing, < 60 s (SC-005) | ✅ |
| App login reads/writes rows; `CREATE`/`ALTER`/`DROP` refused, also in `public` (SC-004) | ✅ |
| Failing migration leaves no partial changes and is named (FR-003) | ✅ |
| Database recording an unknown migration stops the run (edge case) | ✅ |
| No pending EF model changes | ✅ |

## User Story 1: local environment (existing feature 002 database, no reset)

| Check | Result |
|-------|--------|
| `scripts/env.ps1 up --api` bootstraps roles and applies `InitialTenancy` | ✅ then API `available` as the app login |
| Second `up` | ✅ "Database schema is up to date", 0 applied |
| Roles | ✅ `hellopos_app`, `hellopos_migrator`: no superuser, no BYPASSRLS; `pos_app` NOLOGIN; app is member of `pos_app` with INHERIT |
| Ownership | ✅ schema `pos` and `businesses` owned by `hellopos_migrator`; RLS enabled |
| App login without a business | ✅ sees 0 businesses |
| App login `create table pos.x` | ✅ denied |
| App login reads migration history | ✅ denied |

### Issues found and fixed

1. EF Core *Relational* resolved to 10.0.4 (via the Npgsql provider) next to EF Core 10.0.12.
   Fixed: central transitive pinning to 10.0.12.
2. Npgsql attempted Kerberos (GSS) encryption on every connection, logging a missing
   `libgssapi_krb5` library in the chiseled image. Fixed: GSS encryption disabled in one shared
   connection-string normalizer used by the API, the probe and the migrator.

### Note

Compose warns that `COMPOSE_BAKE=false` is deprecated. Without it, compose warns that Bake needs
buildkit (which Podman lacks). Harmless either way; revisit when the compose provider changes.

## User Stories 2–4 (automated, `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`: 53/53, stable over 3 runs)

| Area | Result |
|------|--------|
| No identity → 401, no data touched; business ids in body, query, headers ignored (FR-006–FR-008) | ✅ |
| Stamping; business change rejected; save without business throws (FR-010) | ✅ |
| Reference to another business's record → 404, nothing stored; foreign read identical to unknown id (FR-012, FR-013) | ✅ |
| Raw SQL as app login: only session business visible/changeable; no session business → nothing (SC-007) | ✅ |
| 200 interleaved requests over a 2-connection pool never mix; pooled connection reused without business sees nothing (SC-008) | ✅ |
| `IgnoreQueryFilters` only in `AcrossAllBusinesses()` (FR-009) | ✅ |
| Isolation harness on the sample slice: list, read, update, delete, reference (SC-002, SC-006) | ✅ |
| Harness catches leaks that bypass both layers, naming operation and record (SC-003) | ✅ |
| Convention check passes on the real schema; each of 8 rules fires on a deliberately broken table (SC-001) | ✅ |
| Ids are UUIDv7 and time-ordered (FR-018) | ✅ |
| Sabotage: no session interceptor → 10/12 tenancy tests fail; no stamping → 11/12 fail | ✅ tests are load-bearing |

### Issue found and fixed

As the suite grew, idle pooled connections from finished test databases exhausted PostgreSQL's
default 100 connections. The test container now allows 500 and each test app clears its pools on
dispose.
