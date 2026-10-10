# Implementation Plan: Repository Skeleton

**Branch**: `001-repository-skeleton` | **Date**: 2026-10-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-repository-skeleton/spec.md`

## Summary

Create the Hello POS monorepo skeleton: a .NET 10 backend (vertical-slice API plus a shared
kernel library), a Blazor WebAssembly back office, a Kotlin/Compose Android app, and a
`contracts/` folder holding the hand-written OpenAPI contract and the shared pricing test-case
file with its schema. A single `GET /health` operation goes through the whole chain: defined in
the contract, implemented as an API slice that a test checks against the contract, and shown on
the web and Android start screens through clients generated from the contract (Kiota for web,
OpenAPI Generator for Android). Toolchains are pinned; every app builds and tests with one
command each. .NET runs in the official SDK container (Podman or Docker), so no local SDK is
needed. Decisions and alternatives: [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (SDK pinned via `global.json`, stable feature bands only;
run in the official SDK container through `scripts/dotnet.*`, Podman by default, Docker optional);
Kotlin 2.x on JDK 21 toolchain (Android Studio's bundled JDK)

**Primary Dependencies**: ASP.NET Core minimal APIs; Serilog (console compact JSON + optional
Seq sink); Blazor WebAssembly + MudBlazor; Microsoft
Kiota (web client); Jetpack Compose (Material 3), Hilt, Retrofit, OkHttp, kotlinx.serialization,
OpenAPI Generator Gradle plugin (Android client)

**Storage**: N/A in this feature (PostgreSQL arrives with roadmap #2)

**Testing**: xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`, bUnit, Microsoft.OpenApi +
JsonSchema.Net (contract and schema validation); JUnit 5, Turbine, MockWebServer, Konsist,
networknt json-schema-validator (Android)

**Target Platform**: API on Linux containers (DigitalOcean, later); web as a static site in modern
browsers; Android 8.0+ (minSdk 26, target/compile SDK 36), tablets first

**Project Type**: monorepo with a web service, a WebAssembly web app, and a mobile app

**Performance Goals**: health result on screen within 5 s (SC-003); full clone-to-tested in under
30 min (SC-001)

**Constraints**: no secrets in client configuration; plain HTTP only in debug builds; generated
clients must always match `contracts/openapi.yaml`

**Scale/Scope**: 1 API operation, 2 start screens, ~8 projects/modules; no business features

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | No money yet. The pricing-case file and schema use integer minor units; both suites read it (R12). | Pass |
| II. Never Lose or Duplicate a Sale or a Charge | No sales yet. | N/A |
| III. Tenant Isolation | No tenant data yet; `/health` is anonymous and global by design. Tenancy infrastructure is roadmap #3. | Pass |
| IV. Sales Are an Append-Only Record | No sales yet. | N/A |
| V. Contract-First API | Hand-written `contracts/openapi.yaml`; API verified against it by test (R5); both clients generated from it (R6, R7). | Pass |
| VI. Test-First for Business Rules | No business rules. Rule-bearing checks (contract conformance, schema validation, dependency rules) each have a test. PostgreSQL integration tests start in #2/#3. | Pass |
| VII. Vertical Slices, Clear Boundaries | API uses `Features/<Area>/<UseCase>/`; kernel has no framework or app references (test-enforced); web calls only the API; Android `domain` has no Android imports (test-enforced). | Pass |
| VIII. Secure by Default | `/health` exposes four fields only; client configs hold no secrets; API URLs from configuration; cleartext HTTP limited to debug. | Pass |
| IX. No Silent Failures | Structured JSON logs carrying a per-request trace id; API errors as RFC 7807 problem details with the same trace id (both test-enforced); clients show "API unavailable" instead of failing silently. | Pass |
| X. Simplicity | Kernel starts empty; one Android module; no database, auth or CI in this feature. Project count justified below. | Pass |

