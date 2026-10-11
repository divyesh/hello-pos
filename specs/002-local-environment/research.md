# Research: Local Environment

Decisions for feature 002. Image tags and package versions were checked on 2026-10-11 and are
pinned exactly; nothing floats.

## R1. One compose file, one project name

- **Decision**: `compose.yaml` at the repository root, compose project name `hellopos`. Services:
  `postgres`, `mock-api`, `seq`, and `api` (in the `api` profile, so it starts only on request).
  Started through `podman compose` (which delegates to the installed docker-compose v2.40
  provider) or `docker compose`.
- **Rationale**: one file, one name, so stop and reset can target exactly this environment's
  containers and volumes (FR-004). Profiles keep the real API optional (FR-001).
- **Alternatives considered**: one compose file per service (harder to start together); .NET
  Aspire (an orchestrator with a dashboard, but more moving parts and its own conventions, while
  compose matches what CI and staging need).

## R2. Commands: `scripts/env.ps1` and `scripts/env.sh`

- **Decision**: wrapper scripts with subcommands `up [--api]`, `down`, `reset [--yes]`, `status`
  and `logs [service]`. They choose the engine (`CONTAINER_ENGINE`, default `podman`), create
  `.env` from `.env.example` when missing (FR-008), run compose with `up -d --wait` so the command
  returns only when every health check passes, or exits non-zero naming the unhealthy service
  (FR-002), then print each service's address. `reset` asks for confirmation unless `--yes`, then
  runs `down --volumes` for the `hellopos` project only.
- **Rationale**: the same short commands on every machine and engine (FR-005); the compose details
  stay in one place. `--wait` relies on each service's health check (R3–R5).
