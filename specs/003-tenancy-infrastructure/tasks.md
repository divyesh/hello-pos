---
description: "Task list for feature 003: tenancy and API infrastructure"
---

# Tasks: Tenancy and API Infrastructure

**Input**: Design documents from `specs/003-tenancy-infrastructure/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/tenancy-developer-guide.md](contracts/tenancy-developer-guide.md),
[quickstart.md](quickstart.md)

**Tests**: Required (Constitution III and VI; spec SC-001–SC-008). Every rule gets a test that fails
first, against real PostgreSQL, connecting as the **app login** (never a superuser, which bypasses
row-level security).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no unfinished dependencies)
- **[Story]**: US1 migrations, US2 confinement, US3 isolation harness, US4 conventions

## Conventions for every task

- .NET only through `scripts/dotnet.ps1` (tests mount the engine socket automatically).
- Versions exactly as in research.md; `Directory.Packages.props` for packages.
- Names: schema `pos`, group role `pos_app`, setting `pos.business_id`, filter name `BusinessScope`,
  policy name `business_scope`.

---

## Phase 1: Setup

- [X] T001 Add `Microsoft.EntityFrameworkCore` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions` 10.0.1 and `Microsoft.EntityFrameworkCore.Design` 10.0.12 (`PrivateAssets=all`) to `Directory.Packages.props` and `backend/src/HelloPos.Api/HelloPos.Api.csproj`
- [X] T002 [P] Add `dotnet-ef` 10.0.12 to `.config/dotnet-tools.json`
- [X] T003 [P] Add `HELLOPOS_DB_MIGRATOR_USER=hellopos_migrator` and `HELLOPOS_DB_MIGRATOR_PASSWORD=change-me-migrator` to `.env.example` (and the local `.env` if present) with a comment (research R2, R13)

---

## Phase 2: Foundational (blocking)

- [X] T004 Create `backend/src/HelloPos.Api/Infrastructure/Tenancy/IBusinessOwned.cs` (`Guid BusinessId { get; }` with an internal setter path for stamping) and `ICurrentBusiness.cs` (`Guid? BusinessId`), plus `ClaimsCurrentBusiness` reading claim `business_id` from `IHttpContextAccessor` (returns `null` without an authenticated user) (research R4)
- [X] T005 Create `backend/src/HelloPos.Api/Infrastructure/Persistence/HelloPosDbContext.cs`: default schema `pos`, snake_case naming, applies all `IEntityTypeConfiguration<>` from the assembly, and for every entity type implementing `IBusinessOwned` adds named query filter `BusinessScope` `e => e.BusinessId == currentBusinessId` (captured per context instance from `ICurrentBusiness`); `__ef_migrations_history` in schema `pos` (research R1, R5)
- [X] T006 [P] Create `backend/src/HelloPos.Api/Infrastructure/Persistence/BusinessOwnedConfiguration.cs`: `ConfigureBusinessOwned<T>()` sets key `(BusinessId, Id)`, required FK `BusinessId → Business.Id` (restrict delete), index on `BusinessId`; `HasBusinessLink<TPrincipal>(fk)` helper for composite links (research R7)
- [X] T007 [P] Create `backend/src/HelloPos.Api/Features/Businesses/Business.cs` and `BusinessConfiguration.cs` per data-model.md (`pos.businesses`: every column, check constraints for `country_code` in (`IN`,`US`,`CA`), `currency_code` `^[A-Z]{3}$`, `pricing_mode` in (`tax_inclusive`,`tax_exclusive`), `financial_year_start_month` 1–12; `id` default `uuidv7()`)
- [X] T008 [P] Create `backend/src/HelloPos.Api/Infrastructure/Persistence/DesignTimeDbContextFactory.cs` (connection string from `HELLOPOS_DESIGN_CONNECTION` or a placeholder; `ICurrentBusiness` = none) so `dotnet ef` works inside the SDK container
- [X] T009 Extend `backend/tests/HelloPos.Api.Tests/Infrastructure/Database/PostgresFixture.cs`: keep the superuser connection for setup only; expose `AdminConnectionString`, and after migrations (US1) `AppConnectionString` / `MigratorConnectionString` with test passwords; add `ResetDataAsync()` that truncates `pos` tables as the migrator between tests (depends on T005)

