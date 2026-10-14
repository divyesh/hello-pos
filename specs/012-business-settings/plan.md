# Implementation Plan: Business Settings

**Branch**: `012-business-settings` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)

## Summary

Owners set up payment types (Cash built in, record-only non-cash types), the discount approval limit
(0% for new businesses) and tip presets; owners and managers set up areas and tables, including bulk add.
Every item is business-owned and versioned, every change is recorded in a settings history, and tills receive
everything in their catalog download (#13), stored on the till for offline selling (#14). The discount rule
is written into the pricing rules. Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API, kernel, web); Kotlin 2.4 (Android storage only)

**Primary Dependencies**: existing only

**Storage**: PostgreSQL 18: `payment_types`, `table_areas`, `dining_tables`, `selling_settings`,
`settings_changes`; Room database version 2 on the till (data-model.md)

**Testing**: xUnit v3 integration tests on Testcontainers PostgreSQL (each operation, roles, conflicts,
history, isolation, download); kernel unit tests; bUnit for pages; Robolectric for the till's storage

**Target Platform**: API container, Blazor WASM back office, Android tills

**Project Type**: web service + web app + mobile app (storage only)

**Performance Goals**: bulk add of 200 tables in under 2 s; pages load in under 1 s with 200 tables

**Constraints**: Constitution I (amount limit in minor units), III (isolation), IV (append-only history), V
(contract first), VI (tests first), VII (slices), IX (no silent failures)

**Scale/Scope**: ~19 API operations, 4 web pages, 1 migration, ~90 new tests

## Constitution Check

| Principle | How this plan complies | Status |
|-----------|------------------------|--------|
| I. Money Is Exact | Amount limit as int64 minor units with the currency; percentages as decimal strings. | Pass |
| II. Never Lose or Duplicate a Sale | Nothing a sale may reference is deleted (deactivate only); Cash always exists. | Pass |
| III. Tenant Isolation | Business from the session or device; composite keys; RLS; every referenced id (area) checked; isolation tests on every operation and on the till download. | Pass |
| IV. Append-Only Record | `settings_changes` insert-only for the app login. | Pass |
| V. Contract-First | Operations and schemas added to `contracts/openapi.yaml` first; Kiota and the Android client regenerated. | Pass |
| VI. Test-First | Tests per story first; kernel rule tests. | Pass |
| VII. Vertical Slices | `Features/Settings/*`; kernel `Settings/SettingsRules`; web via the API only. | Pass |
| VIII. Secure by Default | Role checks per request and per field (managers: tables only); no secrets involved. | Pass |
| IX. No Silent Failures | Stale saves 409; bulk add lists every clash; refusals explain why. | Pass |
| X. Simplicity | Same patterns as the catalog; no new packages. | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/012-business-settings/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/openapi-additions.yaml
└── checklists/requirements.md
```

### Source Code

```text
contracts/openapi.yaml
specs/pricing-rules.md                                  # discount approval rule (section 4)
backend/src/HelloPos.Kernel/Settings/SettingsRules.cs
backend/src/HelloPos.Api/Features/Settings/             # SettingsEntities.cs, SettingsSupport.cs,
                                                        # PaymentTypes/, Tables/, Selling/, SettingsHistory.cs
backend/src/HelloPos.Api/Features/Businesses/CreateBusiness.cs   # Cash + selling row with the business
backend/src/HelloPos.Api/Features/Pos/CatalogSync.cs    # settings in the till download
backend/src/HelloPos.Api/Infrastructure/Persistence/Migrations/  # AddBusinessSettings (+ backfill, triggers)
backend/tests/HelloPos.Api.Tests/Features/Settings/, Tenancy/SettingsIsolationTests.cs
web/src/HelloPos.Web/Pages/Settings/, Services/SettingsService.cs; tests in web/tests/.../Pages/Settings/
android/app/src/main/java/com/recbook/hellopos/data/catalog/   # Room v2: payment types, areas, tables, selling
```

**Structure Decision**: a Settings slice group like Catalog; the till download reuses #13's endpoint and
change tracking so tills have one refresh path.

## Complexity Tracking

No deviations from the constitution; nothing to justify.
