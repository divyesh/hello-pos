# Feature Specification: Owner Sign-up and Businesses

**Feature Branch**: `005-owner-signup-businesses`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Roadmap #5, Phase 1 (accounts and access): owner sign-up and
businesses on the web back office: owner account, create and edit businesses (country, currency,
tax-inclusive or exclusive, financial-year start, cash rounding step)."

## Clarifications

### Session 2026-10-11

- Q: How do owners sign in? → A: Email and password now; "Sign in with Google" in a later feature
  (FR-002).
- Q: Email verification and password reset? → A: Both in this feature (FR-004, FR-004a, FR-004b).
- Q: Who can sign up? → A: Open self-service (FR-007).

## Scope boundary with later features

- **This feature**: an owner creates an account, signs in to the web back office, creates
  businesses, sees the businesses they own, opens one to work in, and edits its settings.
- **Roadmap #6**: staff (owner, manager, cashier users) inside a business.
- **Roadmap #7**: staff sign-in, the Android app, long-lived sessions with token rotation, device
  registration and revocation. #7 builds on the sign-in introduced here.
- **Roadmap #8–#9**: subscriptions. Every business created here is usable without payment until
  subscriptions exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create an owner account and sign in (Priority: P1)

A shop owner visits the back office, creates an account, and signs in. Signing out ends the session.
Nobody can act on any business without being signed in.

**Why this priority**: every other part of Hello POS needs a known owner.

**Independent Test**: create an account, sign out, sign in again; try wrong credentials and an
existing email; confirm business pages are unreachable while signed out.

**Acceptance Scenarios**:

1. **Given** a visitor, **When** they sign up with a name, email and a password meeting the rules,
   **Then** an owner account is created and a verification link is emailed; after following it the
   owner can create businesses.
5. **Given** an owner who forgot their password, **When** they request a reset, **Then** a single-use
   link is emailed (the page answers the same whether or not the email exists); setting a new
   password through it signs out every other session of that owner.
6. **Given** an owner who has not verified their email, **When** they sign in, **Then** they can only
   resend the verification link; business pages stay closed.
2. **Given** an email already used by an account, **When** someone signs up with it, **Then** sign-up
   fails without revealing whether the email is registered (the same message as any other
   rejection, or a neutral "check your email").
3. **Given** an owner, **When** they sign in with the right email and password, **Then** they reach
   their list of businesses; **When** the password is wrong, **Then** they see a generic
   "email or password is incorrect" and repeated failures are slowed down.
4. **Given** a signed-in owner, **When** they sign out, **Then** the session ends and back-office pages
   require signing in again.

---

### User Story 2 - Create businesses (Priority: P1)

A signed-in owner creates a business with its legal settings: name, country (India, USA or Canada),
currency, tax-inclusive or tax-exclusive pricing, financial-year start month, time zone and cash
rounding step. Country choice pre-fills sensible defaults (for example India: rupees,
tax-inclusive, April, Asia/Kolkata, ₹1 rounding), which the owner can change.

**Why this priority**: nothing can be sold without a business; it is the tenant root of every later
feature.

**Independent Test**: create a business in each country and confirm its settings are stored as
entered; confirm invalid settings are rejected with field-level messages.

**Acceptance Scenarios**:

1. **Given** a signed-in owner, **When** they pick a country, **Then** the form pre-fills that country's
   defaults, all editable.
2. **Given** valid settings, **When** the owner saves, **Then** the business is created, the owner owns
   it, and it appears in their list.
3. **Given** an invalid value (unknown currency, month 13, empty name), **When** the owner saves,
   **Then** nothing is created and each invalid field shows why.
4. **Given** an owner, **When** they create several businesses, **Then** each is separate: settings,
   and every later record, belong to one business only.

---

### User Story 3 - See my businesses, open one, edit its settings (Priority: P2)

After signing in, the owner sees exactly the businesses they own and opens one to work in; the back
office then acts for that business only, and shows which business is open. The owner can switch to
another of their businesses and edit the open business's settings.

**Why this priority**: owners with several shops need to move between them safely; editing settings
is needed before selling starts.

**Independent Test**: two owners each with businesses: confirm each sees only their own; open a
business, edit its settings, switch to another, confirm the edit stayed with the first.

**Acceptance Scenarios**:

1. **Given** owners X and Y with their own businesses, **When** X signs in, **Then** X's list contains
   only X's businesses.
2. **Given** X, **When** X opens one of their businesses, **Then** every page acts for that business and
   the page shows its name.
3. **Given** X, **When** X tries to open or edit one of Y's businesses by any means (link, changed
   identifier), **Then** it is "not found", exactly like a business that does not exist.
4. **Given** an open business, **When** X edits its settings, **Then** the changes are saved for that
   business only; country, once the business has sales, cannot change (not applicable until sales
   exist, so in this feature all settings are editable).
5. **Given** X with two businesses, **When** X switches from one to the other, **Then** the back office
   acts only for the newly opened one.

### Edge Cases

- An owner with no businesses lands on "create your first business".
- A session expires while editing: saving fails with "please sign in again"; nothing is half-saved.
- Two tabs with different open businesses: each request acts for the business of its own session
  context; a tab can never write to a business it did not open (mechanism decided in planning).
