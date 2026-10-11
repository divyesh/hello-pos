# Quickstart results: Login and Sessions

**Date**: 2026-10-11

## Automated

| Suite | Command | Result |
|-------|---------|--------|
| Backend | `scripts/dotnet.ps1 test --solution backend/HelloPos.slnx` | 271 passed |
| Web | `scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx` | 73 passed |
| Android | `./gradlew assembleDebug testDebugUnitTest` | 63 passed |
| Contract | `redocly lint contracts/openapi.yaml` | valid |

Fault-injection checks (temporarily broken code, test confirmed failing, code restored): concurrent
token refresh guard (Android); own PIN change ending its own replacement session (found a real bug
through `SessionEndTests`, fixed).

## Manual (emulator): pending

Steps 1–10 of `quickstart.md`, including SC-001 (sign-up to cashier signed in, under 10 minutes) and
SC-002 (shift change under 15 seconds), are to be run by the project owner on an emulator and recorded
here.

| Step | Result | Notes |
|------|--------|-------|
| 1–3 Register a device | | |
| 4 Sign in, lockout, SC-002 timing | | |
| 5 Idle lock | | |
| 6 Change PIN | | |
| 7 PIN reset on the web ends the session | | |
| 8 Offline sign-in and reconnect | | |
| 9 Revoke the device | | |
| 10 SC-001 timing | | |
