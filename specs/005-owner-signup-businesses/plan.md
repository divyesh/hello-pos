# Implementation Plan: Owner Sign-up and Businesses

**Branch**: `005-owner-signup-businesses` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

## Summary

Owner accounts with ASP.NET Core Identity in a separate `identity` schema (email + password,
verification and reset by single-use expiring links, lockout, rate limits, no enumeration), a
same-site HttpOnly cookie session for the Blazor WebAssembly back office (CSRF header guard), and
business management: create (country defaults from the shared kernel), list, open/switch (cookie claim,
stale-tab guard) and edit. Row-level security gains an owner dimension (`pos.owner_id`,
`pos.business_owners`) so owners see exactly their own businesses before opening one. Emails go through
an `IEmailSender`; locally Mailpit catches them. Contract-first: operations added to `openapi.yaml`, web
client regenerated, every endpoint covered by contract and isolation tests. Decisions:
[research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API, web, kernel)

**Primary Dependencies**: ASP.NET Core Identity + EF stores 10.0.12, Data Protection EF 10.0.12, MailKit
4.18.1, ASP.NET Core rate limiting (built in), MudBlazor (web); Mailpit v1.31.5 (local)

**Storage**: PostgreSQL 18: new schema `identity`; `pos.business_owners`; `pos.businesses.cash_rounding_step`

**Testing**: xUnit v3 + Testcontainers (app login), cookie-based API tests with an in-memory email sender,
isolation harness (owner variant), contract tests, bUnit

**Target Platform**: API container, web static site, local compose

**Project Type**: full-stack feature (API slices + web pages + kernel rules)

**Performance Goals**: sign-up to first business < 3 min for a person (SC-001); security-stamp check
adds one indexed lookup per request

**Constraints**: no tokens in browser storage; no enumeration; business only from the cookie; one
validation implementation (kernel)

**Scale/Scope**: ~12 API operations, 7 web pages, 2 migrations, ~60 new tests

## Constitution Check

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | `cash_rounding_step` numeric(19,4) in the database; on the wire as integer minor units (`cashRoundingStepMinor`: 0, 5, 10, 100), per the money-on-the-wire rule. | Pass |
| II | No sales. | N/A |
| III. Tenant Isolation | Owner dimension in RLS; current business only from the cookie after an ownership check; business id from request never authoritative; isolation tests for every business endpoint. | Pass |
| IV | No sales. | N/A |
| V. Contract-First | Operations added to the contract first; client regenerated; contract tests. | Pass |
| VI. Test-First | Tests before implementation per story, real PostgreSQL. | Pass |
| VII. Vertical Slices | `Features/Owners/*` and `Features/Businesses/*` slices; Identity setup in infrastructure; web only via the API; validation shared via the kernel. | Pass |
| VIII. Secure by Default | HttpOnly SameSite=Strict cookie, CSRF header, lockout, rate limits, slow hashing, breached-password list, sessions revoked on reset, no enumeration. Roles (owner/manager/cashier) arrive in #6. | Pass |
| IX. No Silent Failures | Problem details for every failure, including unreadable request bodies (400, not 500). Email delivery failures are logged (kind only); the response stays `202` so it reveals nothing about the account (FR-005), and the owner can ask for a new link. | Pass (see deviation 5) |
| X. Simplicity | Built-in Identity and rate limiting, no external IdP, no BFF. | Pass |

Result: **pass** (re-checked after design).

### Re-check against the delivered code (T028)

Result: **pass**. Deviations from this plan, all recorded in research.md or data-model.md where they
change the design:

1. **Owner access in the database is read-only** (Principle III, tightening research R6). The plan
   widened the `business_scope` policies to "the session's business or one the owner owns". As built,
   `business_scope` is unchanged and a separate `FOR SELECT` policy `owner_read` lets an owner read
   their links and businesses. Writes always run acting as the business, so an owner session cannot
   plant an ownership link in someone else's business.
2. **Own token provider** (R5). Identity's `DataProtectorTokenProvider` reads the system clock, so link
   expiry could not be tested; `OwnerTokenProvider` uses the same protected payload (user, purpose,
   security stamp) with the injected `TimeProvider`. Lifespans unchanged: 24 h verification, 1 h reset.
3. **Own session revalidation** (R5). Identity's `SecurityStampValidator` signs out unregistered
   schemes (two-factor, external) and failed; the cookie's `OnValidatePrincipal` now checks the
   absolute 7-day limit and compares the security stamp with the stored one on every request.
4. **Cash rounding on the wire in minor units** (Principle I): `cashRoundingStepMinor` instead of a
   decimal.
5. **Email failures**: logged and swallowed rather than surfaced, so sign-up and forgot-password
   cannot reveal whether an account exists (FR-005 outweighs a visible error here).
6. **Sign-out ends the current browser's session only.** Other sessions end on expiry or a password
   reset (which renews the security stamp). Signing out everywhere needs server-side sessions; not
   required by the spec.
7. **API registers both database contexts even without a connection string**, so it starts (health
   only) without a database, as before.

Not done here and still open: Data Protection keys are stored unencrypted at rest in PostgreSQL
(R4; encryption-at-rest key is launch hardening, #26).

## Project Structure

```text
contracts/openapi.yaml                       # + owner and business operations
compose.yaml, .env.example, scripts/env.*    # + mailpit service, SMTP settings
backend/src/HelloPos.Kernel/Businesses/      # Countries, BusinessSettingsRules
backend/src/HelloPos.Api/
├── Infrastructure/Identity/                 # OwnerAccount, IdentityDbContext (schema identity), cookie,
│                                            # CSRF filter, rate limits, password validator, IEmailSender (SMTP)
├── Infrastructure/Tenancy/ICurrentOwner.cs  # owner_id claim; session interceptor sets pos.owner_id
├── Features/Owners/{SignUp,VerifyEmail,ResendVerification,SignIn,SignOut,ForgotPassword,ResetPassword,Me}/
├── Features/Businesses/{ListMine,Create,Open,GetCurrent,UpdateCurrent}/
└── Infrastructure/Persistence/Migrations/   # AddOwnerIdentity, AddBusinessOwnership
backend/tests/HelloPos.Api.Tests/Features/Owners/*, Features/Businesses/*, Tenancy/OwnerIsolationTests.cs
web/src/HelloPos.Web/
├── Auth/ (cookie-aware HttpClient handler, AuthenticationStateProvider via /owner/me)
├── Pages/ SignUp, SignIn, VerifyEmail, ForgotPassword, ResetPassword, Businesses, BusinessSettings
└── Layout/ business switcher
web/tests/HelloPos.Web.Tests/Pages/*
```

**Structure Decision**: two slice folders (owners, businesses) plus identity infrastructure; shared
rules in the kernel.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| Separate `identity` schema | Platform data must not fall under business-owned conventions or RLS | Putting accounts in `pos` would need exemptions in the convention check and RLS |
| Stale-tab header (`X-HelloPos-Business`) | Spec edge case: a tab must not write to a business it did not open | Per-tab sessions need tokens in browser storage (forbidden) |