- Sign-up spam or credential stuffing: sign-up and sign-in are rate limited per address and per account.
- An owner deletes nothing in this feature: deleting or closing a business is out of scope (it needs
  data retention rules for sales).

## Requirements *(mandatory)*

### Functional Requirements

**Owner accounts**

- **FR-001**: A visitor MUST be able to create an owner account with name, email and password.
- **FR-002**: Owners MUST sign in with email and password. Other methods ("Sign in with Google",
  passkeys) are later features; the account model MUST allow adding external sign-in methods to an
  existing account without changing its identity.
- **FR-003**: Passwords MUST be at least 12 characters, MUST NOT appear in a list of known breached or
  common passwords, and MUST be stored only as a slow, salted hash; no maximum below 64 characters.
- **FR-004**: An owner MUST verify their email address (by a single-use link sent to it) before they
  can create or open a business; an unverified owner can sign in only to resend the link. Verification
  links expire after 24 hours.
- **FR-004a**: "Forgot password" MUST send a single-use reset link that expires after 1 hour; the
  request page always answers the same way whether or not the email is registered (FR-005).
  Completing a reset MUST end all of that owner's existing sessions.
- **FR-004b**: Emails MUST be delivered through a replaceable sending component; locally they are
  captured by a development mail catcher, never sent to real addresses.
- **FR-005**: Sign-up and sign-in responses MUST NOT reveal whether an email is registered.
- **FR-006**: Repeated failed sign-ins MUST be slowed down per account and per client address;
  sign-ups MUST be rate limited per client address.
- **FR-007**: Sign-up MUST be open self-service: anyone can create an owner account; abuse is limited
  by rate limits (FR-006) and email verification (FR-004). Subscriptions (#8–#9) later gate paid use.
- **FR-008**: Sessions MUST expire after inactivity and MUST end on sign-out; the session secret MUST
  never be readable by page scripts or stored in browser storage (Constitution VIII).

**Businesses**

- **FR-009**: A signed-in owner MUST be able to create a business with: name, country (IN, US, CA),
  currency (ISO 4217), pricing mode (tax-inclusive or tax-exclusive), financial-year start month (1–12),
  time zone (IANA) and cash rounding step (none, 0.05, 0.10 or 1.00 in the currency's units).
- **FR-010**: Choosing a country MUST pre-fill defaults: India → INR, tax-inclusive, April,
  Asia/Kolkata, 1.00; USA → USD, tax-exclusive, January, America/New_York, none; Canada → CAD,
  tax-exclusive, January, America/Toronto, 0.05.
- **FR-011**: Invalid settings MUST be rejected with a message per field; nothing is stored.
- **FR-012**: The creator MUST become an owner of the business. An owner MUST see and open only
  businesses they own; any other business MUST behave as "not found" (Constitution III).
- **FR-013**: Opening a business MUST make it the current business for subsequent requests, set only
  by the server from the signed-in owner's verified ownership, never from data the browser sends
  (Constitution III, feature 003).
- **FR-014**: The owner MUST be able to edit all settings of the open business; changes apply to that
  business only.
- **FR-015**: The back office MUST always show which business is open and let the owner switch to
  another business they own.
- **FR-016**: Every business endpoint MUST have an isolation test (feature 003 harness) proving one
  owner cannot read or change another owner's businesses.

### Key Entities

- **Owner account**: a person who owns businesses: name, email (unique, case-insensitive),
  password hash, verification state, creation time. Not business-scoped: one owner can own many
  businesses.
- **Business ownership**: links an owner account to a business they own. A business has at least one
  owner (the creator).
- **Business**: the tenant root from feature 003, extended with the cash rounding step.
- **Session**: a signed-in owner's session, optionally with an open (current) business.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new owner goes from the sign-up page to a created business in under 3 minutes.
- **SC-002**: 0 businesses of another owner are listed, opened or changed, across every business
  endpoint (isolation harness).
- **SC-003**: 100% of sign-up and sign-in responses are indistinguishable for registered and unknown
  emails (same status and message).
- **SC-004**: After 5 failed sign-ins for an account within 15 minutes, further attempts are delayed or
  refused for at least 15 minutes.
- **SC-005**: Every business created with country defaults passes the pricing rules' settings
  validation (currency, rounding step, financial-year month) with no manual correction.
- **SC-006**: 100% of verification and reset links work once and only once, and none works after it
  expires (24 hours and 1 hour respectively).

## Assumptions

- The web back office (Blazor WebAssembly) and the API from features 001–003 are used; the API is
  the only data path (Constitution VII).
- Country defaults reflect common practice (India rounds cash to ₹1; Canada eliminated the penny);
  owners can change them.
- Business deletion, ownership transfer, co-owners and inviting other owners are out of scope
  (co-owners could come with #6's staff roles).
- Until subscriptions (#8–#9) exist, business creation is free.
- Email delivery, if needed by FR-004, uses a development mail catcher locally; the production email
  provider is chosen with receipts (#25) or launch (#26).
