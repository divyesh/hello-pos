# Quickstart: Login and Sessions

## Automated

- Backend (`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`): pairing codes (single use,
  5-minute expiry, guessing rate-limited, business found from the code), device credential checks
  (wrong secret, wrong business id, revoked), sign-in (generic 401s, person lockout after 5 failures
  across devices, device back-off), sessions (refresh rotation, reuse ends the session, every end
  condition in FR-008 at the next request), own PIN change, offline access bundle (only active staff,
  only decryptable with the device key), offline sessions (accepted only if still active and PIN
  unchanged), role checks for POS calls, device isolation on the shared harness, no secret in any
  response or log.
- Web (`... web/HelloPos.Web.slnx`): devices page, code with countdown and renewal, revoke.
- Android (`./gradlew testDebugUnitTest`): encrypted store, PIN-hash verification against
  `contracts/pin-hash-cases.json`, lockout and back-off, token refresh and reuse handling
  (MockWebServer), view models for pairing, sign-in, lock and change PIN.

## Manual (local environment, Android emulator)

1. `scripts/env.ps1 up --api`; run the web app; sign in as an owner with a business and a cashier
   (feature 006).
2. Web → Devices → "Register a device": a code like `K7QD-M3XP` and a countdown from 5:00.
3. Emulator (`./gradlew installDebug`, API at `http://10.0.2.2:5080/`): type the code and "Front till".
   The device appears active on the web; the app shows the sign-in screen with the business name.
4. Sign in as the cashier: home shows name, role, business. Time a sign-out and the next sign-in
   (SC-002: under 15 seconds). Five wrong PINs lock that cashier for 15 minutes.
5. Leave the app 5 minutes: lock screen; unlock with the PIN; "Not you?" returns to sign-in.
6. Change PIN on the device; sign out; the old PIN fails, the new one works.
7. Reset the cashier's PIN on the web while signed in on the device: the next action signs them out.
8. Airplane mode: sign out and sign in again (offline); turn the network back on: the session becomes
   a server session; deactivate the cashier on the web while offline, reconnect: they are signed out.
9. Revoke the device on the web: the app returns to "register this device".
10. Done-when (SC-001): from a new owner sign-up to the cashier signed in on Android, under 10 minutes.
