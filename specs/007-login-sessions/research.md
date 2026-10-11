# Research: Login and Sessions

Builds on feature 003 (tenancy), 005 (back-office accounts, rate limits, problem details) and 006
(staff, PINs, `ICurrentStaff`, `StaffCredentials`). No new backend packages. Android adds only
AndroidX libraries listed in R9.

## R1. Registering a device: one-time code, then a device credential

- **Web**: an owner or manager (business open) asks for a code: `POST /devices/pairings`. The API
  creates `pos.device_pairings` (business-owned): code hash, expires at (now + 5 min), created by. The
  code is 8 characters from a 31-character alphabet without look-alikes (`23456789ABCDEFGHJKMNPQRSTUVWXYZ`,
  no 0/O/1/I/L), shown as `ABCD-EFGH` with a countdown (spec FR-002). 31^8 ≈ 8.5×10^11 combinations,
  5-minute life, single use and the per-IP rate limit make guessing impractical.
- **Android**: the person types the code and a device name. `POST /pos/pair` (anonymous,
  rate-limited) sends the code and the device's public key (R5). As with invitations (006 R7), the
  business is found from the secret, never from the request: `SECURITY DEFINER` function
  `pos.pairing_business(code_hash)` returns the business of an unused, unexpired code; the API then
  acts as that business, marks the code used and creates `pos.devices`.
- **Answer**: a **device credential** `dev_<businessId>.<deviceId>.<secret>` (32 random bytes;
  only its SHA-256 is stored) and the business name. The device keeps it encrypted (R5) and sends it as
  `Authorization: Device <credential>` on device-level calls (sign-in, refresh, offline data).
- **Why the business id is in the credential**: the API reads it to choose which business to act as,
  then accepts the request only if the secret's hash matches that business's device row (constant-time
  compare). An attacker changing the id gets a non-matching row: 401. So the business comes from an
  authenticated secret, the same rule as tokens (Constitution III).
- **Revoking** sets `revoked_at`; every device credential and session check reads it.
- **Alternatives**: device shows a code typed on the web (rejected in clarification); owner signs in on
  the device (owner password typed on a shared device).

## R2. Staff sessions: opaque tokens checked on every request

- **Decision**: opaque random tokens, not JWTs. A staff session row (`pos.staff_sessions`,
  business-owned) holds the person, device, started at, last active, absolute expiry (started + 12 h),
  ended at and reason, and the hashes of the current access and refresh tokens.
  - Access token `pat_<businessId>.<sessionId>.<secret>`, valid 15 minutes.
  - Refresh token `prt_<businessId>.<sessionId>.<secret>`, rotated on every use, valid until the session
    ends; refreshing also needs the device credential.
- **Why opaque**: the spec ends sessions on revocation, deactivation and PIN reset at the next request
  (SC-004), so every request is checked against the database anyway (as 005/006 do); a JWT would add
  key management and still need the lookup.
- **Every request** (authentication handler `PosSession`): parse the token, act as its business, load
  the session, check the hash, `ended_at is null`, access expiry, absolute expiry, device not revoked,
  staff active, and `session.started_at >= staff.pin_changed_at` (a PIN reset or own change ends older
  sessions). Then the principal gets claims `business_id`, `staff_id`, `device_id`, `session_id`;
  `ICurrentBusiness` already reads `business_id`, and `ICurrentStaff` (006) learns `staff_id`, so
  `RequireRole` works unchanged for POS calls.
- **Refresh reuse**: presenting a refresh token that was already rotated ends the whole session (it was
  copied), per OAuth 2.0 security best current practice.
- **Last active** is updated at most once a minute per session (no write per request).
- **Alternatives**: JWT access + DB refresh (stale access tokens for up to their lifetime, conflicts
  with SC-004); cookies (not suited to a native app).

## R3. Guessing PINs online

- **Per person**: `staff_members.pin_failed_count`, `pin_locked_until`. Five consecutive failures lock
  PIN sign-in for 15 minutes on every device of the business (FR-006); success resets the count.
  The server answers wrong, unknown, inactive and locked with the same generic 401, so a lock reveals
  nothing about which usernames exist. The device shows "too many attempts, try again in 15 minutes"
  from its own count of failures for the username typed, which it knows anyway.
- **Per device**: `devices.failed_signins` and `next_attempt_at`: after 3 consecutive failures (any
  username) each further attempt waits 2^(n−3) seconds, capped at 5 minutes; success resets. This stops
  trying many usernames with common PINs.
- **Per address**: the existing `owner-auth` rate limit also covers `/pos/pair`, `/pos/sign-in`,
  `/pos/refresh`.

## R4. Offline sign-in (clarification Q1): what the device holds and how it is protected

- **What the device can check offline**: the staff member's existing PIN hash (Identity's PBKDF2-SHA512
  format, already stored by 006) plus username, display name, role, `pin_changed_at`. Kotlin verifies
  that format with the platform's PBKDF2 (same iteration count read from the hash header). No second
  copy of PINs exists; the server never needs the PIN.
