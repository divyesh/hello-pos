# Quickstart: Repository Skeleton

How to prove feature 001 works. Run from the repository root unless noted. The commands below
are the targets the implementation must make true; the README's "Building" section is the
lasting copy (FR-015).

## Prerequisites

- Podman (default) or Docker. .NET commands run in the official .NET 10 SDK container through
  `scripts/dotnet.ps1` / `scripts/dotnet.sh` (research R13); no local .NET SDK is needed. To use
  Docker, set `CONTAINER_ENGINE=docker`.
- Android Studio 2026.1 or newer, which provides JDK 21 and the Android SDK (platform 36).
  Set `JAVA_HOME` to its bundled JDK (`<Android Studio>/jbr`) for command-line Gradle builds.
- Android SDK location in `android/local.properties` (`sdk.dir=...`); Android Studio writes this
  on first open.

## 1. Build and test everything (User Story 1, SC-001, SC-002)

```text
scripts/dotnet.ps1 build backend/HelloPos.slnx      # scripts/dotnet.sh on Linux/macOS
scripts/dotnet.ps1 test  backend/HelloPos.slnx
scripts/dotnet.ps1 build web/HelloPos.Web.slnx
scripts/dotnet.ps1 test  web/HelloPos.Web.slnx
cd android
./gradlew assembleDebug        # gradlew.bat on Windows
./gradlew testDebugUnitTest
```

Expected: every build succeeds and every test suite reports zero failures, including:

- the API contract test for `GET /health` (R5),
- the kernel dependency rule test (FR-012),
- the pricing-case file schema test, in both the .NET and Android suites (FR-014),
- the Android `domain` import rule test (FR-010),
- start-screen state tests on web (bUnit) and Android (ViewModel with MockWebServer).

## 2. Wrong toolchain is rejected (User Story 1, scenario 3)

With a local preview or non-10 .NET SDK (for example the preview on the development machine),
`dotnet build backend/HelloPos.slnx` run outside the container fails and names the required SDK
version from `global.json`.

## 3. Three apps, one contract (User Story 2, SC-003)

1. Start the API: `scripts/dotnet.ps1 -Port 5080 run --project backend/src/HelloPos.Api`
   (listens on `http://localhost:5080`, published from the container). Build and test runs publish
   no ports, so they can run while the API and web app are up.
2. Check it directly: `curl http://localhost:5080/health` returns the fields in
   [data-model.md](data-model.md#healthstatus) and nothing else.
3. Start the web back office in a second terminal:
   `scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web`, then open
   `http://localhost:5090`.
   The start screen shows **available** and the version within 5 seconds.
4. Run the Android app on an emulator (`./gradlew installDebug`, then open Hello POS). It uses
   `http://10.0.2.2:5080/` by default and shows the same status and version. On a physical device,
   set `hellopos.apiBaseUrl=http://<your-LAN-IP>:5080/` in `android/local.properties` first.
5. Stop the API. Reopen or retry on both clients: within 10 seconds each shows
   **API unavailable** and stays usable.

## 4. Contract drift is caught (SC-004)

Temporarily add a field to the API's health response (or rename one) without changing
`contracts/openapi.yaml`, then run `scripts/dotnet.ps1 test backend/HelloPos.slnx`: the contract
test fails,
naming the mismatch. Revert the change.

## 5. Malformed pricing cases are rejected (User Story 3, SC-005)

Temporarily add `{"id": "Bad Id"}` to `cases` in `contracts/pricing-cases.json`, then run both the
.NET and Android test suites: both fail, naming the schema violation. Revert the change.

## 6. Logs in Seq (optional until roadmap #2)

1. Start Seq: `podman run -d --name seq -e ACCEPT_EULA=Y -e SEQ_FIRSTRUN_NOAUTHENTICATION=true -p 5341:80 datalust/seq`
   (use `docker` instead of `podman` if preferred; development only, no authentication).
2. Start the API with Seq enabled:
   `$env:Seq__ServerUrl = "http://host.docker.internal:5341"; scripts/dotnet.ps1 -Port 5080 run --project backend/src/HelloPos.Api`
3. Call `curl http://localhost:5080/health` and `curl http://localhost:5080/nope`.
4. Open `http://localhost:5341`: both requests appear as summary events with `TraceId`,
   `Application = hello-pos-api`, status code and elapsed time; the `traceId` in the `404`
   response body matches its event.
5. Without `Seq__ServerUrl`, the API still starts and the same events appear as JSON lines in
   the console.

## 7. Regenerate the web client after a contract change

```text
scripts/dotnet.ps1 tool restore
scripts/dotnet.ps1 kiota generate ...   # exact command documented in the README by the implementation
```

The Android client regenerates automatically on every Gradle build.
