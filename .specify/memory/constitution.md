<!--
Sync Impact Report
- Version change: (template, unversioned) → 1.0.0 (initial ratification; revised in place
  before first commit, so no version bump)
- Pre-commit revision (2026-10-09) applied decisions from the planning sessions:
  - I. Money Is Exact: wire format changed to integer minor units + ISO 4217 currency;
    server-authoritative totals; shared pricing test vectors (contracts/pricing-cases.json)
  - II. Never Lose, Never Duplicate a Sale → Never Lose or Duplicate a Sale or a Charge:
    payment-provider idempotency, unknown-outcome reconciliation, cash-only offline,
    per-business concurrency-safe order numbers
  - Added III. Tenant Isolation (NON-NEGOTIABLE), driven by an audit of the old system
  - Added IV. Sales Are an Append-Only Record (audit trail)
  - Renumbered: Contract-First API III → V, Test-First IV → VI, Vertical Slices V → VII,
    Secure by Default VI → VIII, No Silent Failures VII → IX, Simplicity VIII → X
  - VI expanded with tenant-isolation tests and shared pricing vectors
  - VII expanded with web back office boundary (API only)
  - VIII expanded with owner/manager/cashier roles
  - X: removed the "manager mode deferred" example; catalog admin now lives in the web back office
- Added sections: Product Scope (v1); Technology & Platform Constraints expanded with
  web back office, payments, countries/tax and DigitalOcean hosting
- Removed sections: none
- Product name set to "Hello POS" (2026-10-10)
- Product Scope (v1): added hosted-service subscriptions, API-enforced, switchable off for
  self-hosting (2026-10-10)
- Amendment 1.0.0 → 1.1.0 (2026-10-11, MINOR: scope materially expanded): Product Scope (v1)
  adds retail stores (convenience stores and groceries) next to cafés and restaurants: barcode
  scanning, container deposits, age-restricted items, multi-buy promotions, cash drawer and shifts.
  Inventory is planned right after v1. No principle changed. Roadmap updated in the same change.
- Deferred TODOs: none
- Templates: plan/spec/tasks templates read the constitution at runtime; the plan template's
  Constitution Check MUST now cover Principles I–X.
- Note: README.md still describes the earlier "reuse existing backend, Android only" plan and is
  out of date (new .NET backend, web back office, PostgreSQL, monorepo). Update separately.
-->

# Hello POS Constitution

## Core Principles

### I. Money Is Exact (NON-NEGOTIABLE)

- Monetary amounts MUST NOT be represented as binary floating point (`Double`, `Float`,
  `double`, `float`) anywhere: domain, persistence, wire format, or UI state.
- On the wire, every amount MUST be an integer count of the currency's minor unit (cents, paise)
  as int64, and every business MUST have an explicit ISO 4217 currency code that responses carry.
- Android MUST use a `Money` value type backed by `BigDecimal`; the backend and web back office
  MUST use `decimal`; PostgreSQL MUST use `numeric` with explicit precision and scale.
- Rounding MUST be half-up to the currency's minor unit and MUST happen only at the points
  defined by the pricing rules, never ad hoc in UI or mapping code.
- Pricing logic (subtotal, per-item tax, tax-inclusive/exclusive, discount, tip, total, change)
  MUST live in a single domain module per side and MUST NOT be duplicated in ViewModels,
  components, adapters, or endpoints.
- The server is authoritative: clients compute totals for display, but the backend MUST
  recompute every sale from its own catalog, tax, and discount rules and MUST reject a sale whose
  submitted totals do not match.
- A language-neutral pricing test-vector file, `contracts/pricing-cases.json`, MUST be executed
  by both the .NET and Kotlin test suites; any pricing change starts by adding cases there.
- Non-money decimals (quantities, unit increments) sent over the wire MUST use invariant
  formatting (`Locale.US` / `CultureInfo.InvariantCulture`); device locale is for display only.

Rationale: the old app's rounding and locale errors produced wrong totals, and two independent
pricing implementations drift unless one is authoritative and both are tested against the same
cases.

### II. Never Lose or Duplicate a Sale or a Charge (NON-NEGOTIABLE)

- A completed sale MUST be persisted locally (Room outbox) before any network call, and MUST be
  retried in the background until the server confirms it.
- Every sale MUST carry a client-generated UUID. The backend MUST treat sale creation as
  idempotent on that UUID: a repeated submission returns the original result and creates nothing
  new; a repeated UUID with a different payload MUST be rejected.
- Every card charge MUST send an idempotency key to the payment provider. A charge whose outcome
  is unknown (timeout, crash, lost response) MUST be reconciled with the provider before any
  retry; the system MUST NOT charge again on uncertainty.
- Offline mode permits cash payments only. Card payments require connectivity.
- The UI MUST prevent double submission (Pay disabled while a submit or charge is in flight).
- Order numbers MUST be generated per business from a concurrency-safe sequence; they MUST NOT
  depend on other businesses' activity.
- Process death, app restart, and loss of connectivity MUST NOT drop or duplicate a sale or a
  charge; each of these scenarios MUST have an automated test.

