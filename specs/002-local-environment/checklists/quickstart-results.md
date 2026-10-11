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

Pending.

## Section 3: real API (User Story 3)

Pending.

## Section 5: Docker

Pending.
