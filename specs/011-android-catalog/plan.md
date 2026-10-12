# Implementation Plan: Catalog on Android

**Branch**: `011-android-catalog` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)

## Summary

A paired till downloads its business's whole catalog once, then only what changed, and keeps it in a local
database so it can look up and price products with no network. The server stamps every catalog change with its
transaction id (database triggers; research R1) and one device endpoint,
`GET /pos/catalog?since=`, answering a full copy or the changes in one consistent snapshot. The till applies
each download in one transaction, indexes barcodes by their normalized form (Kotlin port of the barcode
rules, shared cases), prices with the existing Kotlin engine through a price feed that mirrors the server's
`CatalogPricing` (new shared catalog pricing cases prove they agree), and shows a read-only catalog screen.
Unpairing erases the catalog. Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API); Kotlin 2.4 (Android, minSdk 26)

**Primary Dependencies**: existing; Android adds Room (runtime, ktx, compiler via KSP),
`androidx.lifecycle:lifecycle-process`, and for tests Robolectric with the JUnit Vintage engine (all
exact versions pinned in `libs.versions.toml`); API adds ASP.NET Core response compression (in the box)

**Storage**: PostgreSQL 18: `catalog_removals`, trigger-set `revision` columns on 4 catalog tables;
Room `catalog.db` on the till (data-model.md)

**Testing**: xUnit v3 integration tests on Testcontainers PostgreSQL (sync, revisions per write, snapshot
consistency, isolation, catalog pricing cases); Kotlin JUnit 5 (barcode cases, price feed, pricing cases,
sync logic with fakes) and Robolectric (Room apply, index, erase)

**Target Platform**: API container; Android tablets (landscape first)

**Project Type**: web service + mobile app

**Performance Goals**: 10,000 products: first download ready < 60 s, lookups < 200 ms offline; a 10-product
refresh < 1% of a full download

**Constraints**: Constitution I (money), III (isolation), V (contract first), VI (tests first), VII (slices,
`domain` free of Android), VIII (secrets separate), IX (no silent failures)

**Scale/Scope**: 1 API operation, 1 migration, 1 Android screen, ~70 new tests

## Constitution Check

| Principle | How this plan complies | Status |
|-----------|------------------------|--------|
| I. Money Is Exact | Minor units on the wire; the till prices with its `BigDecimal` engine; shared catalog pricing cases run in both suites. | Pass |
| II. Never Lose or Duplicate a Sale | No sales here; the catalog is applied all or nothing, and deactivated products are kept for history. | Pass |
| III. Tenant Isolation | Business from the device credential only; new tables business-owned with RLS; isolation test on the endpoint; the till erases its catalog on unpair or business mismatch. | Pass |
| IV. Append-Only Record | `catalog_removals` append-only; history unchanged. | Pass |
| V. Contract-First | `posCatalog` and its schemas added to `contracts/openapi.yaml` first; Retrofit client generated. | Pass |
| VI. Test-First | Tests per story first; shared barcode and catalog pricing cases. | Pass |
| VII. Vertical Slices | API `Features/Pos/CatalogSync`; Android `domain/catalog`, `data/catalog`, `feature/catalog`; ViewModel uses the repository only. | Pass |
| VIII. Secure by Default | Device credential required, revoked devices refused; rate limited; no secrets in the catalog store; compression only on this endpoint (no reflected secrets). | Pass |
| IX. No Silent Failures | Failed refreshes keep the copy and show the age; stale and clock warnings; lookup says why a code fails. | Pass |
| X. Simplicity | One endpoint for full and changes; whole aggregates instead of field patches; no WorkManager. | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/011-android-catalog/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/openapi-additions.yaml
└── checklists/requirements.md
```

### Source Code

```text
contracts/openapi.yaml, contracts/catalog-pricing-cases.json (+ .schema.json)
backend/src/HelloPos.Api/Infrastructure/Persistence/CatalogRevisionSql.cs  # triggers (entities in CatalogEntities.cs)
backend/src/HelloPos.Api/Features/Pos/CatalogSync.cs               # GET /pos/catalog
backend/src/HelloPos.Api/Infrastructure/Persistence/Migrations/    # AddCatalogRevisions
backend/tests/HelloPos.Api.Tests/Features/Pos/CatalogSyncTests.cs, Features/Catalog/CatalogRevisionTests.cs,
  Features/Catalog/CatalogPricingCasesTests.cs, Tenancy/CatalogSyncIsolationTests.cs
android/app/src/main/java/com/recbook/hellopos/
  domain/catalog/        # models, BarcodeRules, CatalogPriceFeed, ProductLookup
  data/catalog/          # Room database, DAOs, entities, CatalogRepository, CatalogSync, CatalogModule
  feature/catalog/       # CatalogScreen, CatalogViewModel, CatalogUiState
android/app/src/test/java/com/recbook/hellopos/
  domain/catalog/        # SharedBarcodeCasesTest, CatalogPriceFeedTest, SharedCatalogPricingCasesTest
  data/catalog/          # CatalogStoreTest (Robolectric), CatalogSyncTest
  feature/catalog/       # CatalogViewModelTest
```

**Structure Decision**: the endpoint is a POS slice (device-facing, like `/pos/device`); revision tracking
lives with the catalog because every catalog write must feed it. On Android, the rules and the price feed are
plain Kotlin in `domain` so the shared cases run on the JVM.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| Transaction-id revisions set by triggers | Changes must be found whatever order transactions commit in, without locks | Timestamps or identity ids can skip a late commit; a locked counter can deadlock; calls in each slice are easy to forget |
| Robolectric | Room transactions and indexes tested in CI | Instrumented tests do not run in CI |
| Shared catalog pricing cases | Till and server must price identically | Testing each side alone cannot prove agreement |

## Constitution re-check after delivery (T034)

All gates still pass against the delivered code. Deviations from the design above:

- **Change tracking uses transaction ids set by triggers** instead of a per-business counter row: the
  counter could deadlock two transactions editing the same product, and triggers cannot be forgotten by a
  slice (research R1 rewritten; data-model updated). A late-committing save is never skipped (tested, and
  the test was seen failing with the wrong read position).
- **The download needs only the device credential** (spec FR-002 amended at planning), so the lock screen
  refreshes too.
- **FR-012** needed no code: #11 already stores UPC-A in its EAN-13 form with per-business uniqueness, and
  its existing test covers the two-form case.
- **Refresh scheduling** lives in `CatalogRefreshScheduler` (started from `AppViewModel`) and the age shown
  on screen uses an injectable `Ticker`, so neither needs a running clock in tests.
- **App data is excluded from backups and device-to-device transfer** (`data_extraction_rules.xml`): on
  Android 12+ `allowBackup="false"` alone would let a phone-to-phone transfer copy the unencrypted catalog.
- SC-001 is measured in parts by the suites (server response, local save); the end-to-end time on a till
  is a manual quickstart step (`checklists/quickstart-results.md`).