**Checkpoint**: context compiles; fixture ready for migrations.

---

## Phase 3: User Story 1 - Evolve the database safely (P1) 🎯 MVP

**Goal**: bootstrap + migrations as the migrator; app login has row rights only.

**Independent Test**: quickstart.md §1 (migration tests) and §2 steps 1–4.

### Tests (write first, see them fail)

- [X] T010 [US1] Write `backend/tests/HelloPos.Api.Tests/Persistence/MigrationTests.cs`: from empty, `Migrator.RunAsync` applies all migrations and records them; a second run applies nothing and succeeds; the app login can `SELECT/INSERT/UPDATE/DELETE` on `pos.businesses` (with `pos.business_id` set) but `CREATE TABLE pos.x`, `ALTER TABLE pos.businesses ...` and `DROP TABLE` are refused (SC-004); a deliberately failing migration (test-only, raises an error after creating a table) leaves no table and reports its name (FR-003); a database recording an applied migration missing from the code makes the run stop with a clear error (edge case); run time from empty < 60 s (SC-005)
- [X] T011 [P] [US1] Write `backend/tests/HelloPos.Api.Tests/Persistence/ModelSnapshotTests.cs`: the EF model has no pending changes versus the latest migration snapshot

### Implementation

- [X] T012 [US1] Create `backend/src/HelloPos.Api/Infrastructure/Persistence/Migrator.cs`: `RunAsync(adminCs, migratorCs, appLogin, migratorLogin/password, ct)`: idempotent bootstrap as admin (create `pos_app` NOLOGIN, create/alter migrator login with password and `CONNECT, CREATE` on the database, `ALTER ROLE <app> INHERIT NOBYPASSRLS`, `GRANT pos_app TO <app>`), then `MigrateAsync` as the migrator with a context whose `ICurrentBusiness` is none; before migrating, stops with an error if the database records an applied migration that the code does not contain (history mismatch, spec edge case); logs each migration; throws with the failing migration name (research R2, R3)
- [X] T013 [US1] Create `backend/src/HelloPos.Api/Infrastructure/Persistence/RowSecurityMigrationExtensions.cs`: `EnableBusinessRowSecurity(table, column = "business_id")` (enable RLS + `business_scope` policy using `pos.current_business_id()`), and `CreateTenancyFoundation()` (schema-level function `pos.current_business_id()`, default privileges for `hellopos_migrator` → `pos_app`, `GRANT USAGE ON SCHEMA pos TO pos_app`) (research R6)
- [X] T014 [US1] Generate the initial migration `InitialTenancy` with `dotnet ef migrations add` through the wrapper into `backend/src/HelloPos.Api/Infrastructure/Persistence/Migrations/`, then edit it to call `CreateTenancyFoundation()` first and `EnableBusinessRowSecurity("businesses", "id")` after the table (depends on T005–T008, T013)
- [X] T015 [US1] Add `migrate` mode to `backend/src/HelloPos.Api/Program.cs`: when `args[0] == "migrate"`, build a minimal host with logging, read `ConnectionStrings:Admin`, `ConnectionStrings:Migrator`, app/migrator login names, run `Migrator`, exit 0/1; never start the web host in this mode
- [X] T016 [US1] Register persistence for the web host in `Program.cs`: `AddDbContext<HelloPosDbContext>` with `ConnectionStrings:HelloPos` (app login) when configured, `ICurrentBusiness` → `ClaimsCurrentBusiness`, `IHttpContextAccessor`; the API never receives admin/migrator strings (makes T010, T011 pass)
- [X] T017 [US1] Add service `migrate` to `compose.yaml` (`profiles: ["tools"]`, image `localhost/hellopos-api:dev`, build as `api`, `command: ["migrate"]` appended to the entrypoint, env `ConnectionStrings__Admin` (admin login), `ConnectionStrings__Migrator`, `Database__AppLogin`, `Database__MigratorLogin`, `Database__MigratorPassword`, depends on postgres healthy); update `scripts/env.ps1` and `scripts/env.sh` so `up` builds the image and runs `compose --profile tools run --rm migrate` after the core services are healthy and before `--api`, failing `up` if migration fails (research R13)
- [X] T018 [US1] Verify quickstart.md §2 steps 1–4 on the existing local database (no reset) and record in `specs/003-tenancy-infrastructure/checklists/quickstart-results.md`