- **Delivery**: `GET /pos/offline-access` (device credential) returns the list for active staff,
  **encrypted to the device's public key** (R5): only that device's hardware-held private key can
  decrypt it. Refreshed every 15 minutes while online and on every sign-in.
- **At rest**: the bundle stays encrypted under the device key; decrypted only in memory to check a PIN.
- **Lockout offline**: the same 5-failures / 15-minute rule per person, and the device back-off, kept
  in encrypted local state; failures are reported to the server on reconnect and merged into the
  server counters.
- **Residual risk (accepted, documented)**: someone who steals a device, roots it and uses its key to
  decrypt the bundle can try all 10^4–10^6 PINs against the PBKDF2 hashes without online limits (hours,
  not years). Mitigations: hashes are slow, the bundle holds only active staff of that business, the
  owner revokes the device (its credential and sessions die at once; the stolen data cannot sign in to
  the server), and staff should not reuse their till PIN elsewhere. Stronger protection (hardware-rate-
  limited PIN checks) is not available to apps on Android; recorded for the security review (#26).
- **Coming back online**: offline sign-ins become real sessions with `POST /pos/sessions/offline`
  (device credential + staff id + when + the `pin_changed_at` the device checked against). The API
  accepts only if the person is still active and their PIN has not changed since that value; otherwise
  the device signs them out. The device itself is trusted here because it is authenticated and already
  held the data; the audit log records the session as "offline sign-in".

## R5. Keys and secret storage on Android

- At registration the app creates in **Android Keystore** (StrongBox when present):
  - an RSA-OAEP (3072) key pair for receiving the offline bundle (public key sent at pairing);
  - an AES-GCM-256 key for encrypting everything stored at rest.
  Keys are non-exportable; deleted with the app's data (which forces re-registration).
- Device credential, access and refresh tokens, the offline bundle and local lockout state are written
  as AES-GCM-encrypted files in app-private storage; never in plain SharedPreferences, logs, intents or
  crash reports (Constitution VIII). Screens set `FLAG_SECURE` (no screenshots, hidden in recents).
- **Alternatives**: Jetpack Security `EncryptedSharedPreferences` (deprecated); DataStore + Tink (extra
  dependency for the same result).

## R6. Inactivity lock (clarification Q3)

- The app tracks the last user interaction; after 5 idle minutes it shows the lock screen (person's
  name, PIN pad, "Not you?"). Unlocking checks the PIN against the offline bundle (works with or without
  a network; failures count like sign-ins). "Not you?" signs out (server-side if online). The server
  session itself is not ended by the idle lock; it ends by sign-out, the 12-hour limit or the events in
  FR-008.

## R7. Changing one's own PIN

- `PUT /pos/me/pin` (staff session) with current and new PIN uses 006's `StaffCredentials.ChangeOwnPin`;
  on success `pin_changed_at` moves, so the person's *other* sessions end; the current session gets new
  tokens in the response. Needs a connection (it changes server data).

## R8. Contract and web

- New operations (see `contracts/openapi-additions.yaml`): devices (pairing code, list, rename, revoke)
  for the back office; `/pos/*` for the device (pair, sign-in, refresh, sign-out, me, own PIN, offline
  access, offline sessions, attempt reports). New security schemes `deviceCredential` and
  `staffSession` (HTTP bearer-style).
- Web: `Devices.razor`: list, "Register a device" shows the code `ABCD-EFGH` large with a live
  countdown (mm:ss) and a "New code" button when it expires; revoke with confirmation. Owners and
  managers.

## R9. Android structure and dependencies

- Feature packages `feature/pairing`, `feature/signin` (username + PIN pad), `feature/home`
  (placeholder: name, role, business, sign out, change PIN), `feature/lock`; `core/security`
  (Keystore, encrypted files), `core/session` (token store, OkHttp interceptor + authenticator for
  refresh, idle tracker), `data/auth` (repository over the generated client).
- Navigation: `androidx.navigation:navigation-compose` 2.10.2 (new, pinned; latest stable 2026-10-11). No Room or WorkManager yet (the
  offline refresh runs on the app's process scope while the app is open; the POS app is in the
  foreground during a shift).
- Tests: JUnit 6 unit tests for the token store, PIN-hash verifier (against hashes produced by the .NET
  `PasswordHasher` in shared vectors `contracts/pin-hash-cases.json`), lockout and back-off rules,
  view models (Turbine); MockWebServer for refresh and reuse handling.

## R10. Shared PIN-hash test vectors

- `contracts/pin-hash-cases.json`: PINs and their Identity v3 hashes, generated once by a .NET test
  and checked by both suites (the .NET side verifies them; Kotlin must verify the same and reject wrong
  PINs), so the two implementations cannot drift (the same idea as `pricing-cases.json`).
