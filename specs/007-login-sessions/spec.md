# Feature Specification: Login and Sessions

**Feature Branch**: `007-login-sessions`
**Created**: 2026-10-11
**Status**: Draft
**Input**: Roadmap Phase 1, feature #7. Staff sign in on the Android POS with their POS username and PIN
(feature #6), on devices registered to the business from the web back office. PIN guessing is stopped
by lockouts; sessions are held in Android's secure storage, end on sign-out, device revocation,
deactivation, PIN reset and inactivity. Cashiers change their own PIN on the device. Phase 1 is done
when an owner signs up, creates a business and a cashier, registers a device, and the cashier signs
in on Android.

## Clarifications

### Session 2026-10-11

- Q: Can staff sign in while the device is offline? → A: Yes (option A). The device keeps a protected
  copy of the PIN checks for its business's active staff, refreshed whenever it is online; attempt
  limits also apply on the device; deactivations and PIN resets reach the device at its next sync.
- Q: How is a device connected to the business? → A: The web back office shows an 8-character code
  valid for **5 minutes**, with a countdown of the remaining time on screen; it is typed into the
  Android app and works once (option A, 5 minutes instead of 10).
- Q: What happens after inactivity? → A: After 5 idle minutes the device locks: the same person
  re-enters their PIN to continue, or someone else signs in instead (option A).

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Registering, naming, listing and revoking POS devices (web) | Device inventory, printers, kiosk settings |
| PIN sign-in, sign-out, changing one's own PIN (Android) | Biometric sign-in, manager-approval prompts (#14) |
| Device and staff sessions, their end conditions | Offline catalog (#13), selling (#14), sync of sales (#15) |
| A signed-in home screen showing who is signed in and the business | Every screen behind it (#14 onward) |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a POS device (Priority: P1)

An owner or manager, in the web back office with a business open, registers a new POS device for that
business: they name it ("Front till") and connect the Android device to the business. The device then
belongs to that business until it is revoked, and is listed in the back office with when it was last
used.

**Why this priority**: no one can sign in on the POS without a registered device; it is the gate that
makes short PINs safe.

**Independent Test**: register a device from the web, complete the connection on Android, and see it
listed as active on the web; an unregistered device cannot reach the sign-in step.

**Acceptance Scenarios**:

1. **Given** an owner or manager with a business open, **When** they register a device and complete
   the connection on Android, **Then** the device is listed as active for that business and the
   Android app shows the PIN sign-in screen for that business.
2. **Given** a connection attempt that is wrong, expired or already used, **When** it is completed on
   Android, **Then** the device is not registered and the app says why.
3. **Given** a cashier, **When** they try to register a device, **Then** they cannot (no back office).
4. **Given** a registered device, **When** an owner or manager revokes it, **Then** it can no longer
   be used for sign-in and anyone signed in on it is signed out at their next action.
5. **Given** two businesses, **When** the owner of one lists devices, **Then** they see only that
   business's devices.

---

### User Story 2 - Sign in with PIN, sign out, shift change (Priority: P1)

On a registered device, a staff member chooses or types their POS username and enters their PIN, and
lands on a home screen showing their name, role and the business. At the end of a shift they sign out
and the next person signs in on the same device quickly.

**Why this priority**: this is the roadmap's Phase 1 done-when, and every later POS feature needs a
signed-in person.

**Independent Test**: on a registered device, sign in as a cashier with the right PIN and see the
home screen; sign out; sign in as another person; a wrong PIN shows the same generic error as an
unknown username.

**Acceptance Scenarios**:

1. **Given** a registered device and an active staff member, **When** they sign in with the right
   username and PIN, **Then** they see the home screen with their name, role and business.
2. **Given** a wrong PIN or an unknown username, **When** someone signs in, **Then** the same message
   is shown ("username or PIN is incorrect") and nothing reveals which part was wrong.
3. **Given** five wrong PINs in a row for one person, **When** a sixth is tried, **Then** that person
   is locked out for 15 minutes on every device of the business, and the screen says so.
4. **Given** many wrong attempts on one device across different usernames, **When** they continue,
   **Then** the device is slowed down (a growing wait between attempts) so guessing is impractical.
5. **Given** a signed-in person, **When** they sign out, **Then** the device returns to the sign-in
   screen and their session cannot be used again.
6. **Given** a deactivated staff member, **When** they try to sign in, **Then** it fails like a wrong
   PIN; **Given** someone signed in who is then deactivated, **Then** they are signed out at their next
   action.
7. **Given** a staff member of another business, **When** they try to sign in on this device, **Then**
   it fails like an unknown username.

---

### User Story 3 - Session safety and changing one's own PIN (Priority: P2)

A signed-in person's session stays valid while they work, is locked after inactivity, and ends when
their PIN is reset by an owner or manager. A cashier changes their own PIN on the device by giving the
current one.

**Why this priority**: a till left unattended must not stay open to anyone walking by; a PIN that was
told to others must be replaceable by its holder.

**Independent Test**: leave a signed-in device idle past the limit and see it lock; reset someone's
PIN on the web and see their session end; change one's own PIN on the device and sign in with the new
one.

**Acceptance Scenarios**:

1. **Given** a signed-in device left idle for 5 minutes, **When** anyone touches it, **Then** it shows
   a lock screen with the signed-in person's name: that person continues by entering their PIN, or
   "Not you?" signs them out and lets someone else sign in. Wrong PINs at the lock screen count towards
   the lockout like any sign-in.
2. **Given** a signed-in person whose PIN is reset on the web, **When** they act on the device,
   **Then** they are signed out and must sign in with the new PIN.
3. **Given** a signed-in cashier, **When** they change their PIN giving the right current PIN and an
   acceptable new one, **Then** only the new PIN works from then on; a wrong current PIN or a weak new
   PIN is refused with a message.
4. **Given** a signed-in session, **When** its credentials are inspected on the device, **Then** they
   are only in the device's secure storage, never in plain settings, logs or screenshots of the app.

### Edge Cases

- The device has no network at shift change: staff can still sign in, checked against the protected
  copy of PIN checks the device keeps for its business's active staff (refreshed whenever online);
  the same attempt limits apply on the device. Once back online the device reports offline sign-ins
  and refreshes its copy; anyone deactivated or whose PIN was reset in the meantime is signed out then.
- A device that has never been online since registration has no copy yet: offline sign-in is not
  possible until its first refresh (registration itself needs a connection).
- A device is revoked while offline: the revocation takes effect as soon as it reconnects; nothing
  signed in afterwards is accepted by the server.
- The app is reinstalled or its data cleared: the device must be registered again; the old
  registration stays listed until revoked.
- Two devices try to complete the same connection: only the first succeeds.
- A person is locked out on one device: the lock applies to their sign-in on every device of the
  business; other people can still sign in.
- The owner works the till: an owner with a POS username and PIN signs in like anyone else; back-office
  rights stay on the web.

## Requirements *(mandatory)*

### Functional Requirements

**Devices**

- **FR-001**: An owner or manager with a business open MUST be able to register a POS device for that
  business, give it a name, list the business's devices (name, registered by, registered at, last
  used, active or revoked) and revoke any of them.
- **FR-002**: Connecting a device MUST use a single-use code started from the web back office: the
  web shows an 8-character code (letters and digits that are not easily confused) valid for 5 minutes,
  with a visible countdown of the remaining time; it is typed into the Android app and works once. An
  expired or used code is refused; the owner can generate a new one. Codes are rate limited.
- **FR-003**: A device belongs to exactly one business. A revoked device MUST NOT be able to sign
  anyone in or keep a session; re-registering it creates a new registration.

**Sign-in**

- **FR-004**: On a registered device, staff MUST sign in with their POS username and PIN; only active
  staff of the device's business can sign in.
- **FR-005**: A failed sign-in MUST give the same answer whether the username is unknown, belongs to
  another business, the PIN is wrong, or the person is inactive.
- **FR-006**: Five consecutive failed PINs for one person MUST lock that person's PIN sign-in for 15
  minutes on all devices of the business; a successful sign-in resets the count. Repeated failures on
  one device MUST add a growing delay before the next attempt on that device.
- **FR-007**: Signing out MUST end the session on the server, not only on the device.

**Sessions**

- **FR-008**: A session MUST be tied to one person, one device and one business, and MUST end when:
  the person signs out, the device is revoked, the person is deactivated, the person's PIN is reset
  (sessions started before the reset end), or the session's maximum lifetime (12 hours) passes.
- **FR-009**: The server MUST check on every request that the session, device and person are still
  valid, and MUST apply the person's current role (feature #6 rules), never a role remembered by the
  device.
- **FR-010**: Session secrets MUST be stored only in the device's secure storage and MUST be
  replaced regularly while in use, so a copied old secret stops working (Constitution VIII).
- **FR-011**: After 5 minutes without use the device MUST lock to a screen showing the signed-in
  person; their PIN unlocks it, or "Not you?" signs them out (User Story 3, scenario 1).

**Offline sign-in**

- **FR-011a**: While online, a registered device MUST keep a protected copy of what it needs to check
  PINs of its business's active staff (never the PINs themselves), refreshed at least every 15 minutes
  and after any staff change it is told about. With no connection, staff MUST be able to sign in and
  unlock against that copy, with the same lockout rules applied on the device.
- **FR-011b**: Offline sign-ins MUST be reported to the server when the device reconnects; sessions of
  people deactivated or whose PIN was reset in the meantime MUST end then.

**Own PIN**

- **FR-012**: A signed-in person MUST be able to change their own PIN by giving the current one; the
  new PIN follows feature #6 rules; afterwards their other sessions end.

**Isolation and audit**

- **FR-013**: Devices and sessions of one business MUST be invisible and unusable from any other
  business; every device endpoint has an isolation test (feature 003 harness).
- **FR-014**: Device registrations, revocations, sign-ins, failed sign-ins, lockouts and own-PIN
  changes MUST be recorded with who, which device and when, never with a PIN or session secret.

### Key Entities

- **POS device**: a device registered to one business: name, who registered it, when, last used,
  active or revoked.
- **Device connection request**: a single-use, short-lived step that links a physical device to a
  business.
- **Staff session**: one person signed in on one device for one business; started at, last active,
  ends at; ended by sign-out, revocation, deactivation, PIN reset or lifetime.
- **Sign-in attempt record**: failed and successful sign-ins per person and device, for lockouts and
  audit (including those made offline, reported on reconnect).
- **Offline PIN check data**: per device, the protected data that lets it check PINs of active staff
  without a connection; never the PINs; replaced on every refresh.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The roadmap's Phase 1 flow works end to end: an owner signs up, creates a business and a
  cashier, registers a device, and the cashier signs in on Android, in under 10 minutes for a person
  doing it the first time.
- **SC-002**: A shift change (sign out, next person signs in) takes under 15 seconds.
- **SC-003**: Guessing a 4-digit PIN by trying is impractical: after 5 wrong tries the person is locked
  for 15 minutes, so at most 20 guesses per hour per person.
- **SC-004**: Revoking a device, deactivating a person or resetting a PIN ends the affected sessions at
  their next request (0 requests accepted after the change).
- **SC-005**: In isolation tests, 0 devices or sessions of another business are ever listed, used or
  changed.
- **SC-006**: No PIN or session secret ever appears in any response other than the one that issues it,
  in any log, or in plain storage on the device.
- **SC-007**: With the network down, a shift change still works (sign out, next person signs in) for
  staff who were active at the device's last refresh.

## Assumptions

- The Android app keeps one registration per installation; one business per device.
- Owners and managers may also have a POS username and PIN (feature #6) and sign in on the till like
  cashiers; back-office administration stays on the web.
- Server-side lockout and session checks follow feature #5's patterns (generic errors, lockouts, rate
  limits, revalidation on every request).
- The home screen is a placeholder for the selling screen (#14).
