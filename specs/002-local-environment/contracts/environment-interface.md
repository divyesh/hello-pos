# Environment Interface

What developers and other features rely on. The API contract (`contracts/openapi.yaml`) does not
change in this feature: `GET /health` already allows `status: degraded`.

## Commands (`scripts/env.ps1`, `scripts/env.sh`)

| Command | Effect | Exit code |
|---------|--------|-----------|
| `up` | Creates `.env` from `.env.example` if missing; starts `postgres`, `mock-api`, `seq`; waits until all are healthy; prints addresses | 0 when all healthy; non-zero naming the failed service |
| `up --api` | Same, plus builds and starts the `api` image; waits until `/health` answers | as above |
| `down` | Stops and removes this environment's containers; keeps volumes | 0 |
| `reset` | Asks for confirmation, then `down` and deletes this environment's volumes | 0, or 1 if not confirmed |
| `reset --yes` | Same without asking | 0 |
| `status` | Lists this environment's services and health | 0 |
| `logs [service]` | Follows logs of one service or all | 0 |

Engine: `CONTAINER_ENGINE` (`podman` default, or `docker`). Only containers and volumes of compose
project `hellopos` are touched.

## Local settings (`.env`, untracked; `.env.example`, committed)

| Variable | Example value | Used by |
|----------|---------------|---------|
| `POSTGRES_USER` | `hellopos_admin` | postgres (setup login) |
| `POSTGRES_PASSWORD` | `change-me-admin` | postgres |
| `HELLOPOS_DB_NAME` | `hellopos` | postgres init, api |
| `HELLOPOS_DB_APP_USER` | `hellopos_app` | postgres init, api |
| `HELLOPOS_DB_APP_PASSWORD` | `change-me-app` | postgres init, api |
| `HELLOPOS_BIND_ADDRESS` | `127.0.0.1` | all published ports (`0.0.0.0` for a physical device) |
| `HELLOPOS_PG_PORT` | `5432` | postgres |
| `HELLOPOS_MOCK_PORT` | `4010` | mock-api |
| `HELLOPOS_SEQ_PORT` | `5341` | seq (UI and ingestion) |
| `HELLOPOS_API_PORT` | `5080` | api |

Example values are development placeholders only (SC-004).

## Service addresses (defaults)

| Service | From the host | From other containers |
|---------|---------------|-----------------------|
| Database | `localhost:5432` | `postgres:5432` |
| Mock API | `http://localhost:4010` | `http://mock-api:4010` |
| Seq | `http://localhost:5341` | `http://seq:80` |
| API | `http://localhost:5080` | `http://api:8080` |

Clients use the mock API by setting their existing API address: web `Api:BaseUrl` (for example in
`wwwroot/appsettings.Development.json`) to `http://localhost:4010/`; Android
`hellopos.apiBaseUrl=http://10.0.2.2:4010/` in `android/local.properties`.

## API container settings

| Variable | Value in compose |
|----------|------------------|
| `ConnectionStrings__HelloPos` | `Host=postgres;Port=5432;Database=${HELLOPOS_DB_NAME};Username=${HELLOPOS_DB_APP_USER};Password=${HELLOPOS_DB_APP_PASSWORD}` |
| `Seq__ServerUrl` | `http://seq:80` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
