# Research: Owner Sign-up and Businesses

Versions (2026-10-11, pinned): `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12,
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12, `MailKit` 4.18.1, image
`axllent/mailpit:v1.31.5`.

## R1. Owner accounts: ASP.NET Core Identity, in schema `identity`

- **Decision**: `AddIdentityCore<OwnerAccount>()` with EF stores in a separate schema `identity`
  (`owner_accounts`, `owner_logins`, `owner_tokens`, claims tables as Identity needs). Owner accounts are
  platform data, not business-owned, so they live outside `pos`; the feature 003 convention check keeps
  applying to `pos` only. External logins (`owner_logins`) are part of the Identity model, so "Sign in
  with Google" can later attach to an existing account (FR-002).
- **Rights**: the migrator owns `identity`; the app login gets row read/write through `pos_app` default
  privileges in that schema too. No RLS on `identity` tables: sign-in must find an account before any
  owner is known; access is mediated by Identity's own code paths.
- **Alternatives**: hand-written accounts (re-implements hashing, lockout, tokens: risky); an external
  identity provider (Keycloak, Auth0: more moving parts and cost before they are needed).

## R2. Passwords, lockout, rate limits

- Identity's PBKDF2 hasher (current default iteration count). Validators: minimum 12 characters, no
  composition rules (NIST 800-63B), a custom validator rejecting the 10,000 most common passwords (list
  bundled with the API, no network call). Maximum length 128.
- Lockout: 5 failed attempts → 15 minutes (SC-004), enabled for new accounts.
- `Microsoft.AspNetCore.RateLimiting`: partitioned by client IP, fixed window, on sign-up, sign-in,
  resend-verification and forgot-password (10 requests / minute / IP), `429` problem details.
- No enumeration (FR-005, SC-003): sign-up with a registered email returns the same `202` and sends that
  address an "you already have an account" email; sign-in failures (unknown email, wrong password,
  locked, unverified) all return the same `401` "email or password is incorrect", except that a
  correct password for an unverified account returns `403 verification-required` (only reachable with
  the right password, so it reveals nothing to an attacker).

## R3. Web session: same-site, HttpOnly cookie

- **Decision**: the API issues Identity's application cookie (`HttpOnly`, `Secure` outside local
  development, `SameSite=Strict`, sliding 8 h idle expiry, 7 d absolute). The Blazor WebAssembly app
  sends requests with credentials included; CORS allows the web origin with credentials. The web app
  and API are on the same site (locally `localhost:5090` and `localhost:5080`; in production
  `app.<domain>` and `api.<domain>`), so the cookie is first-party and `SameSite=Strict` holds.
- **CSRF**: `SameSite=Strict` plus a required custom header (`X-HelloPos-Client: web`) on every state-changing
  request, which a cross-site form cannot send without a CORS preflight the API refuses.
- Session secrets are never visible to page scripts or browser storage (FR-008, Constitution VIII).
  Android (#7) uses bearer tokens instead; this choice is web-only.
- **Alternatives**: tokens in browser storage (forbidden by the constitution); a BFF server for the web
  app (contradicts the static-site deployment and API-only rule).

## R4. Data protection keys

- Cookies and email/reset tokens are protected with ASP.NET Core Data Protection. Keys are persisted in
  PostgreSQL (`identity.data_protection_keys` via the EF store), so they survive restarts and are shared by
  every API instance; protected at rest by the database's own protection (an encryption-at-rest key
  provider is a launch-hardening item, #26).

## R5. Email verification and password reset

- Identity token providers with separate lifespans: email confirmation 24 h, password reset 1 h (FR-004,
  FR-004a). Reset tokens become invalid when the security stamp changes, so they are single-use; after a
  successful email confirmation the security stamp is also renewed, so verification links are single-use
  too (SC-006).
- Sessions are revalidated against the security stamp on every request (`ValidationInterval = 0`), so a
  password reset signs out every other session immediately (FR-004a). One indexed lookup per request.
- Links point to the web app (`Web:BaseUrl`), e.g. `/verify-email?user=<id>&token=<token>`; the page
  posts them to the API.
- **Email sending**: `IEmailSender` abstraction (FR-004b) with an SMTP implementation (MailKit). Locally
  `scripts/env up` adds a `mailpit` service (SMTP on 1025, web UI on 8025, bound to loopback); tests use
  an in-memory sender. The production provider is chosen later (#25/#26).

## R6. Owner scope in the database (RLS)

- New session setting `pos.owner_id` and function `pos.current_owner_id()`, set by the session interceptor
  from the signed-in owner (claim `owner_id`) alongside `pos.business_id`.
- New table `pos.business_owners (business_id, id, owner_id, created_at)`: business-owned (feature 003
  conventions, policy `business_scope`), unique `(business_id, owner_id)`, plus a read-only policy
  `owner_read` (`FOR SELECT USING owner_id = pos.current_owner_id()`).
- `pos.businesses` keeps its `business_scope` policy and gains read-only `owner_read`: `EXISTS (SELECT 1
  FROM pos.business_owners bo WHERE bo.business_id = businesses.id AND bo.owner_id = pos.current_owner_id())`.
  So a signed-in owner sees exactly their businesses before opening one (FR-012), and nothing else.
  Owner access is read-only: every write runs in a session acting as the business, after the API has
  checked ownership, so an owner session cannot plant rows (e.g. an ownership link) in another business.
- **Creating a business**: the endpoint generates the id, then within one transaction acts as that new
  business (sets the current business to the id it just created) to insert `businesses` and the
  `business_owners` row. The id comes from the server, never from the request.

## R7. Opening and switching a business

- `POST /owner/businesses/{id}/open` checks ownership (RLS-visible `business_owners` row for the signed-in
  owner), then re-issues the cookie with claim `business_id`; `ClaimsCurrentBusiness` (feature 003) needs
  no change (FR-013). Switching is opening another one.
- **Two tabs**: business-scoped requests from the web app carry header `X-HelloPos-Business: <id>` with
  the business that page was opened for; the API rejects the request with `409 business-changed` when it
  differs from the cookie's current business. The header never selects the business (authority stays the
  cookie); it only detects a stale tab (spec edge case).

## R8. Country defaults in the shared kernel

- `HelloPos.Kernel` gains `Countries` (IN, US, CA) with default currency, pricing mode, financial-year
  month, time zone and cash rounding step (FR-010), plus `BusinessSettingsRules` validation (FR-011). Used
  by the API (authoritative validation) and the web form (pre-fill, instant feedback): one
  implementation, as the kernel was created for.
- New column `pos.businesses.cash_rounding_step numeric(19,4)`, check `IN (0, 0.05, 0.10, 1.00)`.

## R9. Contract

- `contracts/openapi.yaml` gains the owner and business operations (see `contracts/openapi-additions.yaml`);
  the Kiota client is regenerated; backend contract tests validate every new response.

## R10. Roadmap

- Add "Sign in with Google for owners" to "Later (not v1)" or Phase 1 extras, per the clarification.
