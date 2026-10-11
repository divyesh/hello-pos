---
description: "Task list for feature 002: local environment"
---

# Tasks: Local Environment

**Input**: Design documents from `specs/002-local-environment/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/environment-interface.md](contracts/environment-interface.md),
[quickstart.md](quickstart.md)

**Tests**: The database probe rule is business-relevant behavior and gets automated tests first,
against real PostgreSQL (Constitution VI). Compose and script behavior (start, persistence, reset,
least privilege, mock responses) is verified by explicit verification tasks that run the
quickstart steps and record results.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1, US2, US3

## Conventions for every task

- .NET commands only through `scripts/dotnet.ps1`; Gradle with `JAVA_HOME` set to Android
  Studio's JDK 21 (feature 001 conventions).
- Image tags and package versions exactly as in research.md; no `latest`.
- Every published port binds to `${HELLOPOS_BIND_ADDRESS}`; every setting comes from `.env`.
- Scripts touch only compose project `hellopos`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 [P] Create `.env.example` at the repository root with every variable in contracts/environment-interface.md ("Local settings") and its example value, plus a header comment: development placeholders only, copy to `.env` (done automatically by `scripts/env`), never commit `.env`
- [X] T002 [P] Verify `.gitignore` ignores `.env` and keeps `.env.example` tracked (`git check-ignore .env` succeeds, `git check-ignore .env.example` fails); fix `.gitignore` if not

**Checkpoint**: settings template in place.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T003 Create `compose.yaml` at the repository root with `name: hellopos` and an empty `services:` map plus the top-level `volumes:` entries `hellopos-postgres` and `hellopos-seq` (research R1); every later service is added to this file

**Checkpoint**: compose project exists; user stories can add services.

---

## Phase 3: User Story 1 - Start and stop the shared services with one command (Priority: P1) 🎯 MVP

**Goal**: `scripts/env up|down|reset|status|logs` manage PostgreSQL and Seq with persistence and
least-privilege logins.

**Independent Test**: quickstart.md section 1.

### Implementation for User Story 1

- [X] T004 [P] [US1] Create `infra/postgres/init/01-app-database.sh` (LF line endings, executable): using `POSTGRES_USER`, `HELLOPOS_DB_NAME`, `HELLOPOS_DB_APP_USER`, `HELLOPOS_DB_APP_PASSWORD`, run `psql -v ON_ERROR_STOP=1` to create database `${HELLOPOS_DB_NAME}` owned by the admin login, create role `${HELLOPOS_DB_APP_USER}` with `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD ...`, `REVOKE CONNECT ON DATABASE ... FROM PUBLIC`, `GRANT CONNECT ... TO` the app role; no table or schema grants (data-model.md, research R3)
- [X] T005 [US1] Add service `postgres` to `compose.yaml`: image `postgres:18.6`; env `POSTGRES_USER`, `POSTGRES_PASSWORD`, `HELLOPOS_DB_NAME`, `HELLOPOS_DB_APP_USER`, `HELLOPOS_DB_APP_PASSWORD` from `.env`; volume `hellopos-postgres:/var/lib/postgresql` (PostgreSQL 18 data layout) and `./infra/postgres/init:/docker-entrypoint-initdb.d:ro`; port `${HELLOPOS_BIND_ADDRESS}:${HELLOPOS_PG_PORT}:5432`; health check `pg_isready -U $${POSTGRES_USER} -d postgres` (interval 5s, retries 12) (depends on T003, T004)
- [X] T006 [US1] Add service `seq` to `compose.yaml`: image `datalust/seq:2026.1.17182`; env `ACCEPT_EULA=Y`, `SEQ_FIRSTRUN_NOAUTHENTICATION=true`; volume `hellopos-seq:/data`; port `${HELLOPOS_BIND_ADDRESS}:${HELLOPOS_SEQ_PORT}:80`; health check against its HTTP API using a tool available in the image (verify which; if none, use a compose `healthcheck` with the image's own binary or fall back to the script's host-side check) (research R5)
- [X] T007 [US1] Create `scripts/env.ps1`: subcommands `up [--api]`, `down`, `reset [--yes]`, `status`, `logs [service]` per contracts/environment-interface.md; engine from `CONTAINER_ENGINE` (default `podman`) calling `<engine> compose --project-name hellopos --env-file .env -f compose.yaml`; creates `.env` from `.env.example` when missing and says so; `up` runs `up -d --wait` (adds `--profile api` and `--build` for `--api`), on failure prints the unhealthy service and exits non-zero, on success prints each service address from `.env`; `reset` prompts unless `--yes`, then `down --volumes --remove-orphans`; works from any working directory (resolves the repository root from the script location)
- [X] T008 [P] [US1] Create `scripts/env.sh` with the same behavior as T007 for Linux/macOS; mark executable in git
- [X] T009 [US1] Verify User Story 1 by running quickstart.md section 1 (create `.env`, healthy services, least-privilege checks, persistence across `down`/`up`, `reset` removes data) and record results in `specs/002-local-environment/checklists/quickstart-results.md` (depends on T005–T007)

**Checkpoint**: database and Seq run with one command. MVP of this feature.

---

## Phase 4: User Story 2 - Build clients against a stand-in API (Priority: P2)

**Goal**: Prism serves `contracts/openapi.yaml` at port 4010.

**Independent Test**: quickstart.md section 2.

- [ ] T010 [US2] Add service `mock-api` to `compose.yaml`: image `stoplight/prism:5.15.10`; command `mock -h 0.0.0.0 -p 4010 /contracts/openapi.yaml`; volume `./contracts:/contracts:ro`; port `${HELLOPOS_BIND_ADDRESS}:${HELLOPOS_MOCK_PORT}:4010`; health check that fails when the mock is not serving (verify a tool available in the image; if none, have `scripts/env` check `GET /health` from the host) (research R4)
- [ ] T011 [US2] Make `scripts/env up` print the mock API address and recreate `mock-api` when `contracts/openapi.yaml` changed (compose `up` does this when the service config or a mounted file hash is part of its labels; otherwise always `--force-recreate mock-api`) in `scripts/env.ps1` and `scripts/env.sh`
- [ ] T012 [US2] Verify User Story 2 by running quickstart.md section 2 (`curl /health` on the mock returns the contract example; web and Android start screens show available against the mock; a broken contract makes `up` fail naming `mock-api`) and record results in `specs/002-local-environment/checklists/quickstart-results.md`

**Checkpoint**: clients can be built against the mock.

---

## Phase 5: User Story 3 - Run the real API in the environment (Priority: P3)

**Goal**: chiseled non-root API image in compose; `/health` reports `degraded` when the database
is unreachable; logs reach Seq.

**Independent Test**: quickstart.md sections 3 and 4.

### Tests for User Story 3 ⚠️ (write first, see them fail)

- [ ] T013 [US3] Extend `scripts/dotnet.ps1` and `scripts/dotnet.sh`: when the first dotnet argument is `test`, mount the engine socket (`HELLOPOS_ENGINE_SOCKET`, default `/run/podman/podman.sock` for podman and `/var/run/docker.sock` for docker) at `/var/run/docker.sock` and set `DOCKER_HOST=unix:///var/run/docker.sock`, `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`, `TESTCONTAINERS_RYUK_DISABLED=true`; on Windows, prevent path conversion of the socket path (research R9)
- [ ] T014 [P] [US3] Write `backend/tests/HelloPos.Api.Tests/Infrastructure/Database/PostgresFixture.cs`: an xUnit class fixture starting `postgres:18.6` with Testcontainers.PostgreSql, exposing its connection string, `StopAsync`/`StartAsync` to simulate outages; add `Testcontainers.PostgreSql` 4.16.0 to `Directory.Packages.props` and the test project (depends on T013)
- [ ] T015 [US3] Write `backend/tests/HelloPos.Api.Tests/Infrastructure/Database/DatabaseProbeTests.cs`: reachable database → probe reports reachable; stopped database → probe reports unreachable within 3 s; wrong password → unreachable; the probe never throws (depends on T014)
- [ ] T016 [US3] Write `backend/tests/HelloPos.Api.Tests/Features/Health/GetHealthDatabaseTests.cs`: with `ConnectionStrings:HelloPos` pointing at the fixture, `/health` returns `available`; after stopping the container it returns `degraded` within 3 s; the body still validates against the contract (`OpenApiContract`) and contains no database name, host or error text; after restarting it returns `available` (FR-013, FR-014, SC-003) (depends on T014)