- **Alternatives considered**: raw compose commands in the README (long, engine-specific, easy to
  get wrong; reset could hit other projects' volumes).

## R3. PostgreSQL

- **Decision**: image `postgres:18.6`. Named volume `hellopos-postgres`. The admin login is the
  image's `POSTGRES_USER`/`POSTGRES_PASSWORD`. An init script
  (`infra/postgres/init/01-app-database.sh`, run once on an empty volume) creates:
  - database `hellopos` (from `HELLOPOS_DB_NAME`), owned by the admin login;
  - login `hellopos_app` (from `HELLOPOS_DB_APP_USER`/`..._PASSWORD`) with `NOSUPERUSER NOCREATEDB
    NOCREATEROLE`;
  - `REVOKE CONNECT ON DATABASE hellopos FROM PUBLIC`, then `GRANT CONNECT` to `hellopos_app` only.

  Table and schema privileges for the application login are deliberately not granted yet: the
  schema, migrations and the role that runs them are decided with tenancy in roadmap #3.
  Health check: `pg_isready`.
- **Rationale**: FR-006. Separating the setup (admin) login from the application login from day
  one means the API never runs with rights it does not need.
- **Alternatives considered**: one superuser for everything (the image default; violates least
  privilege); creating the app login from the API at startup (needs the admin password in the API).

## R4. Mock API: Prism

- **Decision**: image `stoplight/prism:5.15.10`, command
  `mock -h 0.0.0.0 -p 4010 /contracts/openapi.yaml`, with `contracts/` mounted read-only. Prism
  answers with the contract's `example` values and validates requests against the contract. An
  invalid contract makes Prism exit with an error, so `up --wait` fails naming `mock-api`
  (User Story 2, scenario 4). Contract changes take effect on `scripts/env up` (which recreates the
  service when the file changed) or `podman compose restart mock-api`.
- **Rationale**: Prism is the standard OpenAPI mock server, reads OpenAPI 3.0.3, needs no code, and
  serves exactly what the contract says (FR-009, Constitution V).
- **Alternatives considered**: WireMock (needs hand-written stubs that drift from the contract);
  a hand-built .NET mock (duplicates the contract).

## R5. Seq

- **Decision**: image `datalust/seq:2026.1.17182`, `ACCEPT_EULA=Y`,
  `SEQ_FIRSTRUN_NOAUTHENTICATION=true`, named volume `hellopos-seq`, container port 80 published
  (UI and ingestion on one port). The `api` service gets `Seq__ServerUrl=http://seq:80`, so its
  logs arrive without manual setup (FR-012).
- **Rationale**: feature 001 R14 chose Seq; development-only, no authentication locally.

## R6. Ports and exposure

- **Decision**: every published port comes from `.env` (FR-015): `HELLOPOS_PG_PORT` 5432,
  `HELLOPOS_MOCK_PORT` 4010, `HELLOPOS_SEQ_PORT` 5341, `HELLOPOS_API_PORT` 5080. All are bound to
  `HELLOPOS_BIND_ADDRESS`, default `127.0.0.1`, so the unauthenticated Seq and the database are not
  reachable from the network. Testing on a physical Android device sets it to `0.0.0.0`
  (documented). The emulator's `10.0.2.2` maps to the host's loopback, so it works with the default.
- **Rationale**: Secure by Default (Principle VIII) for a local stack with development passwords
  and an unauthenticated log viewer.

## R7. API container image

- **Decision**: `backend/src/HelloPos.Api/Dockerfile`, multi-stage, built with the repository root
  as context:
  - build stage `mcr.microsoft.com/dotnet/sdk:10.0` runs `dotnet publish -c Release`;
  - runtime stage `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`: no shell or package
    manager, non-root user by default, listens on 8080 (`ASPNETCORE_HTTP_PORTS`).

  `.dockerignore` excludes build output, `.git`, `.env`, `android/` and `web/`. No secrets or
  settings are baked in; the connection string and Seq address come from environment variables
  (FR-011). The compose `api` service publishes `HELLOPOS_API_PORT` to 8080. Because the chiseled
  image has no shell or curl, the compose health check for `api` is omitted; `scripts/env` checks
  `/health` from the host instead.
- **Rationale**: smallest attack surface and the image roadmap #4 deploys.
- **Alternatives considered**: the full `aspnet:10.0` image (has a shell; larger); running the API
  from the SDK image with `dotnet run` (not the deployable artifact).

## R8. Database probe in the health check

- **Decision**: the API registers an `NpgsqlDataSource` (package `Npgsql` 10.0.3) when connection
  string `ConnectionStrings:HelloPos` is set. A small `IDatabaseProbe` (in
  `Infrastructure/Database/`) runs `SELECT 1` with a 2-second timeout (connection and command).
  The `GetHealth` slice reports `degraded` when a database is configured and the probe fails,
  `available` otherwise. When no database is configured (as in feature 001's tests and when running
  the API without the environment), the probe is a no-op that reports reachable: there is no
  dependency to be degraded by. No probe detail reaches the response (FR-014); failures are logged
  at Warning with the trace id.
- **Rationale**: FR-013/FR-014 with the existing contract (no contract change). The 2-second bound
  keeps `/health` under 3 seconds (SC-003).
- **Alternatives considered**: ASP.NET Core health-check middleware (plain-text output, separate
  endpoint; can be added later for infrastructure probes); EF Core (arrives in #3, not needed for
  one query).

## R9. Integration tests against real PostgreSQL

- **Decision**: `Testcontainers.PostgreSql` 4.16.0 starts a throwaway `postgres:18.6` for the probe
  tests (Constitution VI: real PostgreSQL, not a substitute). Because tests run inside the SDK
  container (feature 001 R13), the wrapper `scripts/dotnet.*` mounts the engine socket into the
  container **only for `test` commands**, and sets `DOCKER_HOST=unix:///var/run/docker.sock`,
  `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` (to reach published ports through the host
  gateway) and `TESTCONTAINERS_RYUK_DISABLED=true` (Ryuk's privileged sidecar is unreliable under
  Podman; tests dispose their containers). The socket path is `/run/podman/podman.sock` (rootful
  Podman machine, verified 2026-10-11) or `/var/run/docker.sock` for Docker, overridable with
  `HELLOPOS_ENGINE_SOCKET`.
- **Verified**: from the SDK container with the socket mounted, `GET /_ping` returns `OK` and
  `/version` reports Podman Engine 5.4.0.
- **Rationale**: the same tests run locally and in CI (#4, where GitHub's runners have Docker).
- **Alternatives considered**: pointing tests at the compose database (shared state between test
  runs and developers' data; tests would depend on the environment being up); skipping database
  tests locally (violates Principle VI).