Rationale: lost and duplicated sales on network failure were a core defect of the old app, and
charging a customer twice is worse than any duplicate record.

### III. Tenant Isolation (NON-NEGOTIABLE)

- The system is multi-tenant on a shared database. An owner account MAY own multiple
  businesses; staff users (owner, manager, cashier) belong to exactly one business.
- Every tenant-owned table MUST carry `business_id`, including child tables (sale items,
  products, taxes, payments), so no isolation rule depends on a join.
- The acting business MUST come only from the authenticated token. It MUST NOT be accepted from a
  request body, route, or query string for authorization purposes; an owner switching businesses
  obtains a token scoped to the selected business after an ownership check.
- EF Core global query filters MUST scope every tenant query to the token's business. Bypassing a
  filter (`IgnoreQueryFilters` or raw SQL) MUST be justified in the plan and called out in review.
- Foreign keys between tenant tables MUST include `business_id` (composite keys), so the database
  rejects cross-tenant references.
- Every id referenced by a write (product, category, table, payment type, user) MUST be validated
  as belonging to the caller's business before use.
- Every backend slice MUST have an integration test proving business A cannot read or modify
  business B's data through it.

Rationale: an audit of the old system found cross-tenant account takeover, leaked payment
credentials, and cross-tenant writes, all caused by hand-written per-query checks and by trusting
ids sent from forms. Isolation must be structural, not remembered.

### IV. Sales Are an Append-Only Record

- Sales, sale items, and payments MUST NOT be overwritten or hard-deleted.
- Cancellations, edits, refunds, and status changes MUST be appended as immutable sale events
  recording actor, device, timestamp (UTC), and reason; current state is derived from the
  original sale plus its events.
- Only owner and manager roles MAY cancel or refund; the rule MUST be enforced server-side.

Rationale: a POS record is evidence for disputes, cash reconciliation, and tax reporting; history
that can be rewritten cannot be trusted.

### V. Contract-First API

- The OpenAPI document in the repository is the single source of truth for the API between the
  backend, the web back office, and the Android app.
- API changes MUST be made to the contract first, reviewed, and then implemented. Backend and
  client code MUST NOT diverge from the contract; contract tests MUST verify this.
- A mock API generated from or validated against the contract MUST exist so client work can
  proceed independently of backend progress.
- Breaking contract changes MUST be called out in the spec/plan and versioned.

Rationale: three applications are built in parallel; a shared, checked contract keeps them aligned
without coordination overhead.

### VI. Test-First for Business Rules

- Pricing, tax, discount, rounding, sale state transitions, idempotency, payment reconciliation,
  and outbox sync logic MUST have tests written before or alongside the implementation, and those
  tests MUST be seen failing before the code that makes them pass.
- Backend feature slices MUST have integration tests against real PostgreSQL (containerized),
  not an in-memory substitute, including the tenant-isolation test required by Principle III.
- Both the .NET and Kotlin suites MUST run `contracts/pricing-cases.json` (Principle I).
- A change that modifies business rules without corresponding test changes MUST NOT be merged.

Rationale: correctness of money, sales, and isolation is non-negotiable (Principles I–III), and
tests are the only durable proof of it.

### VII. Vertical Slices, Clear Boundaries

- The .NET backend MUST be organized as vertical slices: each feature owns its endpoint,
  request/response, validation, handler, and data access. Cross-slice sharing is limited to
  genuine infrastructure and domain primitives (e.g. `Money`, tenancy, auth).
- The web back office MUST talk only to the API, never to the database. Blazor Server and Auto
  render modes are not allowed for it, because they invite server-side data access that bypasses
  the API's isolation rules.
- The Android app MUST keep `domain` free of Android framework imports. Each feature has a Screen,
  ViewModel, and single `UiState`; ViewModels MUST NOT call Retrofit or Room directly.
- UI state MUST survive configuration change and process death; no business logic in
  Composables, Blazor components, or adapters.

Rationale: the old codebase packed logic into Fragments, adapters, and a website with its own
data access, which is where most defects and the tenant leaks lived.

### VIII. Secure by Default

- Tokens and secrets MUST be stored encrypted (Android Keystore; backend secret store or
  environment), never in plain preferences, Intent extras, logs, browser local storage, or source
  control.
- Payment provider credentials MUST be stored encrypted at rest and MUST NOT be returned by the
  API in full; clients see at most a masked suffix.
- Card data MUST never be stored or logged by this system; card payments MUST go through the
  provider's tokenization/SDK so the system stays out of PCI cardholder-data scope.
- Roles are owner, manager, and cashier. Every backend endpoint MUST enforce authentication and
  role-based authorization server-side; client-side checks are UX only.
- Refresh tokens MUST be rotated on use and revocable; devices MUST be revocable individually.
- Environment-specific URLs and keys MUST come from build/runtime configuration.

Rationale: the old app stored tokens in plain text and the old website exposed payment
credentials; payments raise the stakes further.

### IX. No Silent Failures

