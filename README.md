# Hello POS

Hello POS is a rewrite of OHY POS: a multi-business point-of-sale system for India, the USA and Canada.
It replaces the old .NET 8 / MySQL backend, the old admin website, and the old Android app
(`com.recbook.ohypos`), fixing their main problems: money rounding errors, sales lost or
duplicated on network failure, data leaking between businesses, and tokens stored in plain text.

> Status: Phase 0 done (#1 repository skeleton, #2 local environment, #3 tenancy infrastructure, #4 CI). Phase 1 in progress: #5 owner sign-up and businesses, #6 staff and roles (owners add cashiers and managers with POS usernames and PINs, invite co-owners and managers by email; roles enforced on every request). #7 login and sessions (owners and managers register POS devices on the web; staff sign in on Android with username and PIN, also offline; idle lock; own PIN change). Phase 2 (#8 billing) is specified and parked; Phase 3 in progress: #10 pricing engine, #11 catalog setup (tax profiles with dated rates, categories, products with barcodes, scheduled discounts, CSV import and price-list export).

## Applications

| App | Folder | Stack | Used by |
|-----|--------|-------|---------|
| API and backend | `backend/` | .NET 10, ASP.NET Core minimal APIs, vertical slices, EF Core, PostgreSQL | All apps |
| Web back office | `web/` | Blazor WebAssembly (.NET 10), MudBlazor | Owners and managers: sign-up, businesses, catalog, taxes, staff, payment settings, reports |
| POS | `android/` | Kotlin, Jetpack Compose, Hilt, Room, WorkManager | Cashiers: selling, checkout, orders, printing |
| Contract | `contracts/` | OpenAPI, shared pricing test cases | Source of truth between the three apps |

The web back office and the Android app talk only to the API. Card payments go through Square or
Clover. The system is hosted on DigitalOcean.

## Documents

Read these before changing anything:

- [Constitution](.specify/memory/constitution.md): the rules every change must follow (exact
  money, no lost or duplicate sales or charges, tenant isolation, append-only sales,
  contract-first API, tests first).
- [Roadmap](specs/roadmap.md): v1 split into phases and features.
- [Pricing rules](specs/pricing-rules.md): how a sale's totals, taxes, discounts, tips and
  rounding are calculated.
- [Tenancy guide](docs/tenancy.md): how business data is isolated, and how to add a table, an
  endpoint and its isolation test. Read before touching business-owned data.
- Feature specs: `specs/NNN-feature-name/` (spec, plan, tasks), created per feature.

## How we work

Development follows [Spec Kit](https://github.com/github/spec-kit). Each roadmap feature gets its
own branch and goes through:

`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-analyze` → `/speckit-implement`

Every plan is checked against the constitution. API changes start in the OpenAPI contract.

## Android app structure

Single `app` module with strict package boundaries:

```
app/src/main/java/<package>/
├── core/
│   ├── money/        Money type, formatting
│   ├── network/      Retrofit setup, error mapping (ApiResult)
│   ├── security/     Keystore keys, encrypted files, PIN-hash check (offline sign-in)
│   ├── session/      device registration, session tokens, auth interceptor, token refresh, idle lock
│   ├── printing/     PrinterService, ESC/POS implementation, receipt layout
│   ├── designsystem/ theme, shared Compose components
│   └── util/
├── data/
│   ├── auth/         device pairing, sign-in/out, offline access bundle, pending attempt reports
│   ├── remote/       API interfaces and DTOs
│   ├── local/        Room database, DAOs, entities
│   └── repository/   repositories that combine remote and local
├── domain/           pure Kotlin, no Android imports
│   ├── model/        Product, Cart, CartItem, Sale, Tax, ...
│   ├── pricing/      pricing rules, tested against contracts/pricing-cases.json
│   └── usecase/
└── feature/
    ├── pairing/      register this device with a code from the back office
    ├── signin/       username + PIN pad
    ├── lock/         idle lock (same person's PIN, or "Not you?")
    ├── changepin/
    ├── home/         placeholder until selling (#14)
    ├── pos/          catalog + bill
    ├── checkout/
    ├── pending/
    ├── sales/
    ├── reports/
    ├── kitchen/
    └── settings/     printer, account, logout
```

Each feature has a `Screen` (Compose), a `ViewModel` and a `UiState`. ViewModels call
repositories and use cases, never Retrofit or Room directly. `BLUETOOTH_CONNECT` and
`BLUETOOTH_SCAN` are requested on Android 12+ before printing.

Secrets (device credential, tokens, offline bundle, sign-in memory) are kept only in AES-GCM files
under a non-exportable Android Keystore key, never in plain preferences or logs; screens set
`FLAG_SECURE`.

## Open decisions

| Question | Current assumption |
|----------|--------------------|
| Android application ID | Decided: `com.recbook.hellopos` (installs next to the old app) |
| Paid plans | Decided: subscriptions for the hosted service, paid on the web and checked by the API (roadmap Phase 2). No license key in the app. Billing can be switched off for self-hosted installs. |
| Subscription lapse behavior | Read-only or blocked, decided in roadmap #9 |
| Billing provider | Must work in India, the USA and Canada; decided in roadmap #8 |
| Email and SMS receipt providers | Decided in the receipts feature (roadmap #25) |
| Open pricing questions | Listed at the end of [pricing rules](specs/pricing-rules.md) |

## Building

### Prerequisites

- **Podman** (default) or **Docker**. All .NET commands run inside the official .NET 10 SDK
  container through `scripts/dotnet.ps1` (Windows) or `scripts/dotnet.sh` (Linux/macOS), so no
  local .NET SDK is needed. Set `CONTAINER_ENGINE=docker` to use Docker.
- **Android Studio 2026.1 or newer**, which provides JDK 21 and the Android SDK. For
  command-line Gradle builds, set `JAVA_HOME` to its bundled JDK (`<Android Studio>/jbr`).
  The Android Gradle Plugin downloads missing SDK platforms (it compiles against API 37).

A local .NET 10 SDK is optional (for IDE support); `global.json` rejects preview SDKs.

### Build and test

Run from the repository root:

```text
scripts/dotnet.ps1 build backend/HelloPos.slnx
scripts/dotnet.ps1 test --solution backend/HelloPos.slnx
scripts/dotnet.ps1 build web/HelloPos.Web.slnx
scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx
cd android
./gradlew assembleDebug testDebugUnitTest lintDebug   # gradlew.bat on Windows
```

Tests use Microsoft.Testing.Platform, so `dotnet test` takes `--solution` or `--project`.
Container builds write to `.artifacts/container/` and never touch the `bin/`/`obj/` folders a
host IDE uses.

### Run

```text
scripts/dotnet.ps1 -Port 5080 run --project backend/src/HelloPos.Api    # API on http://localhost:5080
scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web        # web on http://localhost:5090
```

Ports are published only when asked for with `-Port` (`--port` in `dotnet.sh`), so builds and
tests can run while the API and web app are up. Stop a running app with Ctrl+C; if its terminal
was closed or the script was killed, the container keeps running, so check with `podman ps` and
stop it with `podman stop <id>`.

**Android**: `./gradlew installDebug`, then open Hello POS. The emulator reaches the API at
`http://10.0.2.2:5080/` by default. On a physical device, add your computer's LAN address to
`android/local.properties`:

```text
hellopos.apiBaseUrl=http://192.168.1.20:5080/
```

Debug builds allow plain HTTP for this; release builds do not.

**Registering a device and signing in**: in the web back office (owner or manager), open
**Devices** → **Register a device**. A code like `K7QD-M3XP` is shown with a 5-minute countdown. In the
app, type the code and a name for the device; it then shows the sign-in screen. Staff sign in with the
POS username and PIN set under **Staff**. Five wrong PINs lock that person for 15 minutes; after 5
idle minutes the app locks until the same person enters their PIN. Without a connection, staff can
still sign in (their PIN is checked on the device); the session is confirmed with the API when the
connection returns. Revoking a device on the web signs everyone on it out at once.

### Local environment

One command starts the services development needs (PostgreSQL, a mock API, Seq, Mailpit), with
Podman or Docker (`CONTAINER_ENGINE=docker`):

```text
scripts/env.ps1 up            # postgres, mock-api, seq, mailpit; waits until healthy, prints addresses
scripts/env.ps1 up --api      # also builds and runs the real API image
scripts/env.ps1 status        # services and health
scripts/env.ps1 logs [svc]    # follow logs
scripts/env.ps1 down          # stop; data is kept
scripts/env.ps1 reset         # stop and DELETE local data (asks first; --yes to skip)
```

(`scripts/env.sh` on Linux/macOS.) Settings live in `.env`, created from `.env.example` on first
use and never committed: logins (admin, migrator, app), database name and ports.

`up` applies database migrations automatically after PostgreSQL is healthy: a one-off `migrate`
container bootstraps the roles and applies pending migrations as the migrator login. The API only
ever receives the app login, which can read and write rows but never change the schema. Adding
tables and migrations: [docs/tenancy.md](docs/tenancy.md).

| Service | Address | Notes |
|---------|---------|-------|
| PostgreSQL | `localhost:5432` | database `hellopos`; the API uses login `hellopos_app`, which can only connect to it |
| Mock API | `http://localhost:4010` | serves `contracts/openapi.yaml` from its examples; picks up contract changes on `up` |
| Seq | `http://localhost:5341` | API logs; no login locally |
| Mailpit | `http://localhost:8025` (SMTP `1025`) | catches every email the API sends (verification, password reset); nothing reaches real inboxes |
| API (`up --api`) | `http://localhost:5080` | the deployable image; `/health` says `degraded` when the database is unreachable |

Ports bind to `127.0.0.1` only. To test on a physical Android device, set
`HELLOPOS_BIND_ADDRESS=0.0.0.0` in `.env` (this also exposes Seq, which has no login, to your
network; switch back afterwards).

**Try the back office end to end**: `scripts/env.ps1 up --api`, then run the web app
(`scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web`) and open
`http://localhost:5090/sign-up` (use `localhost`, not `127.0.0.1`: the API accepts browser calls from
`http://localhost:5090` only). Sign up, follow the link in Mailpit, sign in, create a business and
open it. The session is an HttpOnly cookie set by the API; requests that change data also send the
header `X-HelloPos-Client: web` (CSRF guard). Sign-in and sign-up are rate limited to 10 per minute
per address (`RateLimits:OwnerAuthPerMinute`).

With a business open, **Staff** in the top bar lists the people who work there: add a cashier with a
POS username and PIN, edit, deactivate or reset PINs, and invite a manager or co-owner by email (the
link opens `/accept-invitation`; the invited person signs in with that address and accepts). Managers
see and manage cashiers only.

**Build clients against the mock API**: web, set `Api:BaseUrl` to `http://localhost:4010/` in
`web/src/HelloPos.Web/wwwroot/appsettings.Development.json`; Android, set
`hellopos.apiBaseUrl=http://10.0.2.2:4010/` in `android/local.properties`.

**API from the SDK container with Seq**: when running the API with `scripts/dotnet.ps1 run`
instead of `up --api`, send its logs to the environment's Seq with
`$env:Seq__ServerUrl = "http://host.docker.internal:5341"` (bash: `export Seq__ServerUrl=...`).

**Database tests**: `scripts/dotnet.ps1 test ...` mounts the container engine's socket (only for
`test`), so tests can start their own throwaway PostgreSQL with Testcontainers. Override the socket
path with `HELLOPOS_ENGINE_SOCKET` if your engine keeps it elsewhere.

### Setting up the catalog

In the web back office (owners and managers), open **Catalog**:

1. **Tax profiles** (owners): for example "HST 13%", or "GST 5%" with CGST 2.5% + SGST 2.5%; zero-rated and
   exempt profiles have no rates. A rate change is entered ahead of time with "Change rates from a date";
   rates already in force never change. The switch at the bottom decides whether container deposits are
   taxed.
2. **Categories**: each has a default tax profile.
3. **Products**: price (or open price), unit and quantity steps for weighed goods, barcodes (UPC-A, EAN-13,
   EAN-8 with check digits, PLU codes), age restriction, container deposit, and scheduled discounts.
4. **Import**: upload a CSV (template on the page). The preview lists every row as new, changed,
   unchanged or with errors; nothing is saved until a file without errors is imported, all at once.
   Rows match existing products by SKU, then barcode. **Export price list** on the products page writes the
   same format, so an export imports back with no changes.

### Changing how sales are priced

Pricing lives in exactly one module per side: `backend/src/HelloPos.Kernel/Pricing/` (used by the API
and the web app) and `android/.../domain/pricing/`. Both run every case in
`contracts/pricing-cases.json`, whose expected amounts are worked out by hand, never copied from
either engine. To change a rule:

1. Add cases to `contracts/pricing-cases.json` (extend `pricing-cases.schema.json` if needed) and see
   both suites fail.
2. Update [pricing rules](specs/pricing-rules.md).
3. Change both engines until both suites pass.

### After changing the API contract

Edit `contracts/openapi.yaml` first (it is the source of truth), then regenerate the web client:

```text
scripts/dotnet.ps1 tool restore
scripts/dotnet.ps1 kiota generate --language CSharp --class-name HelloPosApiClient --namespace-name HelloPos.Web.ApiClient --openapi contracts/openapi.yaml --output web/src/HelloPos.Web/ApiClient --exclude-backward-compatible
```

The Android client is regenerated automatically on every Gradle build. The backend contract tests
fail if the API's responses no longer match the contract.

## Continuous integration

Every pull request to `main` and every push to `main` runs six checks
([.github/workflows/ci.yml](.github/workflows/ci.yml)). They run with read-only permissions and no
secrets (safe for pull requests from forks), and every action is pinned to a commit SHA.

| Check | Proves | Reproduce locally |
|-------|--------|-------------------|
| `backend` | Backend builds; all tests pass, including real-PostgreSQL tests | `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` |
| `web` | Web back office builds; all tests pass | `scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx` |
| `android` | Android app builds; all unit tests pass; lint finds no errors (for example an API newer than minSdk) | `cd android; ./gradlew assembleDebug testDebugUnitTest lintDebug` |
| `contract` | The contract is valid and the committed web client matches it | `npx @redocly/cli@2.62.1 lint contracts/openapi.yaml`, then the regeneration commands above and `git status` |
| `image` | The API container image builds | `podman build -f backend/src/HelloPos.Api/Dockerfile .` |
| `environment` | `compose.yaml` is valid for every profile | `podman compose --env-file .env.example --profile api --profile tools config --quiet` |

A newer push to the same pull request cancels the older run. Test results are attached to each run
as artifacts. Flaky tests are fixed, never retried automatically.

**Making the checks required** (repository owner, once): on GitHub open **Settings → Rules →
Rulesets → New branch ruleset** (or **Settings → Branches → Add branch protection rule**), target
`main`, and enable:

- **Require a pull request before merging**;
- **Require status checks to pass**, adding `backend`, `web`, `android`, `contract`, `image` and
  `environment` (they appear in the list after the workflow has run once);
- **Require branches to be up to date before merging**;
- **Block force pushes**.

## License

[Apache License 2.0](LICENSE).