**Checkpoint**: schema `pos` with `businesses` and RLS is migrated by the migrator everywhere.

---

## Phase 4: User Story 2 - Every request is confined to one business (P1)

**Goal**: current business from identity only; scoping, stamping, reference checks, RLS session.

**Independent Test**: tests below on the sample tables (created by US3's sample context; T019 creates
the minimal sample model they need).

### Tests (write first, see them fail)

- [X] T019 [US2] Create test-only sample model in `backend/tests/HelloPos.Api.Tests/Tenancy/Samples/`: `SampleCategory`, `SampleProduct` (link `(business_id, category_id)`), `SampleDbContext : HelloPosDbContext` adding them via `ConfigureBusinessOwned()`; fixture step that creates their tables from the model differ against the production model and applies `EnableBusinessRowSecurity` SQL for each; sample endpoints (list, get, create, update, delete categories and products) registered only in tests via `WebApplicationFactory` with `.RequireCurrentBusiness()` (research R10)
- [X] T020 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Tenancy/CurrentBusinessTests.cs`: without a current business every sample endpoint returns `401` and touches no data; `ClaimsCurrentBusiness` ignores `business_id` in body, route, query string and headers (only the claim counts); concurrent requests for A and B each see their own business (FR-006–FR-008)
- [X] T021 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Tenancy/StampingAndReferencesTests.cs`: acting as A, a created record belongs to A even when the body says B; changing `BusinessId` on update is rejected; creating a product that refers to B's category fails `404` and stores nothing; getting B's record by id returns a response identical to an unknown id (FR-010, FR-012, FR-013)
- [X] T022 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Tenancy/RowLevelSecurityTests.cs`: as the app login with `pos.business_id` = A, raw SQL `SELECT` returns only A's rows, `UPDATE`/`DELETE` of B's rows affect 0 rows, `INSERT` of a B row fails with RLS violation; with no setting, 0 rows are visible and inserts fail; through EF with `AcrossAllBusinesses()` the database still returns only A's rows (SC-007)
- [X] T023 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Tenancy/SessionResetTests.cs`: with `MaxPoolSize=2`, 200 interleaved requests for A and B never return the other's rows; a connection that served A then opened with no business sees 0 rows (SC-008)
- [X] T024 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Persistence/FilterBypassSourceTests.cs`: no `.cs` file under `backend/src` calls `IgnoreQueryFilters` except `QueryableExtensions.cs` (FR-009)

### Implementation

- [X] T025 [US2] Create `Infrastructure/Persistence/BusinessSessionInterceptor.cs` (`DbConnectionInterceptor`: on opened, `SELECT set_config('pos.business_id', @id, false)` with `''` when none; on closing, `RESET pos.business_id`); register it on the context (makes T022/T023 pass)
- [X] T026 [US2] Create `Infrastructure/Persistence/BusinessStampingInterceptor.cs` (`SaveChangesInterceptor`: stamp added `IBusinessOwned`, reject modified `BusinessId`, throw `NoCurrentBusinessException` without a business) and `Infrastructure/Tenancy/NoCurrentBusinessException.cs`
- [X] T027 [US2] Create `Infrastructure/Tenancy/RequireCurrentBusinessFilter.cs` (`.RequireCurrentBusiness()` endpoint filter → `401` problem details) and `Infrastructure/Persistence/PersistenceErrors.cs` (exception handler mapping `NoCurrentBusinessException` → 401, `PostgresException` `23503`/`42501` → 404 "not found", logged at Warning with trace id); register in `Program.cs` (makes T020/T021 pass)
- [X] T028 [US2] Create `Infrastructure/Persistence/QueryableExtensions.cs` with `AcrossAllBusinesses()` (the only `IgnoreQueryFilters(["BusinessScope"])` call, XML doc: requires review approval) (makes T024 pass)

**Checkpoint**: confinement proven at API and database level.

---

## Phase 5: User Story 3 - Prove isolation for every slice (P2)

**Goal**: reusable harness; proven to catch leaks.

- [X] T029 [US3] Write `backend/tests/HelloPos.Api.Tests/Tenancy/IsolationScenario.cs` and `IsolationTests.cs`: scenario seeds businesses A and B with sample categories/products (through the app login, each under its own business); base class offers `AsBusiness(A)` HTTP client and `AssertCannotList`, `AssertCannotRead`, `AssertCannotModify` (update and delete), `AssertCannotReference`, each failing with the operation and the leaked record id (FR-015, FR-016)
- [X] T030 [US3] Write `backend/tests/HelloPos.Api.Tests/Tenancy/SampleIsolationTests.cs`: one short file using the harness for every sample operation (SC-002, SC-006)
- [X] T031 [US3] Write `backend/tests/HelloPos.Api.Tests/Tenancy/HarnessDetectsLeaksTests.cs`: test-only "leaky" sample endpoints that read and write through a context connected as the **migrator** (table owner, so RLS does not apply) with `AcrossAllBusinesses()`, i.e. with both layers deliberately bypassed; each harness assertion run against them must fail, and the failure message must name the operation and the leaked record id (SC-003)
- [X] T032 [US3] Copy contracts/tenancy-developer-guide.md to `docs/tenancy.md`, replacing the indicative test example with the real harness API and adding the exact `dotnet ef migrations add` command through the wrapper (FR-021)

---

## Phase 6: User Story 4 - Conventions are followed automatically (P3)

- [ ] T033 [US4] Write `backend/tests/HelloPos.Api.Tests/Persistence/ConventionTests.cs`: catalog check per research R11 over the migrated test database including sample tables (passes); negative cases create, in a scratch schema treated as `pos`-like by the check, tables that each break one rule (no `business_id`, PK without it, FK without it, RLS off, `timestamp without time zone`, `double precision`, unconstrained `numeric`) and assert the check names the table/column and rule (SC-001, FR-020)
- [ ] T034 [US4] Implement the catalog check as `backend/tests/HelloPos.Api.Tests/Persistence/SchemaConventions.cs` (reusable by later features' tests) (makes T033 pass)
- [ ] T035 [P] [US4] Add a unit test that `Guid` ids created by the server for business-owned entities are version 7 and time-ordered (FR-018), in `backend/tests/HelloPos.Api.Tests/Persistence/IdConventionTests.cs`

---

## Phase 7: Polish

- [ ] T036 Run the full backend suite; all tests pass, including feature 001/002 tests (health, contract, probe)
- [ ] T037 [P] Update `README.md` (link `docs/tenancy.md`; migrations in "Local environment"; migrator login in settings) and `specs/roadmap.md` #3 if scope changed
- [ ] T038 Re-check the Constitution Check against delivered code; note deviations in `plan.md`; finish `checklists/quickstart-results.md`

---

## Dependencies & Execution Order

- Setup → Foundational → US1 (migrations are needed by every later test).
- US2 needs US1 (migrated database) and T019's sample model.
- US3 needs US2 (sample endpoints and confinement).
- US4 needs US1 (migrated schema) and T019 (sample tables to check); can run alongside US3.
- Polish last.

### Parallel opportunities

- T002/T003; T006/T007/T008; T020–T024 once T019 exists; T035 anytime after T005.

## Implementation Strategy

1. MVP: Setup + Foundational + US1 → migrations by the migrator, app login with row rights only → commit.
2. US2 → confinement at both layers → commit.
3. US3 → harness and its negative proof → commit.
4. US4 → convention check → commit; Polish → PR.

## Notes

- Never connect tests to business data as the superuser; RLS would be bypassed and tests would lie.
- Sample tables and endpoints exist only in the test project.