- Empty `catch` blocks and swallowed errors are prohibited.
- Errors MUST be mapped to typed errors (`AppError` on Android, RFC 7807 problem details on the
  backend, a typed result in the web back office) and surfaced to the user or logged with context.
- The backend MUST emit structured logs with a correlation ID per request; sale UUIDs and
  business ids MUST appear in logs for every sale- and payment-related operation.

Rationale: silent failures hide lost sales and money errors until a customer notices.

### X. Simplicity

- Build only what a current spec requires (YAGNI). Features outside the agreed scope MUST NOT be
  built speculatively.
- New projects, modules, frameworks, or abstraction layers MUST be justified in the plan's
  Complexity Tracking section.
- Prefer the platform's standard tooling over custom infrastructure.

Rationale: a small team rewriting a product gains more from fewer moving parts than from
flexibility nobody has asked for.

## Technology & Platform Constraints

- **Repository**: single monorepo containing the backend, the web back office, the Android app,
  and the shared contract (`contracts/`: OpenAPI document, pricing test vectors).
- **Backend**: .NET 10 (LTS), ASP.NET Core minimal APIs, vertical slice architecture, EF Core with
  Npgsql.
- **Web back office**: Blazor WebAssembly standalone (.NET 10) with MudBlazor. Covers sign-up and
  onboarding, businesses, catalog, taxes, users and roles, payment settings, tables, and reports.
- **Android POS**: Kotlin, Jetpack Compose (Material 3, tablet/landscape first), MVVM with
  unidirectional data flow, Hilt, Retrofit/OkHttp/kotlinx.serialization, Room, DataStore,
  WorkManager, Coroutines/Flow; minSdk 26. Android stays focused on selling; catalog
  administration lives in the web back office.
- **Database**: PostgreSQL; schema changes MUST be delivered as versioned migrations. Data starts
  fresh; no migration from the old MySQL system is in scope.
- **Payments**: Square and Clover in v1, behind a provider-neutral payment interface. In India,
  non-cash payments (UPI, external card terminal) are recorded as payment types without provider
  integration in v1.
- **Countries and tax**: India, USA, Canada. Each business chooses tax-inclusive or tax-exclusive
  pricing. For India the system reports collected tax; tax filing and compliance remain the
  owner's responsibility.
- **Time**: timestamps MUST be stored and transmitted in UTC (ISO 8601); conversion to local time
  happens only for display and for business-day reporting boundaries.
- **Containers**: local services and integration tests run via Podman (compose through the
  docker-compose provider). Instructions and scripts MUST NOT assume Docker Desktop.
- **Hosting**: DigitalOcean: Managed PostgreSQL, the API as a container image, the web back
  office as a static site.
- **Compatibility**: no backward compatibility with the old Android app, old admin website, or
  old API is required.

## Product Scope (v1)

v1 serves both food service (cafés, restaurants) and retail stores (convenience stores,
groceries). v1 delivers: login, selling screen (scan-first for retail, product grid for quick
items), barcode scanning, checkout, card payments, pending orders, sales history, cancel/refund,
itemized sales report, kitchen display, Bluetooth receipt printing, email/SMS receipts, tables,
container deposits, age-restricted item checks, multi-buy promotions, and cash drawer and shift
close, plus the web back office described above, and subscriptions for the hosted service.
Inventory (stock levels, receiving) follows right after v1. Subscription status MUST be enforced by the API, never by a check inside a
client app, and billing MUST be switchable off for self-hosted deployments (the code is open
source under Apache-2.0). Ordering and phasing live in
`specs/roadmap.md`; anything outside this list requires a scope change recorded there.

## Development Workflow & Quality Gates

- All features follow Spec Kit: `/speckit-specify` → `/speckit-clarify` (as needed) →
  `/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`. Specs live
  under `specs/`.
- Every plan MUST include a Constitution Check against Principles I–X; any violation MUST be
  recorded with justification in Complexity Tracking or the plan MUST be revised.
- Work happens on feature branches; `main` MUST stay buildable with all tests passing.
- CI MUST, at minimum: build backend, web back office, and Android; run unit and integration
  tests; run the shared pricing vectors on both sides; and verify conformance to the OpenAPI
  contract.
- Code review MUST explicitly check money handling, tenant isolation, sale and charge
  idempotency, outbox behavior, and error handling for any change touching those areas.

## Governance

- This constitution supersedes other practices, notes, and README guidance where they conflict.
- Amendments are made via `/speckit-constitution`, MUST state the rationale, and MUST include a
  Sync Impact Report reviewed before commit. Dependent specs/plans affected by an amendment MUST
  be identified and updated or explicitly accepted as-is.
- Versioning follows semantic versioning: MAJOR for removing or redefining a principle, MINOR for
  adding a principle/section or materially expanding guidance, PATCH for clarifications.
- Compliance is verified at plan time (Constitution Check), at review time, and by
  `/speckit-analyze` before implementation; unjustified violations block merge.

**Version**: 1.1.0 | **Ratified**: 2026-10-09 | **Last Amended**: 2026-10-11
