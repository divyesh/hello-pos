---
description: "Task list for feature 001: repository skeleton"
---

# Tasks: Repository Skeleton

**Input**: Design documents from `specs/001-repository-skeleton/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. The spec makes tests part of the requirements (FR-003, FR-006, FR-010,
FR-012, FR-014, SC-004, SC-005) and the constitution requires tests first (Principle VI). Within
each story, test tasks come first and must fail before the implementation that makes them pass.

**Organization**: Tasks are grouped by user story so each story can be built and checked on its
own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: Which user story the task belongs to (US1, US2, US3)

## Conventions for every task

- Run all .NET commands through `scripts/dotnet.ps1` (Windows) or `scripts/dotnet.sh` (research
  R13), never the host SDK.
- Run Gradle with `JAVA_HOME` set to Android Studio's JDK 21
  (`C:\Program Files\Android\Android Studio2\jbr` on the development machine).
- Pin every package and plugin to an exact version (latest stable that fits research.md); .NET
  versions go in `Directory.Packages.props`, Android versions in `android/gradle/libs.versions.toml`.
- Ports: API `5080`, web `5090`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Toolchain pins, container wrapper, and the shared contracts that every app uses.

- [ ] T001 Create `global.json` at the repository root pinning SDK `10.0.100` with `"rollForward": "latestFeature"` and `"allowPrerelease": false` (research R1)
- [ ] T002 [P] Create `scripts/dotnet.ps1`: run `dotnet <args>` in `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted at `/repo` (working directory mapped to the caller's relative path), a named volume `hellopos-nuget` mounted at `/nuget` with `NUGET_PACKAGES=/nuget`, interactive TTY when attached; engine from `$env:CONTAINER_ENGINE`, default `podman`; exit with the container's exit code. Publish **no ports by default**; ports are published only when requested with a `-Port <n>` parameter (repeatable) or the `HELLOPOS_PORTS` environment variable (comma-separated), so the API (5080), the web app (5090) and test runs can run side by side without port clashes. Pass through to the container any of these host environment variables that are set: `Seq__ServerUrl`, `Seq__ApiKey`, `ASPNETCORE_ENVIRONMENT`. Add `--add-host host.docker.internal:host-gateway` so the same hostname reaches the host under Podman and Docker (research R13, R14)
- [ ] T003 [P] Create `scripts/dotnet.sh` with the same behavior as T002 for Linux/macOS (`--port <n>` option or `HELLOPOS_PORTS`), additionally running the container as the calling user (`--user "$(id -u):$(id -g)"`, `HOME=/tmp/home`) so files it creates are not owned by root; mark it executable in git (`git update-index --chmod=+x`)
- [ ] T004 [P] Add `.editorconfig` at the repository root (UTF-8, LF for `.sh`, CRLF-neutral, 4-space C#/Kotlin indentation, `dotnet_diagnostic` severity defaults) with a section `[web/src/HelloPos.Web/ApiClient/**]` setting `generated_code = true` and `dotnet_analyzer_diagnostic.severity = none`, so the committed Kiota output is not failed by `TreatWarningsAsErrors`
- [ ] T005 [P] Add `.gitattributes` at the repository root (`* text=auto`, `*.sh text eol=lf`, `gradlew text eol=lf`, `*.bat text eol=crlf`, `*.jar binary`)
- [ ] T006 [P] Copy `specs/001-repository-skeleton/contracts/openapi.yaml` to `contracts/openapi.yaml` and remove its "design contract" header comment; it is the source of truth from now on
- [ ] T007 [P] Copy `specs/001-repository-skeleton/contracts/pricing-cases.schema.json` to `contracts/pricing-cases.schema.json`
- [ ] T008 [P] Create `contracts/pricing-cases.json` containing exactly `{ "$schema": "./pricing-cases.schema.json", "version": 1, "cases": [] }`
- [ ] T009 Verify the container toolchain: `scripts/dotnet.ps1 --version` prints a `10.0.x` stable version (depends on T001, T002)

**Checkpoint**: `.NET` commands run in the container and the contracts folder exists.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared build settings that all .NET projects inherit.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T010 Create `Directory.Build.props` at the repository root: `TargetFramework` `net10.0`, `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `AnalysisLevel` latest-recommended, `Deterministic` true (no `InvariantGlobalization` here: the web app needs culture data to format money and dates for India, the USA and Canada). When `$(DOTNET_RUNNING_IN_CONTAINER)` is `true`, set `UseArtifactsOutput` true and `ArtifactsPath` to `$(MSBuildThisFileDirectory).artifacts/container`, so container builds never share `bin/` and `obj/` with a host IDE; add `.artifacts/` to `.gitignore`
- [ ] T011 Create `Directory.Packages.props` at the repository root with `ManagePackageVersionsCentrally` true and an empty `ItemGroup` for package versions (filled by later tasks)
- [ ] T012 Create `.config/dotnet-tools.json` (local tool manifest) with `microsoft.openapi.kiota` pinned (research R6)

**Checkpoint**: Foundation ready; user stories can start.

---

## Phase 3: User Story 1 - Build and test everything from a fresh clone (Priority: P1) 🎯 MVP

**Goal**: The backend, web back office and Android app each build and run a passing test suite
with one documented command.

**Independent Test**: on a clean clone with only the prerequisites, the six commands in
quickstart.md section 1 all succeed with zero test failures.

### Tests for User Story 1 ⚠️ (write first; each must fail before its project compiles)

- [ ] T013 [P] [US1] Write `backend/tests/HelloPos.Api.Tests/ApiStartupTests.cs`: with `WebApplicationFactory<Program>`, a request to an unknown path returns `404` with `application/problem+json` (proves the host starts and errors are RFC 7807)
- [ ] T014 [P] [US1] Write `web/tests/HelloPos.Web.Tests/AppRenderTests.cs`: bUnit renders the app's main layout without throwing
- [ ] T015 [P] [US1] Write `android/app/src/test/java/com/recbook/hellopos/BuildConfigTest.kt` (JUnit 5): `BuildConfig.APPLICATION_ID` equals `com.recbook.hellopos`
- [ ] T016 [P] [US1] Write `backend/tests/HelloPos.Api.Tests/Infrastructure/RequestCorrelationTests.cs`: with Serilog writing to an in-memory test sink, a request produces exactly one request-summary event (method, path, status code, elapsed time) carrying a `TraceId` property, and the `traceId` in that request's problem details response (unknown path, `404`) equals it; a second test confirms that with no `Seq:ServerUrl` configured the API starts and logs to the console only (Constitution, Principle IX; research R14)

### Implementation for User Story 1

- [ ] T017 [P] [US1] Create `backend/src/HelloPos.Api/HelloPos.Api.csproj` (Web SDK, `InvariantGlobalization` true for the API only) and `backend/src/HelloPos.Api/Program.cs` with composition only: problem details (`AddProblemDetails`, status-code pages), a `public partial class Program` for tests, and a `Features/` folder; `Properties/launchSettings.json` profile `http` with `applicationUrl` `http://0.0.0.0:5080`
- [ ] T018 [US1] Add `backend/src/HelloPos.Api/Infrastructure/Logging.cs`: Serilog as the logging provider (`Serilog.AspNetCore`), configured from the `Serilog` configuration section; console sink with `CompactJsonFormatter` (one JSON event per line on stdout, no file sinks); Seq sink (`Serilog.Sinks.Seq`) added only when `Seq:ServerUrl` is set, with optional `Seq:ApiKey`; enrichment with `TraceId`/`SpanId` of the current activity, `Application` = `hello-pos-api`, and environment name; `UseSerilogRequestLogging` producing one summary event per request; minimum levels `Information` (app) and `Warning` (`Microsoft`, `System`) in `appsettings.json`; `Seq:ServerUrl` is not set in any committed settings file (it is supplied by environment variable `Seq__ServerUrl`, see quickstart §6; the compose environment in roadmap #2 sets it to the Seq container); add package versions to `Directory.Packages.props` (research R14). Also add `backend/src/HelloPos.Api/Infrastructure/ProblemDetailsSetup.cs`: problem details include `traceId` from `Activity.Current` or `HttpContext.TraceIdentifier` (contract `ProblemDetails.traceId`); register both in `Program.cs` (makes T016 pass) (depends on T017)
- [ ] T019 [P] [US1] Create `backend/tests/HelloPos.Api.Tests/HelloPos.Api.Tests.csproj` (xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`) referencing `HelloPos.Api`; add versions to `Directory.Packages.props`
- [ ] T020 [US1] Create `backend/HelloPos.slnx` containing `src/HelloPos.Api` and `tests/HelloPos.Api.Tests`; run `scripts/dotnet.ps1 test backend/HelloPos.slnx` and confirm T013 and T016 pass (depends on T018, T019)
- [ ] T021 [P] [US1] Create `web/src/HelloPos.Web/` as a standalone Blazor WebAssembly app (`Microsoft.NET.Sdk.BlazorWebAssembly`) with MudBlazor registered in `Program.cs` and `_Imports.razor`, a `MainLayout.razor` using MudBlazor's layout, an empty `wwwroot/appsettings.json`, and `Properties/launchSettings.json` with `applicationUrl` `http://0.0.0.0:5090`; add MudBlazor and Blazor package versions to `Directory.Packages.props`
- [ ] T022 [P] [US1] Create `web/tests/HelloPos.Web.Tests/HelloPos.Web.Tests.csproj` (xUnit v3, bUnit) referencing `HelloPos.Web`; add versions to `Directory.Packages.props`
- [ ] T023 [US1] Create `web/HelloPos.Web.slnx` containing `src/HelloPos.Web` and `tests/HelloPos.Web.Tests`; run `scripts/dotnet.ps1 test web/HelloPos.Web.slnx` and confirm T014 passes (depends on T021, T022)
- [ ] T024 [US1] Create the Android Gradle build in `android/`: Gradle wrapper (latest stable, committed, including `gradle/wrapper/gradle-wrapper.jar`), `settings.gradle.kts` (project name `hello-pos`, module `:app`), root `build.gradle.kts`, `gradle.properties` (AndroidX, non-transitive R classes, configuration cache), and `gradle/libs.versions.toml` with pinned AGP, Kotlin 2.x, Compose BOM, Material 3, Hilt, KSP, Retrofit, OkHttp, kotlinx.serialization, Coroutines, JUnit 5 (android-junit5 plugin), Turbine, MockWebServer, Konsist, networknt json-schema-validator, OpenAPI Generator plugin (research R10, R11)
- [ ] T025 [US1] Create `android/app/build.gradle.kts`: `namespace` and `applicationId` `com.recbook.hellopos`, `minSdk` 26, `compileSdk` and `targetSdk` 36, Java/Kotlin toolchain 21, Compose enabled, `buildConfig` enabled, Hilt + KSP, JUnit 5 for unit tests (depends on T024)
- [ ] T026 [US1] Create the Android app shell: `android/app/src/main/AndroidManifest.xml` (INTERNET permission, `HelloPosApplication` annotated `@HiltAndroidApp`, `MainActivity` launcher), `android/app/src/main/java/com/recbook/hellopos/HelloPosApplication.kt`, `MainActivity.kt` (`@AndroidEntryPoint`, sets Compose content), `core/designsystem/Theme.kt` (Material 3 theme), and package directories `core/network`, `core/util`, `data/remote`, `data/repository`, `domain/model`, `feature/start`, each kept with an empty `.gitkeep` until a later task adds its first file (depends on T025)
- [ ] T027 [US1] Run `./gradlew assembleDebug testDebugUnitTest` in `android/` and confirm T015 passes; ensure `android/local.properties` is git-ignored (depends on T026)

**Checkpoint**: three apps build and test from the command line. MVP of this feature.

---

## Phase 4: User Story 2 - See that all three apps share one contract (Priority: P2)

**Goal**: `GET /health` is defined in the contract, implemented by the API, checked against the
contract, and shown on the web and Android start screens.

**Independent Test**: quickstart.md sections 3 and 4.

### Tests for User Story 2 ⚠️ (write first, see them fail)

- [ ] T028 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Contract/OpenApiContract.cs`: loads `contracts/openapi.yaml` (path resolved from the repository root found by walking up to `global.json`) with Microsoft.OpenApi and exposes the response schema for an `operationId` and status code; add Microsoft.OpenApi and JsonSchema.Net versions to `Directory.Packages.props` (research R5)
- [ ] T029 [US2] Write `backend/tests/HelloPos.Api.Tests/Features/Health/GetHealthContractTests.cs`: calls `GET /health` and asserts status `200`, content type `application/json`, and that the body validates against the `HealthStatus` schema from the contract, with `additionalProperties: false` enforced (depends on T028)
- [ ] T030 [P] [US2] Write `backend/tests/HelloPos.Api.Tests/Features/Health/GetHealthTests.cs`: `service` is `hello-pos-api`; `status` is `available`; `version` is non-empty; `serverTimeUtc` ends in `Z` and is within 5 seconds of the test's UTC clock; the endpoint needs no authentication
- [ ] T031 [P] [US2] Write `web/tests/HelloPos.Web.Tests/Pages/StartPageTests.cs` (bUnit, fake health service): shows a progress indicator while checking; shows "available" and the version on success; shows "API unavailable" and a retry button on timeout, network error, non-200, or invalid body (data-model.md, client view state)
- [ ] T032 [P] [US2] Write `android/app/src/test/java/com/recbook/hellopos/feature/start/StartViewModelTest.kt` (JUnit 5, Turbine, MockWebServer): states go Checking → Available(status, version) for a valid `200`; Checking → Unavailable for `500`, malformed JSON, connection refused, and a response delayed past the 5-second timeout; retry re-runs the check
- [ ] T033 [P] [US2] Write `android/app/src/test/java/com/recbook/hellopos/architecture/DomainRulesTest.kt` (Konsist): no file in package `com.recbook.hellopos.domain..` imports `android.*` or `androidx.*` (FR-010)

### Implementation for User Story 2: API

- [ ] T034 [US2] Implement the slice `backend/src/HelloPos.Api/Features/Health/GetHealth/`: `GetHealthResponse.cs` (record with `Service`, `Version`, `Status`, `ServerTimeUtc`; JSON camelCase; `Status` serialized as lowercase `available`/`degraded`; time as UTC ISO 8601 with `Z`), `GetHealthEndpoint.cs` (`MapGetHealth` extension: `GET /health`, anonymous, `WithName("getHealth")`), using `TimeProvider` for the clock and the assembly informational version, defaulting to `0.1.0-dev`; call `MapGetHealth` from `Program.cs` (makes T029, T030 pass)
- [ ] T035 [US2] Add `backend/src/HelloPos.Api/Infrastructure/Cors.cs`: CORS policy whose allowed origins come from configuration key `Cors:AllowedOrigins`; set `http://localhost:5090` in `backend/src/HelloPos.Api/appsettings.Development.json`; register in `Program.cs`

### Implementation for User Story 2: web

- [ ] T036 [US2] Generate the Kiota client into `web/src/HelloPos.Web/ApiClient/` from `contracts/openapi.yaml` (language CSharp, class `HelloPosApiClient`, namespace `HelloPos.Web.ApiClient`) via `scripts/dotnet.ps1 tool restore` then `scripts/dotnet.ps1 kiota generate ...`; record the exact command in `web/src/HelloPos.Web/ApiClient/README.md`; add the Kiota bundle package to `Directory.Packages.props` (research R6)
- [ ] T037 [US2] Add `web/src/HelloPos.Web/Services/HealthService.cs` (`IHealthService` returning a `HealthCheckResult` of Available(status, version) or Unavailable) wrapping the Kiota client with a 5-second timeout; register the client and service in `Program.cs` with the base address from configuration key `Api:BaseUrl`; set `Api:BaseUrl` to `http://localhost:5080/` in `web/src/HelloPos.Web/wwwroot/appsettings.Development.json` and leave it as a placeholder in `wwwroot/appsettings.json` (no secrets, FR-011)
- [ ] T038 [US2] Add `web/src/HelloPos.Web/Pages/Start.razor` at route `/`: MudBlazor card showing Checking / Available (status, version) / "API unavailable" with a Retry button, driven by `IHealthService` (makes T031 pass)

### Implementation for User Story 2: Android

- [ ] T039 [US2] Configure the OpenAPI Generator plugin in `android/app/build.gradle.kts`: input `../../contracts/openapi.yaml`, generator `kotlin`, library `jvm-retrofit2`, `serializationLibrary=kotlinx_serialization`, package `com.recbook.hellopos.data.remote.generated`, output under `build/generated/openapi`, added as a source set and run before Kotlin compilation (research R7)
- [ ] T040 [US2] Add `BuildConfig.API_BASE_URL` in `android/app/build.gradle.kts` from Gradle property `hellopos.apiBaseUrl` (read from `local.properties` if present), default `http://10.0.2.2:5080/` for debug; add `android/app/src/debug/res/xml/network_security_config.xml` allowing cleartext only for `10.0.2.2`, `localhost` and `127.0.0.1`, referenced from a debug-only manifest `android/app/src/debug/AndroidManifest.xml` (FR-011)
- [ ] T041 [P] [US2] Add `android/app/src/main/java/com/recbook/hellopos/domain/model/HealthStatus.kt` (`service`, `version`, `status`, `serverTimeUtc`; pure Kotlin)
- [ ] T042 [US2] Add `android/app/src/main/java/com/recbook/hellopos/core/network/NetworkModule.kt` (Hilt module: OkHttp with 5-second call timeout, Retrofit with kotlinx.serialization converter and `BuildConfig.API_BASE_URL`, the generated health API) and `android/app/src/main/java/com/recbook/hellopos/data/repository/HealthRepository.kt` mapping the generated response to `HealthStatus` and every failure to a typed unavailable result (no swallowed errors, Principle IX) (depends on T039, T040, T041)
- [ ] T043 [US2] Add `android/app/src/main/java/com/recbook/hellopos/feature/start/StartUiState.kt`, `StartViewModel.kt` (`@HiltViewModel`, `StateFlow<StartUiState>`, check on init, `retry()`), and `StartScreen.kt` (Material 3: progress, status and version, or "API unavailable" with Retry); show `StartScreen` from `MainActivity` (makes T032 pass) (depends on T042)

**Checkpoint**: quickstart.md sections 3 and 4 pass; all three apps share one contract.

---

## Phase 5: User Story 3 - Shared building blocks are in place (Priority: P3)

**Goal**: a shared kernel library used by the API and the web app, and the shared pricing-case
file read and validated by both the .NET and Android test suites.

**Independent Test**: quickstart.md section 5, plus the kernel dependency rule test.

### Tests for User Story 3 ⚠️ (write first, see them fail)

- [ ] T044 [P] [US3] Write `backend/tests/HelloPos.Kernel.Tests/KernelDependencyTests.cs`: the `HelloPos.Kernel` assembly references no assembly whose name starts with `Microsoft.AspNetCore`, `Microsoft.JSInterop`, `MudBlazor`, `HelloPos.Api` or `HelloPos.Web` (FR-012)
- [ ] T045 [P] [US3] Write `backend/tests/HelloPos.Kernel.Tests/Pricing/PricingCasesFileTests.cs` (JsonSchema.Net): `contracts/pricing-cases.json` exists, parses, and validates against `contracts/pricing-cases.schema.json`; on failure the message names the file and the violation (FR-014)
- [ ] T046 [P] [US3] Write `android/app/src/test/java/com/recbook/hellopos/domain/pricing/PricingCasesFileTest.kt` (networknt json-schema-validator): same assertions as T045, reading both files from the path given by system property `hellopos.contractsDir`; configure that property for unit tests in `android/app/build.gradle.kts` as `rootProject.projectDir/../contracts` (FR-014)

### Implementation for User Story 3

- [ ] T047 [US3] Create `backend/src/HelloPos.Kernel/HelloPos.Kernel.csproj` (class library, no framework references) with a single `AssemblyInfo.cs` describing its purpose; create `backend/tests/HelloPos.Kernel.Tests/HelloPos.Kernel.Tests.csproj` (xUnit v3, JsonSchema.Net) referencing it; add both to `backend/HelloPos.slnx` (makes T044 and T045 pass)
- [ ] T048 [US3] Reference `backend/src/HelloPos.Kernel` from `backend/src/HelloPos.Api/HelloPos.Api.csproj` and from `web/src/HelloPos.Web/HelloPos.Web.csproj` (relative project path), and add the kernel project to `web/HelloPos.Web.slnx`; both solutions still build and test (FR-012)
- [ ] T049 [US3] Run the Android unit tests and confirm T046 passes; then run quickstart.md section 5 (malformed case rejected by both suites) and revert the change

**Checkpoint**: all three user stories pass independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Update the README's "Building" section (FR-015): prerequisites (Podman or Docker, Android Studio 2026.1+ with JDK 21, `JAVA_HOME`), the container wrapper, the build/test/run commands for each app, the Kiota regeneration command, the Android API address settings for emulator and physical device
- [ ] T051 [P] Update `.gitignore` for anything the build produced that is not yet ignored (`.artifacts/`, `android/.kotlin/`, `*.iml`, `.idea/` local files, `web/**/wwwroot/appsettings.Local.json` if introduced)
- [ ] T052 Run every section of `specs/001-repository-skeleton/quickstart.md` end to end, including the manual start-screen checks on web and an emulator, and record the results in `specs/001-repository-skeleton/checklists/quickstart-results.md`
- [ ] T053 Re-check the plan's Constitution Check against the delivered code and note any deviation in `specs/001-repository-skeleton/plan.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: after Setup; blocks all user stories.
- **US1 (Phase 3)**: after Foundational.
- **US2 (Phase 4)**: after US1 (needs the three app projects to exist).
- **US3 (Phase 5)**: after US1. Independent of US2 and can run alongside it, except T048, which
  edits `HelloPos.Web.csproj` and must come after T036 (which edits the same file).
- **Polish (Phase 6)**: after all stories.

### Within each story

- Test tasks first; confirm they fail.
- Backend: contract helper (T028) → contract test (T029) → slice (T034).
- Android: generator (T039) and config (T040) → network and repository (T042) → ViewModel and screen (T043).

### Parallel opportunities

- Setup: T002–T008 in parallel.
- US1: T013–T016 (tests) in parallel; T017/T019, T021/T022 and T024 tracks in parallel.
- US2: T030–T033 in parallel; the API (T034–T035), web (T036–T038) and Android (T039–T043) tracks in parallel once their tests exist.
- US3: T044–T046 in parallel.

## Parallel Example: User Story 2

```text
Task: "T030 GetHealthTests in backend/tests/HelloPos.Api.Tests/Features/Health/GetHealthTests.cs"
Task: "T031 StartPageTests in web/tests/HelloPos.Web.Tests/Pages/StartPageTests.cs"
Task: "T032 StartViewModelTest in android/app/src/test/java/com/recbook/hellopos/feature/start/StartViewModelTest.kt"
Task: "T033 DomainRulesTest in android/app/src/test/java/com/recbook/hellopos/architecture/DomainRulesTest.kt"
```

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup, Phase 2 Foundational.
2. Phase 3 (US1): three apps build and test.
3. Stop and validate with quickstart.md section 1.

### Incremental delivery

1. US1: skeleton builds → commit.
2. US2: health check through the contract → commit.
3. US3: kernel and shared pricing file → commit.
4. Polish: README and full quickstart run → open the pull request.

## Notes

- Commit after each phase at least; never commit a failing build to the feature branch's tip.
- Do not add business logic, database access, authentication, compose files, or CI (roadmap #2–#4).
