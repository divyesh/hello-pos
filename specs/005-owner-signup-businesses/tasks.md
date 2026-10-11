---
description: "Task list for feature 005: owner sign-up and businesses"
---

# Tasks: Owner Sign-up and Businesses

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi-additions.yaml](contracts/openapi-additions.yaml),
[quickstart.md](quickstart.md)

**Tests**: Required (Constitution III, VI, VIII). Each story's tests come first and must fail first.
API tests run against real PostgreSQL as the app login, with cookies (the test client keeps them) and an
in-memory email sender that exposes the last email per address.

## Conventions

- .NET through `scripts/dotnet.ps1`; versions exactly as in research.md.
- Every new operation goes into `contracts/openapi.yaml` before its endpoint; regenerate the Kiota client
  after contract changes (command in `web/src/HelloPos.Web/ApiClient/README.md`).
- State-changing requests require header `X-HelloPos-Client: web`.
- Never log passwords, tokens or reset links.

---

## Phase 1: Setup

- [X] T001 Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12 and `MailKit` 4.18.1 to `Directory.Packages.props` and `HelloPos.Api.csproj`
- [X] T002 [P] Add service `mailpit` (`axllent/mailpit:v1.31.5`, SMTP 1025, UI 8025, health check, ports on `HELLOPOS_BIND_ADDRESS`) to `compose.yaml`; add `HELLOPOS_MAIL_UI_PORT=8025`, `HELLOPOS_SMTP_PORT=1025` to `.env.example`; pass `Email__SmtpHost=mailpit`, `Email__SmtpPort=1025`, `Email__From`, `Web__BaseUrl` to the `api` service; print the Mailpit address in `scripts/env.ps1|sh`
- [X] T003 [P] Add "Sign in with Google for owners" to "Later (not v1)" in `specs/roadmap.md` (clarification Q1)

## Phase 2: Foundational

- [X] T004 Create `backend/src/HelloPos.Kernel/Businesses/Countries.cs` (IN, US, CA defaults per data-model.md) and `BusinessSettingsRules.cs` (validation per data-model.md returning field → message), with kernel tests `backend/tests/HelloPos.Kernel.Tests/Businesses/BusinessSettingsRulesTests.cs` written first (valid defaults for each country pass, SC-005; each invalid field reported)
- [X] T005 Create `backend/src/HelloPos.Api/Infrastructure/Identity/OwnerAccount.cs` (IdentityUser<Guid> with DisplayName, CreatedAt; UUIDv7 ids) and `OwnerIdentityDbContext.cs` (IdentityDbContext in schema `identity`, snake_case table names `owner_accounts`, `owner_logins`, ..., plus `data_protection_keys`), registered with the same provider settings and migrations history table as `HelloPosDbContext` (separate history table `identity.__ef_migrations_history`); extend `Migrator` to migrate both contexts and grant the app's group role on schema `identity` via a migration
- [X] T006 Add `Infrastructure/Tenancy/ICurrentOwner.cs` (`ClaimsCurrentOwner` reading claim `owner_id`), extend `BusinessSessionInterceptor` to set `pos.owner_id` too, and add migration `AddBusinessOwnership`: `pos.current_owner_id()`, table `pos.business_owners` (business-owned, unique `(business_id, owner_id)`, RLS per data-model.md), replace the `businesses` policy, add `businesses.cash_rounding_step numeric(19,4)` with its check (research R6, R8)
- [X] T007 Add `Infrastructure/Identity/IdentitySetup.cs`: `AddIdentityCore<OwnerAccount>` + EF stores, password options (min 12, no composition rules, max 128), `CommonPasswordValidator` with bundled `common-passwords.txt` (10,000 entries, embedded resource), lockout 5/15 min, token providers (email 24 h, reset 1 h), Data Protection persisted to `data_protection_keys`, application cookie (HttpOnly, Secure outside Development, SameSite=Strict, 8 h sliding / 7 d absolute, `ValidationInterval = 0`, API-style 401/403 instead of redirects), rate limiter policy `owner-auth` (permit limit from `RateLimits:OwnerAuthPerMinute`, default 10/min/IP; the test host raises it except in the rate-limit test, since every test shares one client address), `RequireClientHeader` endpoint filter (CSRF), CORS with credentials for `Cors:AllowedOrigins`
- [X] T008 Add `Infrastructure/Identity/Email/IEmailSender.cs` + `SmtpEmailSender.cs` (MailKit, settings `Email:*`) + email templates (verification, already-registered, reset) with links from `Web:BaseUrl`; test project gets `InMemoryEmailSender`
- [X] T009 Extend the test infrastructure: `TenancyApp` registers `InMemoryEmailSender`, keeps cookies per client, and adds helpers `SignUpVerifiedOwnerAsync(email)` and `OwnerClient(email)`; add `IsolationTests` support for owner-based clients alongside the test business header