Result: **pass**, no violations. Re-checked after Phase 1 design (contracts, data model,
quickstart): still pass. `/speckit-analyze` found that structured logging with a correlation id
(Principle IX) had no task; it was added to User Story 1.

## Project Structure

### Documentation (this feature)

```text
specs/001-repository-skeleton/
├── plan.md              # This file
├── research.md          # Phase 0: decisions R1–R12
├── data-model.md        # Phase 1: HealthStatus, client states, pricing-case file
├── quickstart.md        # Phase 1: validation guide
├── contracts/
│   ├── openapi.yaml               # design contract for GET /health
│   └── pricing-cases.schema.json  # design schema for shared pricing cases
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
global.json                          # .NET SDK pin (R1)
Directory.Build.props                # net10.0, nullable, warnings as errors (backend + web)
Directory.Packages.props             # central package versions (backend + web)
.config/dotnet-tools.json            # Kiota local tool (R6)
scripts/
├── dotnet.ps1                       # run dotnet in the SDK container (R13)
└── dotnet.sh

contracts/
├── openapi.yaml                     # source of truth (from the design contract)
├── pricing-cases.json               # { "version": 1, "cases": [] }
└── pricing-cases.schema.json

backend/
├── HelloPos.slnx
├── src/
│   ├── HelloPos.Api/
│   │   ├── Program.cs               # composition only: services, middleware, feature mapping
│   │   ├── Infrastructure/          # Serilog (console JSON, optional Seq) + trace id, problem details, CORS
│   │   └── Features/
│   │       └── Health/
│   │           └── GetHealth/       # endpoint, response, handler
│   └── HelloPos.Kernel/             # shared primitives (empty for now)
└── tests/
    ├── HelloPos.Api.Tests/          # /health behavior + contract conformance
    └── HelloPos.Kernel.Tests/       # pricing-case schema test, kernel dependency rule

web/
├── HelloPos.Web.slnx
├── src/
│   └── HelloPos.Web/                # Blazor WebAssembly, MudBlazor
│       ├── ApiClient/               # Kiota-generated (committed)
│       ├── Pages/Start.razor        # start screen
│       └── wwwroot/appsettings*.json
└── tests/
    └── HelloPos.Web.Tests/          # bUnit start-screen states

android/
├── settings.gradle.kts
├── build.gradle.kts
├── gradle/libs.versions.toml        # all versions pinned
├── gradlew, gradlew.bat, gradle/wrapper/
└── app/
    ├── build.gradle.kts             # applicationId com.recbook.hellopos; OpenAPI generation
    └── src/
        ├── main/java/com/recbook/hellopos/
        │   ├── core/   (network, designsystem, util)
        │   ├── data/   (remote: health client wrapper; repository)
        │   ├── domain/ (model: HealthStatus)
        │   └── feature/start/        # StartScreen, StartViewModel, StartUiState
        ├── debug/res/xml/network_security_config.xml   # cleartext for dev hosts only
        └── test/java/com/recbook/hellopos/              # JUnit 5, Konsist, schema test
```

**Structure Decision**: three application roots (`backend/`, `web/`, `android/`) plus
`contracts/`, as fixed by the constitution. Each .NET app has its own solution so it can be built,
tested and later deployed on its own; the web app references `backend/src/HelloPos.Kernel` by
project path. Android stays a single module with package boundaries, per the README.

## Complexity Tracking

No constitution violations. Project count is noted here because Principle X asks new projects to
be justified:

| Project | Why it exists now |
|---------|-------------------|
| `HelloPos.Kernel` (+ tests) | Must exist before the pricing engine (#10) so the API and the web app share one money and pricing implementation (Principle I). Empty until then. |
| `HelloPos.Api.Tests` | Required for contract conformance (Principle V) and every later slice's tests. |
| `HelloPos.Web.Tests` | Start-screen behavior (FR-008) needs an automated check; bUnit is the standard. |
| Two .NET solutions | Backend and web build, test and deploy independently (API container vs static site). |
