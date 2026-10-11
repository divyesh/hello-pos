# Research: Tenancy and API Infrastructure

Versions checked 2026-10-11 and pinned: `Microsoft.EntityFrameworkCore` 10.0.12,
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions` 10.0.1,
`Microsoft.EntityFrameworkCore.Design` 10.0.12, tool `dotnet-ef` 10.0.12. PostgreSQL 18.6.

## R1. Data access

- **Decision**: EF Core 10 with the Npgsql provider. One `HelloPosDbContext` in
  `backend/src/HelloPos.Api/Infrastructure/Persistence/`. Slices own their entity types and EF
  configurations (`IEntityTypeConfiguration<T>` next to the slice); the context discovers them by
  assembly scan. Migrations live in `Infrastructure/Persistence/Migrations/`.
- **Rationale**: the constitution's stack; vertical slices keep entities with their features while
  the context stays the single unit of work.
- **Alternatives**: Dapper/raw SQL (no global query filters, every query must remember the scope:
  the old system's failure mode); a separate persistence library (adds a project with no current
  benefit, Principle X).

## R2. Database roles

| Role | Login | Purpose | Key rights |
|------|-------|---------|-----------|
| admin (`POSTGRES_USER` locally; `doadmin` on DigitalOcean) | yes | bootstrap only | creates roles, grants |
| `hellopos_migrator` | yes | runs migrations; owns schema `pos` and all its objects | `CONNECT, CREATE` on the database |
| `pos_app` | **no** (group role) | what migrations grant data rights to | `USAGE` on `pos`; `SELECT, INSERT, UPDATE, DELETE` on tables (default privileges) |
| `hellopos_app` | yes | the API | member of `pos_app` (`INHERIT`), `NOBYPASSRLS`, not an owner |

- **Decision**: migrations grant to the fixed group role `pos_app`, never to an environment-specific
  login name. A **bootstrap** step (idempotent SQL run as admin) creates `hellopos_migrator`,
  `pos_app`, makes the app login a member and sets `INHERIT` (feature 002 created it `NOINHERIT`),
  and grants the migrator `CREATE` on the database. Migrations then create schema `pos` (owned by
  the migrator) and `ALTER DEFAULT PRIVILEGES FOR ROLE hellopos_migrator IN SCHEMA pos GRANT
  SELECT, INSERT, UPDATE, DELETE ON TABLES TO pos_app`.
- **Rationale**: FR-002. The app login never owns tables, so row-level security always applies to
  it (owners bypass RLS unless forced). Login names and passwords stay per-environment.
- **Alternatives**: granting to `hellopos_app` by name inside migrations (couples schema history to
  one environment's login names); letting the migrator create roles (needs `CREATEROLE` forever).

## R3. Migration runner: `migrate` mode of the API image

- **Decision**: the API executable has a second entry mode, `dotnet HelloPos.Api.dll migrate`, which
  runs the bootstrap (admin connection) and then `Database.MigrateAsync()` (migrator connection),
  logs each applied migration, and exits non-zero on failure without starting the web host. It
  reads `ConnectionStrings__Admin` and `ConnectionStrings__Migrator`; the normal API process never
  receives either. Locally, `scripts/env up` runs it as a one-off container
  (`compose run --rm migrate`) after the database is healthy and before starting `api`. Tests call
  the same `Migrator` class in-process. Staging and production (#4) run the same image in this mode
  as a pre-deploy job.
- **Rationale**: FR-003/FR-004 with one artifact; no EF tooling or SDK at deploy time; the API's
  own process can never migrate (FR-004).
- **Alternatives**: EF migration bundles (a second binary and image target); applying migrations
  at API startup (needs schema rights in the API; races with several instances); psql scripts
  (lose EF's model diffing).
- Each migration runs in a transaction (PostgreSQL DDL is transactional), so a failed migration
  leaves no partial changes (FR-003). Editing an applied migration is detected by comparing the
  model snapshot in a test (R12) and by review.

## R4. Current business

- **Decision**: `ICurrentBusiness` (scoped) exposes `Guid? BusinessId`. The production
  implementation reads claim `business_id` from the authenticated user; until login exists (#7)
  there is no authentication, so it is always `null` (FR-007). Tests replace it with
  `FixedCurrentBusiness` through `WebApplicationFactory.ConfigureTestServices`; that type lives only
  in the test assembly. Nothing reads the business from bodies, routes, query strings or headers
  (FR-006). Background work creates its own scope with `null` unless started for a business (FR-008).
- **Endpoint guard**: endpoints that touch business-owned data use `.RequireCurrentBusiness()`, an
  endpoint filter returning `401` problem details when there is no current business (FR-008).

## R5. Application-level scope (EF Core)

- **Decision**: business-owned entities implement `IBusinessOwned { Guid BusinessId }`. The context
  applies, for each such entity type, a **named** query filter `"BusinessScope"`:
  `e => e.BusinessId == currentBusinessId` (EF Core 10 named filters), evaluated per context
  instance, so with no current business the filter matches nothing. The only way to skip it is the
  extension `AcrossAllBusinesses()` (wraps `IgnoreQueryFilters(["BusinessScope"])`); a source test
  fails if `IgnoreQueryFilters` appears anywhere else (FR-009).
- **Writes**: a `SaveChanges` interceptor stamps `BusinessId` on added business-owned entities
  (overwriting any value, FR-010), rejects changes to `BusinessId` on modified ones, and throws
  `NoCurrentBusinessException` (mapped to `401`) when there is none.
- **Referenced ids** (FR-012): guaranteed by composite foreign keys (R7); the database's foreign-key
  violation (`23503`) and RLS violation (`42501`) are mapped to `404` problem details "not found",
  identical to an unknown id (FR-013). Slices may also call `EnsureExistsAsync<T>(id)` before a write
  for a clearer field-level error.

## R6. Database-level scope: row-level security

- **Decision**: schema `pos` has a function `pos.current_business_id()`:
  `NULLIF(current_setting('pos.business_id', true), '')::uuid` (`STABLE`). Every business-owned table
  gets `ENABLE ROW LEVEL SECURITY` and policy `business_scope`:
  `USING (business_id = pos.current_business_id()) WITH CHECK (business_id = pos.current_business_id())`.
  `pos.businesses` gets the same with `id`. Not `FORCE`d: the owner (migrator) bypasses it for data
  migrations; the app login never owns tables. A migration helper `EnableBusinessRowSecurity(table)`
  emits this SQL so every table gets it identically; the convention check (R11) verifies it.
- **Setting the business on the session**: an EF `DbConnectionInterceptor` runs
  `SELECT set_config('pos.business_id', @id, false)` when a connection opens (empty string when there
  is no current business) and `RESET pos.business_id` when it closes. Npgsql's pool also runs
  `DISCARD ALL` when a connection is returned (its default), so state cannot survive into another
  request even if the closing reset failed (FR-014, SC-008).
- **Unset means nothing**: with no setting, `current_business_id()` is `NULL` and every policy
  comparison is false: no rows visible, no rows writable (spec US2 scenario 5).
- **Platform bypass**: not provisioned in this feature (no platform work exists yet); a dedicated
  role with `BYPASSRLS`, used only by named platform code, is added when the first platform report
  needs it.
- **Alternatives**: transaction-scoped `set_config(..., true)` (needs every operation inside an
  explicit transaction; EF reads are not); `FORCE ROW LEVEL SECURITY` (would block the migrator's
  data fixes).

## R7. Keys, links and ids

- **Decision**: business-owned tables have primary key `(business_id, id)`; every foreign key between
  business-owned tables is `(business_id, <x>_id) → (business_id, id)`; each also has a foreign key
  `business_id → pos.businesses(id)`. A shared EF helper `ConfigureBusinessOwned()` sets the key,
  the business link and the index so slices cannot forget them. Ids are `uuid`, UUID version 7
  (time-ordered): devices generate them offline; the server uses `Guid.CreateVersion7()` when absent;
  columns default to PostgreSQL 18's `uuidv7()` for rows created in SQL (FR-018).
- **Rationale**: FR-011. Foreign-key checks are not subject to RLS, so the composite key is what
  rejects `(A, product of B)`, independent of any application code.

## R8. Naming, time and money conventions

- **Decision**: schema `pos`; snake_case tables (plural) and columns via `EFCore.NamingConventions`;
  timestamps are `timestamptz`, mapped from `DateTimeOffset`/UTC `DateTime` (Npgsql rejects non-UTC
  writes); money is `numeric(19,4)` (exact; four decimals leave room for per-gram unit prices, totals
  are rounded to the currency's minor unit by the pricing rules); no `real`/`double precision`
  anywhere (FR-019, Constitution I).

## R9. `businesses` table

- **Decision** (FR-005): `id uuid`, `name text`, `country_code char(2)` (`IN`, `US`, `CA`),
  `currency_code char(3)` (ISO 4217), `pricing_mode text` (`tax_inclusive` | `tax_exclusive`),
  `financial_year_start_month smallint` (1–12), `time_zone text` (IANA), `created_at`, `updated_at`
  (`timestamptz`). Check constraints enforce the enumerations and ranges. RLS by `id`.

## R10. Test databases and test-only sample tables

- **Decision**: a Testcontainers fixture starts PostgreSQL 18.6, runs the real bootstrap and
  migrations through `Migrator` (as in production), then creates two **test-only** sample tables
  (`sample_categories`, `sample_products`, shaped like the old categories → products, FR-017) from a
  `SampleDbContext` that extends the production model: the migrations model differ produces their
  DDL from the same `ConfigureBusinessOwned()` helper, and the same RLS helper SQL is applied. Tests
  then connect **as the app login** (never the superuser, which would bypass RLS).
- **Rationale**: proves the real helpers and policies end to end while production migrations never
  contain sample tables.

## R11. Convention check

- **Decision**: a test queries the migrated test database's catalog (`information_schema`,
  `pg_class.relrowsecurity`, `pg_policies`, `pg_constraint`) and fails, naming table/column and rule,
  when in schema `pos` (excluding `__ef_migrations_history`): a table other than `businesses` lacks a
  non-null `business_id uuid`; a primary key does not start with `business_id`; a foreign key between
  business-owned tables does not include `business_id`; RLS is not enabled or `business_scope` is
  missing; a column is `timestamp without time zone`, `real` or `double precision`; a `numeric`
  column has no precision/scale (FR-020). It runs over the sample tables too, and a negative test
  creates deliberately broken tables to prove each rule fires.

## R12. Isolation harness

- **Decision**: `IsolationScenario` (test assembly) seeds businesses A and B with sample data through
  the app login under each business, and offers `AsBusiness(A)` clients and assertions:
  `AssertCannotRead(op)`, `AssertCannotList(op)`, `AssertCannotModify(op)`, `AssertCannotReference(op)`.
  A slice's isolation test supplies only its operations (SC-006). Negative tests run the same
  assertions against a deliberately unscoped sample operation and expect failure (SC-003), and run
  raw SQL as the app login with the EF filter bypassed to prove RLS alone holds (SC-007).
  A concurrency test interleaves many requests for A and B over a small pool and checks no response
  ever contains the other business's data (SC-008).
- **Model snapshot test**: a test fails if the EF model has pending changes not captured in a
  migration (keeps migrations and code in step).

## R13. Local environment changes (feature 002 follow-up)

- `.env.example` gains `HELLOPOS_DB_MIGRATOR_USER`/`_PASSWORD`. A `migrate` service (API image,
  `profiles: ["tools"]`, command `migrate`, admin and migrator connection strings) is run by
  `scripts/env up` after the database is healthy. The `api` service is unchanged (app login only).
- Existing local databases need no reset: the bootstrap is idempotent and upgrades the feature 002
  app login (`INHERIT`, `pos_app` membership).
