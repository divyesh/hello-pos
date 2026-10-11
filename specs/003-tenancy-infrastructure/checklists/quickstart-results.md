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
