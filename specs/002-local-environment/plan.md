# Implementation Plan: Local Environment

**Branch**: `002-local-environment` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-local-environment/spec.md`

## Summary

Add a compose-based local environment (project `hellopos`) with PostgreSQL 18, the Prism mock API
serving `contracts/openapi.yaml`, Seq, and an optional `api` service built from a new chiseled,
non-root API image. `scripts/env.ps1|sh` give one-command `up [--api]`, `down`, `reset`, `status`
and `logs` on Podman or Docker, creating `.env` from `.env.example`. PostgreSQL is initialised with
a separate admin and least-privilege application login. The API gains a 2-second database probe
so `/health` reports `degraded` when the database is unreachable, tested against a real
PostgreSQL through Testcontainers running inside the SDK container. Decisions:
[research.md](research.md); interface: [contracts/environment-interface.md](contracts/environment-interface.md).

## Technical Context

**Language/Version**: C# on .NET 10 (feature 001 toolchain); PowerShell 5.1+ and POSIX shell for
scripts; compose specification (docker-compose v2.40 provider)

**Primary Dependencies**: images `postgres:18.6`, `stoplight/prism:5.15.10`,
`datalust/seq:2026.1.17182`, `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`; packages
`Npgsql` 10.0.3, `Testcontainers.PostgreSql` 4.16.0

**Storage**: PostgreSQL 18 (empty application database; named volumes for database and Seq)

**Testing**: xUnit v3 with Testcontainers (real PostgreSQL) for the probe; existing contract tests;
manual quickstart for compose behavior

**Target Platform**: developer machines (Windows with Podman machine, Linux, macOS; Docker as an
alternative)

**Project Type**: development infrastructure plus one change to the API

**Performance Goals**: later starts under 1 minute; `/health` under 3 seconds in every state

**Constraints**: no secrets committed; published ports bound to `127.0.0.1` by default; API image
non-root, no shell

**Scale/Scope**: 4 services, 2 scripts, 1 Dockerfile, 1 API change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | No money. | N/A |
| II. Never Lose or Duplicate a Sale or a Charge | No sales. | N/A |
| III. Tenant Isolation | No tenant data yet. The application login gets no table rights until #3 adds tenancy. | Pass |
| IV. Sales Are an Append-Only Record | No sales. | N/A |
| V. Contract-First API | No contract change; the mock API serves the contract itself, which is the mandated mock (Principle V). | Pass |
| VI. Test-First for Business Rules | The probe → status rule is tested first, against real PostgreSQL (Testcontainers), not a substitute. | Pass |
| VII. Vertical Slices, Clear Boundaries | The probe is infrastructure under `Infrastructure/Database/`; the `GetHealth` slice only maps its result. | Pass |
| VIII. Secure by Default | Separate admin and least-privilege app logins; secrets only in untracked `.env`; ports on loopback; non-root, shell-less API image with no baked settings. | Pass |
| IX. No Silent Failures | Probe failures logged with trace id; `up` fails naming the unhealthy service. | Pass |
| X. Simplicity | Compose instead of an orchestrator; Prism instead of a custom mock; no EF Core yet. | Pass |

Result: **pass**. Re-checked after Phase 1 design: still pass.

## Project Structure

### Documentation (this feature)

```text
specs/002-local-environment/
├── plan.md
├── research.md                       # R1–R9
├── data-model.md                     # database objects, health rule, volumes
├── quickstart.md
├── contracts/environment-interface.md  # commands, .env settings, addresses
├── checklists/requirements.md
└── tasks.md                          # /speckit-tasks
```

### Source Code (repository root)

```text
compose.yaml                          # postgres, mock-api, seq, api (profile "api")
.env.example                          # committed placeholders; .env is git-ignored
.dockerignore
infra/postgres/init/01-app-database.sh
scripts/
├── env.ps1 / env.sh                  # up [--api] | down | reset [--yes] | status | logs
└── dotnet.ps1 / dotnet.sh            # + engine socket and Testcontainers settings for `test`

backend/src/HelloPos.Api/
├── Dockerfile                        # sdk:10.0 build → aspnet:10.0-noble-chiseled runtime
├── Infrastructure/Database/
│   ├── DatabaseProbe.cs              # IDatabaseProbe, Npgsql SELECT 1 with 2 s timeout
│   └── DatabaseSetup.cs              # registers NpgsqlDataSource when configured
└── Features/Health/GetHealth/        # maps probe result to available/degraded

backend/tests/HelloPos.Api.Tests/
├── Infrastructure/Database/DatabaseProbeTests.cs        # Testcontainers PostgreSQL
└── Features/Health/GetHealthDatabaseTests.cs            # available / degraded via the API
```

**Structure Decision**: environment files at the repository root (they span all apps); database
init under `infra/`; the API change stays inside the existing vertical slice and infrastructure
folders.

## Complexity Tracking

No constitution violations. Mounting the container engine socket into the SDK container (R9) is
the one sharp tool here: it is limited to `test` commands, where Testcontainers needs it.
