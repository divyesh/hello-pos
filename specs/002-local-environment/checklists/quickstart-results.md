# Quickstart Results: Local Environment

**Run**: 2026-10-11, Windows 11, Podman 5.4 (rootful machine), docker-compose v2.40 provider
**Guide**: [quickstart.md](../quickstart.md)

## Section 1: start, persist, reset (User Story 1)

| Check | Result |
|-------|--------|
| `.env` created from `.env.example` when missing | ✅ message printed |
| `up` waits for health and prints addresses | ✅ postgres and seq healthy; `scripts/env.ps1` and `scripts/env.sh` |
| Init creates database and app login | ✅ `Created database 'hellopos' and least-privilege login 'hellopos_app'` |
| App login on `hellopos` | ✅ connects |
| App login on `postgres` database | ✅ refused (`permission denied for database "postgres"`) |
| App login creates database / role / table | ✅ all denied |
| Password required from the network | ✅ right password works, wrong password rejected (SCRAM) from another container |
| Data survives `down` / `up` | ✅ marker row still present |
| `reset --yes` deletes data | ✅ only `hellopos` containers, network and volumes removed; marker gone after `up` |

### Issues found and fixed during this run

1. `CREATE DATABASE ... OWNER CURRENT_USER` is not valid PostgreSQL; the database was never
   created. Fixed: owner named explicitly.
2. A failed init script still left the service "healthy" (`pg_isready` only checks the server).
   Fixed: the health check now queries the application database, so a failed init fails `up`.
3. The app login could connect to the built-in `postgres` database (PUBLIC has CONNECT by default).
   Fixed: CONNECT revoked from PUBLIC on `postgres` and `template1`.
4. Windows PowerShell 5.1 treated compose's progress output (stderr) as a terminating error.
   Fixed: `scripts/env.ps1` judges compose by exit code only.

Note: inside the database container, local connections are trusted (the official image's
default); every connection from the network, including the API's, requires the password.

## Section 2: mock API (User Story 2)

| Check | Result |
|-------|--------|
| `up` starts `mock-api` healthy | ✅ health check `wget /health` inside the container |
| `GET /health` on the mock returns the contract example | ✅ identical to the `example` in `contracts/openapi.yaml`; CORS allows any origin |
| Changed contract is served after `up` | ✅ only `mock-api` re-created (contract hash in its configuration); new example returned |
| Broken contract fails `up` naming `mock-api` | ✅ exit 1; status table shows `mock-api exited`, postgres and seq healthy |
| Web start page against the mock | ⏳ pending, needs a browser (developer) |
| Android start screen against the mock | ⏳ pending, needs an emulator (developer) |

### Issue found and fixed

Prism 5.15.10 crashes in its default multi-process mode (`Cannot read properties of undefined
(reading 'isPrimary')`). The service runs Prism with `--multiprocess false`.

## Section 3: real API (User Story 3)

| Check | Result |
|-------|--------|
| `up --api` builds the image and waits for `/health` | ✅ `API http://127.0.0.1:5080 (status: available, version 0.1.0-dev)`; build context 24 kB |
| API reaches the database with the least-privilege login | ✅ `available` with `ConnectionStrings__HelloPos` using `hellopos_app` |
| Database stopped → `degraded` | ✅ after 1 s; response in 0.03 s; four contract fields only |
| Database started → `available` | ✅ immediately |
| API logs in Seq without setup | ✅ request summaries plus `Database probe failed` (Warning) carrying the same trace id as its request |
| Image runs as non-root | ✅ `User=1654` (chiseled, no shell) |
| No secrets in the image | ✅ no `.env`; `appsettings*.json` hold only log levels and the dev CORS origin; no settings in image `Env` |

## Section 4: automated tests

✅ `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`: 15/15, including probe and `/health`
tests against a real PostgreSQL 18.6 started by Testcontainers inside the SDK container (engine
socket mounted for `test` runs).

### Issue found and fixed

Against a *hung* database (container paused), the probe took 6 s despite a 2 s cancellation:
Npgsql's cancel request goes to the same hung server. Fixed: the probe bounds its wait with
`Task.WaitAsync(2 s)` and abandons the query (cancelled by its own token, failure observed); the
hung-server test now passes within 3 s.

## Section 5: Docker

⏳ **Pending**: the Docker CLI is installed but the Docker Desktop engine is not running on the
development machine. The scripts only differ by `CONTAINER_ENGINE=docker` (which calls
`docker compose` and mounts `/var/run/docker.sock` for tests); run section 1 with Docker Desktop
started to close SC-005. CI (roadmap #4) runs on Docker and will exercise the same compose file.
