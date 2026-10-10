# Research: Repository Skeleton

Decisions for feature 001. Exact package versions are chosen at implementation time as the latest
stable release that satisfies the constraints here, then pinned exactly (no floating versions).

## R1. .NET SDK pinning

- **Decision**: `global.json` at the repository root pins SDK `10.0.100` with
  `rollForward: latestFeature` and `allowPrerelease: false`. All .NET projects target `net10.0`.
- **Rationale**: any stable .NET 10 feature band builds the repo; a wrong major version or a
  preview SDK fails immediately with the SDK's own "requested SDK version" message (spec, User
  Story 1, scenario 3).
- **Alternatives considered**: exact patch pin (`rollForward: disable`) forces every developer to
  install one specific patch, too rigid; no pin lets .NET 11 previews build the repo silently.
- **Environment note**: the development machine has only a preview .NET 10 SDK locally. Instead of
  installing one, .NET commands run inside the official SDK container (R13); a local stable SDK
  remains optional, for IDE use.

## R14. Logging: Serilog with Seq

- **Decision**: the API logs through Serilog (`Serilog.AspNetCore`). Two sinks only:
  - **Console** with `CompactJsonFormatter`: one JSON event per line on stdout, which the container
    engine and DigitalOcean capture. No file sinks: files inside containers are lost on redeploy
    and can fill the disk.
  - **Seq** (`Serilog.Sinks.Seq`), enabled only when `Seq:ServerUrl` is configured (optional
    `Seq:ApiKey`). Without it the API logs to the console only.

  Every event is enriched with the current activity's `TraceId` and `SpanId`, `Application`
  (`hello-pos-api`) and the environment name. `UseSerilogRequestLogging` writes one summary event
  per request (method, path, status, elapsed time). Feature code logs through `ILogger<T>` only,
  never through Serilog types, so the provider stays replaceable.
