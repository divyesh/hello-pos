# Quickstart: Local Environment

How to prove feature 002 works. Commands are for PowerShell (`scripts/env.ps1`); use
`scripts/env.sh` on Linux/macOS. The README is the lasting copy (FR-016).

## Prerequisites

Feature 001's prerequisites (Podman or Docker; Android Studio for the Android checks).

## 1. Start, persist, reset (User Story 1, SC-001, SC-002)

1. Delete `.env` if present, then run `scripts/env.ps1 up`.
   Expected: `.env` is created from `.env.example` (message says so); the command returns after
   reporting `postgres`, `mock-api` and `seq` healthy, with their addresses.
2. Check least privilege as the application login:
   `podman exec hellopos-postgres-1 psql -U hellopos_app -d hellopos -c "select current_user"` works;
   `... -d postgres` is refused; `... -d hellopos -c "create database x"` is refused.
3. Write a marker as the admin login:
   `podman exec hellopos-postgres-1 psql -U hellopos_admin -d hellopos -c "create table marker(x int); insert into marker values (1)"`.
4. `scripts/env.ps1 down`, then `scripts/env.ps1 up`: `select * from marker` still returns `1`.
5. `scripts/env.ps1 reset` (answer yes), then `up`: the `marker` table no longer exists.

## 2. Mock API (User Story 2)

1. `curl http://localhost:4010/health` returns the contract's example health response.
2. Point the web app at the mock (`Api:BaseUrl` = `http://localhost:4010/`) and start it with
   `scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web`: the start page shows
   available. Revert the setting.
3. Android: `hellopos.apiBaseUrl=http://10.0.2.2:4010/` in `android/local.properties`, install,
   open: the start screen shows available. Revert.
4. Break `contracts/openapi.yaml` (for example delete the `paths:` line), run `scripts/env.ps1 up`:
   it fails naming `mock-api`. Revert, run `up` again.

## 3. Real API in the environment (User Story 3, SC-003)

1. `scripts/env.ps1 up --api`: `curl http://localhost:5080/health` returns `status: available`.
2. `podman stop hellopos-postgres-1`: within 10 s `/health` returns `status: degraded`, answering
   within 3 s, with only the four contract fields.
3. `podman start hellopos-postgres-1`: within 10 s `/health` returns `available` again.
4. Open `http://localhost:5341`: the API's request events are there, with `Application =
   hello-pos-api`.
5. Inspect the image: `podman image inspect` shows a non-root user; no `.env` or secrets in it.

## 4. Automated tests

`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` passes, including the database probe
tests that start their own PostgreSQL container (the wrapper passes the engine socket for `test`
commands).

## 5. Docker instead of Podman (SC-005)

With Docker running: `$env:CONTAINER_ENGINE = "docker"` and repeat section 1. Same results.
