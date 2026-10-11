# Roadmap

How v1 (Constitution, "Product Scope") is split into phases. Each phase ends with something that
works end to end, and each numbered item is one Spec Kit feature
(`/speckit-specify` → … → `/speckit-implement`) on its own branch.

Status: draft, 2026-10-10. Order and scope are open for review.

Every feature includes its API slice (contract first), its web back office pages, its Android
screens where relevant, tenant-isolation tests, and an update to `contracts/pricing-cases.json`
if it touches money.

## Phase 0: Foundation

Goal: an empty system that builds, tests, and deploys, with tenancy enforced before the first
feature exists.

1. **Repository skeleton**: `backend/`, `web/`, `android/`, `contracts/`; .NET 10 solution with a
   vertical-slice layout; Blazor WebAssembly app; Android project; `global.json` pinning the SDK.
2. **Local environment**: `compose.yaml` for Podman or Docker with PostgreSQL, the Prism mock API, Seq and an optional API container (the deployable image); `/health` reports the database;
   one-command start, stop and reset (seed data arrives with the features that create business data).
3. **Tenancy and API infrastructure**: `business_id` conventions, EF Core global query filters,
   composite foreign keys, row-level security, migrations by a dedicated migrator login, the
   cross-tenant isolation test harness, schema convention checks. (RFC 7807 errors, correlation
   ids and structured logging were delivered earlier, in #1.)
4. **Continuous integration**: GitHub Actions building all three apps and running every test
   (including Testcontainers PostgreSQL), contract validity and generated-client drift, API image
   build and compose validation, on every pull request and push to `main`. Staging deployment was
   moved to Phase 8 (decided 2026-10-11: not deploying to DigitalOcean yet).

Done when: every pull request shows the six checks and the isolation test harness runs in CI.

## Phase 1: Accounts and access

Goal: people can sign up, create businesses, and log in with the right role.

5. **Owner sign-up and businesses** (web): owner account, create and edit businesses (country,
   currency, tax-inclusive or exclusive, financial-year start, cash rounding step).
6. **Staff and roles** (web): add owner, manager, and cashier users to a business.
7. **Login and sessions** (API, web, Android): access and refresh tokens with rotation, device
   registration and revocation, an owner switching between businesses.

Done when: an owner signs up on the web, creates a business and a cashier, and the cashier logs
in on Android.

## Phase 2: Subscriptions

Goal: businesses on the hosted service pay a subscription; self-hosted installs are unaffected.

8. **Plans and billing** (API, web): plans, free trial, owner pays on the web back office through
   a billing provider (chosen in the feature's spec; it must work in India, the USA and Canada),
   billing webhooks, invoices.
9. **Subscription enforcement** (API, Android, web): the API checks each business's subscription
   on every request, never the client; a grace period, then the lapsed behavior (to be decided:
   read-only or blocked); Android and web show the status. A configuration switch turns billing
   off entirely for self-hosted deployments.

Done when: an owner starts a trial, pays, lets it lapse, is restricted as specified, and renews;
a self-hosted instance with billing switched off is never restricted.

## Phase 3: Catalog

Goal: a business can set up everything it sells.

10. **Pricing engine**: the shared pricing module in C# (used by the API and the web app) and
   Kotlin, driven by `contracts/pricing-cases.json`, implementing `specs/pricing-rules.md`.
11. **Catalog setup** (web): categories, products, units, open-price items, tax profiles,
   scheduled item discounts, CSV import.
12. **Business settings** (web): payment types, tables, discount approval limit.
13. **Catalog on Android**: download and cache the catalog for offline selling.

Done when: a business's full catalog is set up on the web and shows correctly on Android, with
prices matching the shared test cases.

## Phase 4: Selling with cash

Goal: a real cash sale, online or offline, that can never be lost or duplicated.

14. **Selling screen** (Android): category tabs, product grid, cart, quantities with units and
    steps, item and order discounts, price override with manager approval, tips.
15. **Cash checkout and sale sync**: cash tendered, cash rounding, change; local outbox;
    idempotent sale creation; server recalculation; invoice and ticket numbers.
16. **Receipt printing** (Android): Bluetooth ESC/POS printers, printer selection, receipt layout.

Done when: a cashier completes cash sales in airplane mode, and they appear exactly once on the
server with correct totals and numbers once the device reconnects.

## Phase 5: Order lifecycle

Goal: restaurant-style flows and safe corrections.

17. **Pending orders and tables**: open tabs, adding items later, assigning tables.
18. **Kitchen display**: live order board with item status (server-sent events or SignalR).
19. **Cancel and refund**: sale events (append-only), manager/owner only, reason required.

Done when: an order moves from table to kitchen to paid, and a manager refunds it, with the full
history visible.

## Phase 6: Card payments

Goal: card payments that can never charge twice.

20. **Payment provider interface and reconciliation**: idempotency keys, unknown-outcome
    reconciliation, payment records.
21. **Square**: connecting the account (web), taking payments (Android), tips on the device.
22. **Clover**: same as Square.

Done when: card sales work with both providers on staging, and a forced timeout mid-payment
never results in a double charge.

## Phase 7: History, reports and receipts

Goal: owners can see and share what happened.

23. **Sales history**: Android (today, month, year, custom range; edit customer details) and web.
24. **Reports** (web): itemized sales, payment types, tax collected (split by taxable,
    zero-rated, and exempt for India).
25. **Email and SMS receipts**: provider choice decided in the feature's spec.

Done when: an owner reconciles a day's sales and tax from the web reports.

## Phase 8: Launch

26. **Production readiness**: staging and production environments on DigitalOcean (Managed
    PostgreSQL, API container with the `migrate` job before each deploy, static web app; moved here
    from #4), deployment from CI, backups and restore test, monitoring and alerts, security review,
    load test.
27. **Pilot**: one or two real businesses, feedback, fixes.

## Later (not v1)

- Payment provider integration for India (Razorpay or Pine Labs).
- Manager mode in the Android app (if a client asks).
- Scale integration for weighed items.
- Self-service features beyond the back office (online ordering, customer accounts).