### Implementation for User Story 3

- [ ] T017 [US3] Add `backend/src/HelloPos.Api/Infrastructure/Database/DatabaseProbe.cs` (`IDatabaseProbe` with `Task<bool> IsReachableAsync(CancellationToken)`; `NpgsqlDatabaseProbe` opening a connection from `NpgsqlDataSource` and running `SELECT 1` with a 2-second overall timeout, catching `NpgsqlException`, `TimeoutException` and timeout cancellation, logging failures at Warning via source-generated `LoggerMessage`; `NoDatabaseProbe` returning true) and `DatabaseSetup.cs` (`AddHelloPosDatabase(configuration)`: when `ConnectionStrings:HelloPos` is set, register `NpgsqlDataSource` and `NpgsqlDatabaseProbe`, else `NoDatabaseProbe`); add `Npgsql` 10.0.3 to `Directory.Packages.props` and the API project; register in `Program.cs` (makes T015 pass)
- [ ] T018 [US3] Update `backend/src/HelloPos.Api/Features/Health/GetHealth/GetHealthEndpoint.cs`: `Handle` takes `IDatabaseProbe` and the request's `CancellationToken`, returns `Degraded` when the probe reports unreachable (makes T016 pass; feature 001 health tests still pass)
- [ ] T019 [P] [US3] Create `backend/src/HelloPos.Api/Dockerfile` (build context = repository root): stage `build` from `mcr.microsoft.com/dotnet/sdk:10.0` copying `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, the API and kernel project files first (restore layer), then sources, `dotnet publish backend/src/HelloPos.Api -c Release -o /app/publish`; stage `final` from `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, copy publish output, `ENTRYPOINT ["dotnet", "HelloPos.Api.dll"]`; no `ENV` with settings, no secrets (research R7)
- [ ] T020 [P] [US3] Create `.dockerignore` at the repository root excluding `.git`, `.env`, `**/bin`, `**/obj`, `.artifacts`, `android`, `web`, `specs`, `.claude`, `.specify`
- [ ] T021 [US3] Add service `api` to `compose.yaml` in profile `api`: build context `.`, dockerfile `backend/src/HelloPos.Api/Dockerfile`; env `ASPNETCORE_ENVIRONMENT=Development`, `ConnectionStrings__HelloPos` and `Seq__ServerUrl=http://seq:80` per contracts/environment-interface.md; port `${HELLOPOS_BIND_ADDRESS}:${HELLOPOS_API_PORT}:8080`; `depends_on` postgres and seq (`service_started`, so the API starts even if the database is down); make `scripts/env up --api` wait until `GET http://localhost:${HELLOPOS_API_PORT}/health` answers (max 60 s) and print its status (depends on T019, T020)
- [ ] T022 [US3] Run `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` and confirm all tests pass, including T015 and T016 against real PostgreSQL
- [ ] T023 [US3] Verify User Story 3 by running quickstart.md section 3 (available → stop database → degraded within 10 s → start → available; events in Seq; image user is non-root and contains no `.env`) and record results in `specs/002-local-environment/checklists/quickstart-results.md`

