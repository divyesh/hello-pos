# Implementation Plan: Login and Sessions

**Branch**: `007-login-sessions` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

## Summary

POS devices are registered from the web back office with a one-time 8-character code (5 minutes,
countdown) typed on the Android app; pairing gives the device a credential whose business is found from
the code through a narrow `SECURITY DEFINER` lookup, never from request data. On a registered device,
staff sign in with their POS username and PIN (006); the API issues opaque access tokens (15 minutes) and
rotating refresh tokens tied to a 12-hour staff session, checked against the database on every request so
sign-out, device revocation, deactivation and PIN changes end sessions at the next request. Wrong PINs
lock a person for 15 minutes after 5 failures across all devices, and devices back off exponentially.
For offline sign-in the device receives its business's active staff PIN hashes encrypted to a
hardware-held key and verifies them locally with the same lockout rules, converting offline sign-ins into
server sessions on reconnect. The app locks after 5 idle minutes and lets staff change their own PIN.
Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API, web, kernel); Kotlin 2.4.21 (Android)

**Primary Dependencies**: backend and web: existing only. Android: adds
`androidx.navigation:navigation-compose` 2.10.2; uses Android Keystore (platform), OkHttp
interceptor/authenticator, generated Retrofit client.

**Storage**: PostgreSQL 18: new `pos.device_pairings`, `pos.devices`, `pos.staff_sessions`; columns on
`pos.staff_members` (PIN lockout) and `pos.staff_changes` (`device_id`); function
`pos.pairing_business`. Android: AES-GCM-encrypted files under Keystore keys.

**Testing**: xUnit v3 + Testcontainers (app login), isolation harness, contract tests, bUnit;
Android JUnit 6 + Turbine + MockWebServer; shared vectors `contracts/pin-hash-cases.json`.

**Target Platform**: API container, web static site, Android 8.0+ (minSdk 26) tablets and phones.

**Project Type**: full-stack feature (API slices, web page, Android screens, kernel rules).

**Performance Goals**: shift change under 15 s (SC-002); session check one indexed lookup per request.

**Constraints**: business only from authenticated secrets; no secret in plain storage, logs or
responses other than the issuing one; offline sign-in with the same lockout rules.

**Scale/Scope**: ~16 API operations, 1 web page, 5 Android screens, 1 migration, ~90 new tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | No money. | N/A |
| II. Never Lose or Duplicate | Offline sign-in keeps the till usable during outages (cash selling offline arrives in #15). | Pass |
| III. Tenant Isolation | All new tables business-owned with RLS; the business comes only from authenticated secrets (device credential, session token) or a pairing code via a narrow definer function; isolation tests for every device endpoint; POS role checks reuse `ICurrentStaff`. | Pass |
| IV. Append-Only Record | Sessions and devices are ended or revoked, never deleted; all sign-in events in the append-only `staff_changes`. | Pass |
| V. Contract-First | Operations added to the contract first; web Kiota and Android OpenAPI clients regenerated. | Pass |
| VI. Test-First | Tests before implementation per story; shared PIN-hash vectors run by .NET and Kotlin. | Pass |
| VII. Vertical Slices | `Features/Devices/*`, `Features/Pos/*`; Android `feature/*` packages; web talks only to the API. | Pass |
| VIII. Secure by Default | Tokens opaque, hashed at rest, short-lived, rotated with reuse detection; device-bound credentials; lockout and back-off; secrets in Keystore-encrypted storage, `FLAG_SECURE`; offline-bundle residual risk documented (research R4) for the #26 security review. | Pass (with documented residual risk) |
| IX. No Silent Failures | Problem details for every refusal; offline events reported on reconnect, never dropped. | Pass |
| X. Simplicity | Opaque tokens instead of JWT key management; no Room or WorkManager yet; one new Android dependency. | Pass |

Result: **pass**. Re-checked after design: pass.

## Project Structure

### Documentation (this feature)

```text
specs/007-login-sessions/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/openapi-additions.yaml
├── checklists/requirements.md
└── tasks.md                       # /speckit-tasks
```

### Source Code (repository root)

```text
contracts/openapi.yaml                       # + devices and /pos operations, deviceCredential and staffSession schemes
contracts/pin-hash-cases.json                # new: PINs and their Identity v3 hashes (both suites verify)
backend/src/HelloPos.Api/
├── Features/Devices/                        # Device, DevicePairing entities; CreatePairing, List, Rename, Revoke
├── Features/Pos/                            # Pair, SignIn, Refresh, SignOut, Me, ChangeOwnPin,
│                                            # OfflineAccess (bundle encryption), SessionFromOffline, ReportAttempts
├── Infrastructure/Pos/                      # token/credential format and hashing, PosSession and DeviceCredential
│                                            # authentication handlers, StaffSession entity, lockout and back-off
├── Infrastructure/Tenancy/ICurrentStaff.cs  # + staff_id claim (POS sessions)
└── Infrastructure/Persistence/Migrations/   # AddDevicesAndSessions
backend/tests/HelloPos.Api.Tests/Features/Devices/*, Features/Pos/*, Tenancy/DeviceIsolationTests.cs
web/src/HelloPos.Web/Pages/Devices/Devices.razor (+ service, countdown), tests in web/tests/.../Pages/Devices/
android/app/src/main/java/com/recbook/hellopos/
├── core/security/                           # KeystoreKeys, EncryptedFileStore, PinHashVerifier
├── core/session/                            # SessionStore, AuthInterceptor, TokenAuthenticator, IdleTracker
├── data/auth/                               # DeviceRepository, SessionRepository, OfflineAccessRepository
├── feature/pairing/, feature/signin/, feature/lock/, feature/home/, feature/changepin/
└── MainActivity.kt                          # navigation graph, FLAG_SECURE
android/app/src/test/...                     # unit tests per package above
```

**Structure Decision**: two backend slice folders (devices for the back office, pos for the device) plus
POS authentication infrastructure; Android gets its first real features and the security core.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| Offline PIN verification on the device | Clarification Q1: sign-in must work during an outage | Online-only sign-in would stop the till at shift change during an outage |
| Business id inside device credentials and tokens | The API must choose the business before it can find the row under RLS | A global lookup table outside tenancy, or a definer function per request |
| Two authentication schemes for the device | Device-level calls (sign-in, refresh, offline data) vs person-level calls | Reusing the session token for device calls would make sign-in impossible before a session exists |
