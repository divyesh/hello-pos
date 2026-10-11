# Implementation Plan: Tenancy and API Infrastructure

**Branch**: `003-tenancy-infrastructure` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-tenancy-infrastructure/spec.md`

## Summary

Introduce EF Core 10 on PostgreSQL 18 with a tenant model enforced in five layers: the current
business comes only from the authenticated identity (always absent until login, #7; tests inject
it); EF named query filters scope every read; a save interceptor stamps and protects
`business_id`; composite `(business_id, id)` keys make the database reject cross-business links;
and row-level security, driven by a per-connection setting that is reset on close and by pool
`DISCARD ALL`, scopes every row even for raw SQL. Migrations run through a `migrate` mode of the
API image as a dedicated migrator login after an idempotent role bootstrap; the API's login only
has row rights via group role `pos_app`. A Testcontainers-based isolation harness, test-only
sample tables (categories → products) and a catalog-based convention check prove it all.
Decisions: [research.md](research.md); developer contract:
[contracts/tenancy-developer-guide.md](contracts/tenancy-developer-guide.md).

## Technical Context

**Language/Version**: C# on .NET 10 (feature 001 toolchain, SDK container)

**Primary Dependencies**: EF Core 10.0.12, Npgsql EF provider 10.0.3, EFCore.NamingConventions
10.0.1, EF Core Design 10.0.12 + `dotnet-ef` 10.0.12 (local tool); existing Npgsql, Testcontainers

**Storage**: PostgreSQL 18.6, schema `pos`, RLS

**Testing**: xUnit v3 + Testcontainers (real PostgreSQL, app login), WebApplicationFactory

**Target Platform**: API container (local compose, later DigitalOcean)

**Project Type**: backend infrastructure (no new endpoints for users)

**Performance Goals**: empty database migrated in under 1 minute (SC-005); RLS adds no measurable
latency at this scale (one indexed equality per table)

**Constraints**: app login never owns tables; business never from request data; no
`IgnoreQueryFilters` outside the named opt-out; sample tables never in production migrations

**Scale/Scope**: 1 production table, 2 test-only tables, ~8 infrastructure types, 1 migration,
~10 test classes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | Money columns `numeric(19,4)`, no float types, enforced by the convention check. | Pass |
| II. Never Lose or Duplicate | Client-generatable UUIDv7 ids prepare idempotent sale creation. | Pass |
| III. Tenant Isolation | This feature: identity-only business, global filters, stamping, composite FKs, RLS, referenced-id checks, isolation harness. Bypass only via `AcrossAllBusinesses()`, source-checked. | Pass |
| IV. Append-Only Sales | No sales yet. | N/A |
| V. Contract-First | No API contract change. | Pass |
| VI. Test-First | Every rule has a failing test first, against real PostgreSQL as the app login. | Pass |
| VII. Vertical Slices | Entities and configurations live with slices; tenancy is shared infrastructure, as allowed. | Pass |
| VIII. Secure by Default | Migrator/app split; group-role grants; RLS; admin and migrator credentials never in the API process. | Pass |
| IX. No Silent Failures | Migrate mode exits non-zero naming the failed migration; FK/RLS violations mapped to explicit 404/401 problem details and logged. | Pass |
| X. Simplicity | One context, migrations via the existing image, no platform bypass role until needed. Complexity justified below. | Pass |

Result: **pass**. Re-checked after Phase 1 design: still pass.

## Project Structure

### Documentation (this feature)

```text
specs/003-tenancy-infrastructure/
├── plan.md, research.md (R1–R13), data-model.md, quickstart.md
├── contracts/tenancy-developer-guide.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
docs/tenancy.md                                   # developer guide (FR-021)
.config/dotnet-tools.json                         # + dotnet-ef
.env.example                                      # + migrator login
compose.yaml                                      # + migrate service (profile "tools")
scripts/env.ps1, env.sh                           # up runs migrate after the database is healthy

backend/src/HelloPos.Api/
├── Program.cs                                    # `migrate` mode; persistence registration
├── Infrastructure/
│   ├── Tenancy/
│   │   ├── ICurrentBusiness.cs                   # + ClaimsCurrentBusiness (null until #7)
│   │   ├── IBusinessOwned.cs
│   │   ├── RequireCurrentBusinessFilter.cs       # .RequireCurrentBusiness() → 401
│   │   └── NoCurrentBusinessException.cs
│   └── Persistence/
│       ├── HelloPosDbContext.cs                  # named "BusinessScope" filters, conventions
│       ├── BusinessOwnedConfiguration.cs         # ConfigureBusinessOwned()
│       ├── QueryableExtensions.cs                # AcrossAllBusinesses()
│       ├── BusinessStampingInterceptor.cs        # SaveChanges: stamp/protect business_id
│       ├── BusinessSessionInterceptor.cs         # set_config on open, RESET on close
│       ├── RowSecurityMigrationExtensions.cs     # EnableBusinessRowSecurity(table)
│       ├── PersistenceErrors.cs                  # 23503/42501 → 404, NoCurrentBusiness → 401
│       ├── Migrator.cs                           # bootstrap (admin) + MigrateAsync (migrator)
│       ├── DesignTimeDbContextFactory.cs
│       └── Migrations/                           # InitialTenancy: schema, function, businesses, RLS
└── Features/Businesses/Business.cs + BusinessConfiguration.cs

backend/tests/HelloPos.Api.Tests/
├── Infrastructure/Database/PostgresFixture.cs    # + bootstrap/migrate, app-login connection
├── Tenancy/
│   ├── Samples/ (SampleCategory, SampleProduct, SampleDbContext, sample endpoints)
│   ├── IsolationScenario.cs, IsolationTests.cs   # the harness
│   ├── SampleIsolationTests.cs                   # harness on sample slice
│   ├── HarnessDetectsLeaksTests.cs               # negative tests (SC-003)
│   ├── RowLevelSecurityTests.cs                  # raw SQL as app login (SC-007)
│   ├── SessionResetTests.cs                      # concurrency and pool reuse (SC-008)
│   ├── StampingAndReferencesTests.cs             # FR-010, FR-012, FR-013
│   └── CurrentBusinessTests.cs                   # FR-006–FR-008, 401
├── Persistence/
│   ├── MigrationTests.cs                         # apply, re-apply, failure rollback, rights (SC-004/005)
│   ├── ConventionTests.cs                        # catalog check + broken-table negatives (SC-001)
│   ├── ModelSnapshotTests.cs                     # no pending model changes
│   └── FilterBypassSourceTests.cs                # IgnoreQueryFilters only in AcrossAllBusinesses
```

**Structure Decision**: persistence and tenancy are shared infrastructure inside the API project
(Constitution VII allows shared infrastructure); the `Business` entity sits in its own feature
folder for #5 to extend. All test-only code stays in the test project.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|------|-----------|--------------------------------------|
| Two database-side layers (composite FKs + RLS) on top of EF filters | Clarification Q1: database must hold even if application scoping fails | EF filters alone were the old system's model of "remember every check" |
| `migrate` mode inside the API executable | One artifact for API and migrations, no SDK at deploy | Separate migrator project/image adds a build target for no gain |
| Group role `pos_app` plus bootstrap step | Migrations must not hard-code environment login names; migrator must not create roles | Granting by login name ties schema history to one environment |