**Checkpoint**: all three stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T024 [P] Add a "Local environment" section to `README.md` (FR-016): prerequisites, `scripts/env` commands, service addresses and default ports, `.env` and `HELLOPOS_BIND_ADDRESS` (physical device), pointing web and Android at the mock API, running the real API with `up --api`, how database tests reach the container engine; update the "Logs (Seq)" section to use the environment's Seq
- [ ] T025 [P] If the delivered scope differs from roadmap #2's description, update `specs/roadmap.md` to match (for example the optional `api` service and the database probe)
- [ ] T026 Run quickstart.md section 5 with Docker if Docker Desktop is available; otherwise record it as pending in `checklists/quickstart-results.md` (SC-005)
- [ ] T027 Re-check the plan's Constitution Check against the delivered work and note any deviation in `specs/002-local-environment/plan.md`

---

## Dependencies & Execution Order

- Setup (T001–T002) → Foundational (T003) → US1 (T004–T009).
- US2 (T010–T012) needs T003 and the scripts from US1 (T007/T008).
- US3: T013 → T014 → T015/T016 (tests, red) → T017 → T018; T019/T020 in parallel with the tests;
  T021 after T019/T020 and US1's scripts; T022 after T018; T023 last.
- Polish after the stories.

### Parallel opportunities

- T001, T002; T004 with T003; T008 alongside T007 once its behavior is settled.
- US3: T019 and T020 alongside T014–T016.

## Implementation Strategy

1. MVP: Setup, Foundational, US1 → database and Seq with one command → commit.
2. US2 → mock API → commit.
3. US3 → API image, database probe, real-database tests → commit.
4. Polish → README, Docker check, constitution re-check → pull request.

## Notes

- Do not create tables, migrations, EF Core, or seed data (roadmap #3 and later).
- Never print or log passwords from `.env`.