- **Local Seq**: the compose environment (roadmap #2) adds a `datalust/seq` container and points
  the API at it. Until then a developer can run Seq by hand (quickstart §6) and set
  `Seq__ServerUrl=http://host.docker.internal:5341`; the wrapper passes that variable through and
  maps the hostname to the host for both Podman and Docker.
- **Production**: Seq on DigitalOcean (a small droplet or container with a persistent volume,
  behind authentication and an ingestion API key), decided with launch work (roadmap #26). Seq's
  free licence covers a single user; more users need a paid licence, so this is revisited before
  launch.
- **Rationale**: the developer chose Seq; Serilog is Seq's native client and gives request
  summaries and enrichment with little code. Structured events with trace ids satisfy Principle IX.
- **Alternatives considered**: the built-in JSON console logger (no Seq integration or request
  summaries without extra code); OpenTelemetry exporting to Seq over OTLP (vendor-neutral and
  adds traces and metrics; worth revisiting with tenancy and API infrastructure in roadmap #3).

## R13. Running .NET in a container

- **Decision**: wrapper scripts `scripts/dotnet.ps1` (Windows) and `scripts/dotnet.sh`
  (Linux/macOS) run `dotnet <args>` inside `mcr.microsoft.com/dotnet/sdk:10.0`, with the
  repository mounted at `/repo` and a named volume for the NuGet cache. Ports are published only on
  request (`-Port 5080` for the API, `-Port 5090` for the web app), so several containers can run
  at once. Container builds write to `.artifacts/container` instead of `bin/`/`obj/`, so they never
  clash with a host IDE build; on Linux/macOS the container runs as the calling user. The engine
  is Podman by default; setting `CONTAINER_ENGINE=docker` uses Docker instead. Verified: the image
  provides SDK 10.0.401 under Podman 5.4.
- **Rationale**: no local SDK install, identical toolchain for every developer and for CI (#4),
  and the constitution forbids assuming Docker Desktop. The `global.json` pin still applies inside
  the container.
- **Alternatives considered**: requiring a local SDK install (the developer prefers containers);
  a dev container definition (useful later for IDEs, more setup than this feature needs).

## R2. Backend solution layout (vertical slices)

- **Decision**: `backend/HelloPos.slnx` (the XML solution format that is the default in .NET 10)
  with:
  - `src/HelloPos.Api`: ASP.NET Core minimal API. Features live in `Features/<Area>/<UseCase>/`,
    each folder holding its endpoint mapping, request/response types and handler. Infrastructure
    shared across slices lives in `Infrastructure/`.
  - `src/HelloPos.Kernel`: class library for shared primitives (empty in this feature). No
    reference to ASP.NET Core, Blazor, or any HelloPos app project.
  - `tests/HelloPos.Api.Tests`: endpoint and contract tests.
  - `tests/HelloPos.Kernel.Tests`: kernel tests, the shared pricing-case file test, and the
    dependency rule test for the kernel.
- **Rationale**: matches Constitution VII (vertical slices; sharing limited to primitives). The
  kernel is the future home of `Money` and pricing, used by both API and web (Constitution I).
- **Alternatives considered**: Clean Architecture's 4-project layering (Domain/Application/
  Infrastructure/Api), which the constitution's Simplicity principle and the vertical-slice choice
  rule out; a single project with no kernel, which cannot be shared with the web app.

## R3. Test framework (.NET)

- **Decision**: xUnit v3 for all .NET test projects; `Microsoft.AspNetCore.Mvc.Testing`
  (`WebApplicationFactory`) for in-memory API tests; bUnit for Blazor component tests.
- **Rationale**: mainstream, well supported on .NET 10, and bUnit is the standard for Blazor
  components. One framework across backend and web.
- **Implementation note**: xUnit v3 (4.x) runs on Microsoft.Testing.Platform, which the .NET 10
  SDK requires opting into: `global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`,
  the VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) are not used, and
  tests run with `dotnet test --solution <file>.slnx`.
- **Alternatives considered**: MSTest (fine, smaller community examples), TUnit (newer, less
  proven).

## R4. Contract format and version

- **Decision**: one hand-written contract, `contracts/openapi.yaml`, in **OpenAPI 3.0.3**.
- **Rationale**: the contract is the source of truth (Constitution V), so it is authored, not
  generated from code. 3.0.3 is fully supported by both client generators chosen below and by
  mock servers (roadmap #2); 3.1 support in client generators is still uneven.
- **Alternatives considered**: OpenAPI 3.1 (better JSON Schema alignment, revisit once the
  generators fully support it); code-first generation from the API (contradicts contract-first).

## R5. Verifying the API against the contract

- **Decision**: a backend test calls each operation through `WebApplicationFactory` and validates
  the actual HTTP status, content type and JSON body against the matching response schema read
  from `contracts/openapi.yaml` (parsed with Microsoft.OpenApi, validated with JsonSchema.Net).
- **Rationale**: tests the real behavior, so any change to a response that the contract does not
  describe fails the build (SC-004).
- **Alternatives considered**: diffing the API's generated OpenAPI document against the contract,
  which is brittle (naming, ordering, nullability differences) and tests the description rather
  than the behavior.

## R6. Web client from the contract

- **Decision**: Microsoft Kiota generates the C# client for the web back office from
  `contracts/openapi.yaml`. Kiota is pinned in a local tool manifest (`.config/dotnet-tools.json`);
  the generated code is committed under `web/src/HelloPos.Web/ApiClient/` and regenerated with one
  documented command. CI (roadmap #4) will regenerate and fail on any difference.
- **Rationale**: Kiota is Microsoft's supported generator, works in Blazor WebAssembly, and
  handles OpenAPI 3.0.3. Committing the output keeps builds fast and offline while the CI check
  prevents drift.
- **Alternatives considered**: NSwag (mature, but its OpenAPI 3.1 path and maintenance pace are
  weaker); a hand-written `HttpClient` wrapper (drifts from the contract).

## R7. Android client from the contract

- **Decision**: the OpenAPI Generator Gradle plugin generates a Kotlin client at build time from
  `contracts/openapi.yaml` (generator `kotlin`, library `jvm-retrofit2`, serialization
  `kotlinx_serialization`) into the build directory (not committed). It sits behind
  `data/remote/`, so the rest of the app depends on our own types.
- **Rationale**: matches the constitution's Android stack (Retrofit, OkHttp,
  kotlinx.serialization); generating on every build means the client cannot drift from the
  contract.
- **Alternatives considered**: hand-written Retrofit interfaces (drift); Kiota (no Kotlin
  generator).

## R8. Health operation

- **Decision**: `GET /health` returns `200` with JSON `{ service, version, status, serverTimeUtc }`;
  `status` is `available` or `degraded`; `serverTimeUtc` is an ISO 8601 UTC timestamp. It is
  anonymous and returns nothing else (FR-005, FR-007). Implemented as the slice
  `Features/Health/GetHealth`. Version comes from the assembly's informational version, defaulting
  to `0.1.0-dev`.
- **Rationale**: the spec needs a JSON body that clients render. ASP.NET Core's built-in
  health-check middleware returns plain text and is aimed at infrastructure probes; it can be added
  later (with the database in #2) without changing this contract.
- **Alternatives considered**: built-in `MapHealthChecks` with a custom JSON writer (couples the
  public contract to probe internals).

## R9. Web back office

- **Decision**: standalone Blazor WebAssembly app `web/src/HelloPos.Web` (`net10.0`) with
  MudBlazor; tests in `web/tests/HelloPos.Web.Tests` (bUnit). The API base URL comes from
  `wwwroot/appsettings.json` and `appsettings.Development.json`, which hold no secrets because
  they are served to the browser. The API allows the web origin through configuration-driven CORS.
  The health call has a 5-second timeout; failures show "API unavailable".
- **Rationale**: Constitution, Technology section; the API remains the only data access path.
- **Alternatives considered**: hosted Blazor (an API project serving the client), rejected because
  the web app is deployed as a static site (Constitution, Hosting).

## R10. Android project

- **Decision**: single `app` module in `android/`, application ID and namespace
  `com.recbook.hellopos`, minSdk 26, targetSdk 36, compileSdk 37 (Compose 1.12 from BOM
  2026.09 requires compiling against API 37; the Android Gradle Plugin installed platform 37
  automatically). Android Gradle Plugin 9 compiles Kotlin itself, so only the Compose compiler
  plugin is applied, not `org.jetbrains.kotlin.android`. Kotlin 2.x
  with Compose (Material 3) via the Compose BOM, Hilt (with KSP), Retrofit, OkHttp,
  kotlinx.serialization, Coroutines. Versions live in the Gradle version catalog
  (`gradle/libs.versions.toml`); the Gradle wrapper is committed; the JVM toolchain is 21 (the JDK
  bundled with Android Studio 2026.1).
- **API address**: `BuildConfig.API_BASE_URL` is set per build type from a Gradle property, with a
  documented default of `http://10.0.2.2:<port>/` for the emulator; a physical device sets the
  developer machine's LAN address in `local.properties`. Plain HTTP is allowed only in debug builds,
  through a debug-only network security configuration.
- **Rationale**: Constitution, Android constraints; FR-011.
- **JDK**: Gradle needs JDK 17+. Inside Android Studio the IDE supplies its bundled JDK 21. For
  command-line builds, `JAVA_HOME` points to that JDK (on this machine
  `C:\Program Files\Android\Android Studio2\jbr`). The old app's JDK (Corretto 1.8 in `~/.jdks`)
  and its tooling (Android Gradle Plugin 4.2.1, Gradle 6.7.1) are too old to reuse; only its SDK
  location (`C:\Users\divye\AppData\Local\Android\Sdk`) carries over.
- **Alternatives considered**: multi-module Gradle setup (deferred until needed; README says single
  module); JDK 17 toolchain (also fine, but 21 is what is installed); building Android in a
  container (heavy image, and emulators and devices still need the host).

## R11. Android tests

- **Decision**: JVM unit tests with JUnit 5 (android-junit5 Gradle plugin), Turbine for flows,
  MockWebServer for the health client, Konsist for the rule that `domain` imports nothing from
  `android.*` or `androidx.*` (FR-010), and networknt json-schema-validator for the pricing-case
  file test.
- **Rationale**: the README's testing stack, and every rule in the spec has an automated check.
- **Alternatives considered**: JUnit 4 (the Android default, but the project standard is JUnit 5);
  splitting `domain` into a pure Kotlin module (stronger enforcement, but more structure than one
  rule needs right now).

## R12. Shared pricing test cases

- **Decision**: `contracts/pricing-cases.json` holds `{ "version": 1, "cases": [] }` and points to
  `contracts/pricing-cases.schema.json` (JSON Schema draft 2020-12) that describes one case: inputs
  (business pricing settings, lines, discounts, tip, payment method) and expected outputs (line
  results, tax lines, totals, rounding, change), with all money in integer minor units. The .NET
  kernel tests and the Android unit tests each load the file and validate it against the schema.
- **Rationale**: Constitution I and VI; proves both suites read the same file before any pricing
  code exists. The `version` field lets the schema evolve in the pricing engine feature (#10).
- **Alternatives considered**: YAML (easier to write but needs a parser on both sides; JSON has
  built-in support everywhere); one file per case (harder to review as a set).
