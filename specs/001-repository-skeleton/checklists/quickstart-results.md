# Quickstart Results: Repository Skeleton

**Run**: 2026-10-10, development machine (Windows 11, Podman 5.4, .NET SDK 10.0.401 in container,
Android Studio 2026.1.2 JDK 21)
**Guide**: [quickstart.md](../quickstart.md)

| § | Check | Result |
|---|-------|--------|
| 1 | Backend build + tests | ✅ 10/10 passed (API 7, kernel 3) |
| 1 | Web build + tests | ✅ 10/10 passed |
| 1 | Android `assembleDebug testDebugUnitTest` | ✅ 10/10 passed, debug APK built |
| 2 | Wrong toolchain rejected | ✅ by `global.json` (`allowPrerelease: false`); the container SDK 10.0.401 is accepted |
| 3 | `GET /health` against the running API container | ✅ four contract fields only, `status: available`, UTC time ending in `Z` |
| 3 | CORS | ✅ `http://localhost:5090` allowed; other origins get no CORS headers |
| 3 | Web start screen in a browser | ⏳ **pending, needs the developer** (bUnit tests cover all three states) |
| 3 | Android start screen on an emulator or device | ⏳ **pending, needs the developer** (ViewModel tests cover all states against a real HTTP stack) |
| 3 | API stopped: both clients show "API unavailable" | ⏳ pending with the two checks above (covered by automated tests) |
| 4 | Contract drift caught | ✅ test proves the contract rejects undeclared fields; any extra or renamed field in `/health` fails `GetHealth_ResponseConformsToContract` |
| 5 | Malformed pricing case rejected | ✅ real file broken by hand: .NET and Kotlin suites both failed naming the file; restored. Also covered permanently by in-memory negative tests |
| 6 | Logs with trace ids | ✅ console JSON event for `GET /nope` carried `@tr` equal to the response `traceId`; Seq itself not started in this run |
| 7 | Kiota regeneration | ✅ generated through the container wrapper |

## Issues found during the run

- Gradle reused stale Android test results after `contracts/` changed. Fixed: `contracts/` is a
  declared input of the test task.
- Killing the wrapper script from outside leaves its container running. Documented in the README
  (stop with `podman stop`); Ctrl+C in a terminal stops it normally.