**Checkpoint**: migrations create `identity` and ownership; existing 53 tests still pass.

---

## Phase 3: User Story 1 - Accounts and sign-in (P1) 🎯 MVP

### Tests (first, failing)

- [X] T010 [P] [US1] Add the owner operations to `contracts/openapi.yaml` (sign-up, verify-email, resend-verification, sign-in, sign-out, forgot-password, reset-password, me) with schemas and problem responses
- [X] T011 [P] [US1] `backend/tests/HelloPos.Api.Tests/Features/Owners/SignUpTests.cs`: 202 and verification email; same 202 for a registered email (and an "already registered" email instead); password rules (11 chars, common password) → 400 per field; missing CSRF header → 400/403; responses match the contract
- [X] T012 [P] [US1] `.../Features/Owners/VerifyEmailTests.cs`: link verifies; second use fails; expired (clock advanced 24 h+) fails; unverified sign-in → 403 verification-required only with the right password; resend sends a new link
- [X] T013 [P] [US1] `.../Features/Owners/SignInTests.cs`: success sets an HttpOnly SameSite=Strict cookie; wrong password, unknown email and locked all return identical 401 bodies (SC-003); 5 failures lock for 15 minutes (SC-004); 11th request in a minute from one IP → 429; sign-out ends the session; `/owner/me` 401 without a session
- [X] T014 [P] [US1] `.../Features/Owners/PasswordResetTests.cs`: forgot-password always 202; reset link works once, not after 1 h; after reset, another client's existing session gets 401 on its next request (FR-004a)

### Implementation

- [X] T015 [US1] Implement slices `Features/Owners/SignUp`, `VerifyEmail`, `ResendVerification`, `SignIn`, `SignOut`, `ForgotPassword`, `ResetPassword`, `Me` (IEndpointMapper each, rate-limited, CSRF-guarded, renewing the security stamp after email confirmation; sign-in issues claims `owner_id`) (makes T011–T014 pass)
- [X] T016 [US1] Regenerate the Kiota client; web: cookie-aware `HttpClient` (`BrowserRequestCredentials.Include`, CSRF header), `OwnerAuthenticationStateProvider` (via `/owner/me`), pages `SignUp.razor`, `SignIn.razor`, `VerifyEmail.razor`, `ForgotPassword.razor`, `ResetPassword.razor`, sign-out in the layout; bUnit tests for each page's success and error states in `web/tests/HelloPos.Web.Tests/Pages/Owners/`

**Checkpoint**: an owner can sign up, verify, sign in, reset and sign out.

---

## Phase 4: User Story 2 - Create businesses (P1)

- [X] T017 [P] [US2] Add `GET/POST /owner/businesses` and `Business`/`BusinessSettings`/`BusinessSummary` schemas to the contract
- [X] T018 [P] [US2] `.../Features/Businesses/CreateBusinessTests.cs` (first, failing): unverified owner → 403; valid settings per country → 201 with the owner linked; invalid fields → 400 per field (kernel rules); business id in the body ignored (server-generated); the new business appears in the owner's list
- [X] T019 [US2] Implement `Features/Businesses/Create` (server-generated id; one transaction acting as the new business to insert `businesses` and `business_owners`) and `ListMine`
- [X] T020 [US2] Web: `Businesses.razor` (list + "create your first business" empty state) and create form with country pre-fill and kernel validation; bUnit tests

---

## Phase 5: User Story 3 - Open, switch, edit (P2)

- [X] T021 [P] [US3] Add `/owner/businesses/{id}/open`, `GET/PUT /business` to the contract
- [X] T022 [P] [US3] Tests first: `.../Features/Businesses/OpenAndEditTests.cs` (open sets the business; another owner's id → 404 identical to unknown; edit saves only that business; stale `X-HelloPos-Business` → 409; switching) and `.../Tenancy/OwnerIsolationTests.cs` using the harness: owner X cannot list, open, read or update owner Y's businesses (SC-002)
- [X] T023 [US3] Implement `Features/Businesses/Open`, `GetCurrent`, `UpdateCurrent`, the stale-tab endpoint filter
- [X] T024 [US3] Web: business switcher in the layout (shows the open business), `BusinessSettings.razor`, sending `X-HelloPos-Business`; handle 409 with "this tab is out of date, reload"; bUnit tests

---

## Phase 6: Polish

- [X] T025 Run backend and web suites; all pass (existing + new)
- [X] T026 Run quickstart.md manual steps with Mailpit; record in `checklists/quickstart-results.md`
- [X] T027 [P] Update README (owners, Mailpit, sign-in) and `docs/tenancy.md` (owner dimension, `pos.owner_id`)
- [X] T028 Re-check the Constitution Check against delivered code; note deviations in plan.md

## Dependencies

- Setup → Foundational (T004–T009) → US1 → US2 → US3 → Polish.
- Within each story: contract → tests (red) → implementation → web.
