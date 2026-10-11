# Data Model: Local Environment

No application data: the database starts empty (tables arrive with roadmap #3).

## Database objects created on first start

| Object | Name (default) | Rules |
|--------|----------------|-------|
| Admin login | `POSTGRES_USER` (`hellopos_admin`) | Setup only; never used by the API |
| Application database | `hellopos` | Owned by the admin login; `CONNECT` revoked from `PUBLIC` |
| Application login | `hellopos_app` | `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE`; `CONNECT` on `hellopos` only; no table or schema privileges yet (decided in #3) |

Created only when the data volume is empty; later starts reuse the volume (FR-007).

## Health status (existing contract, new rule)

| Database configured? | Probe result (≤ 2 s) | `status` |
|----------------------|----------------------|----------|
| No | n/a | `available` |
| Yes | `SELECT 1` succeeds | `available` |
| Yes | fails or times out | `degraded` |

The response keeps exactly the four contract fields; probe errors are logged, never returned.

## Persistent volumes

| Volume | Holds | Removed by |
|--------|-------|------------|
| `hellopos_hellopos-postgres` | database files | `scripts/env reset` |
| `hellopos_hellopos-seq` | Seq events and settings | `scripts/env reset` |
| `hellopos-nuget` | NuGet cache for the SDK wrapper (feature 001) | not touched by this feature |
