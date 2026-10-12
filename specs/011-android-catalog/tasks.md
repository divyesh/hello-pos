---
description: "Tasks for the catalog on Android (feature #13)"
---

# Tasks: Catalog on Android

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/openapi-additions.yaml](contracts/openapi-additions.yaml), [quickstart.md](quickstart.md)

**Tests**: required (Constitution III, VI): API integration tests against PostgreSQL, isolation tests,
shared cases in both suites, Kotlin unit and Robolectric tests; written before the code and seen failing.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

---

## Phase 1: Setup

- [X] T001 Contract first: add `GET /pos/catalog` (`posCatalog`, tag `Pos`, `deviceCredential`, optional `since` int64 ≥ 0, 200 `CatalogSync`, 401, 429) and schemas `CatalogSync`, `CatalogSettings`, `SyncTaxProfile` (+ version item), `SyncCategory`, `SyncProduct`, `SyncDiscount` from `contracts/openapi-additions.yaml` to `contracts/openapi.yaml` (explicit object schemas only, no free-form maps; money int64 minor units; quantities and rates as decimal strings; tax kind `taxable|zeroRated|exempt`; pricing mode `tax_inclusive|tax_exclusive`); lint; regenerate Kiota (web unaffected but kept in step); Android client regenerates on build
- [X] T002 [P] `contracts/catalog-pricing-cases.json` + `catalog-pricing-cases.schema.json`: one catalog in the `CatalogSync` shape (mode `full`; business in America/Toronto, CAD) and hand-derived checks `{ id, productId, at (instant), quantity, enteredPrice?, expected: { unitPrice, discount, tax, total } }` covering: tax-exclusive and tax-inclusive (second catalog with the same products), HST 13% and GST 2.5% + 2.5%, zero-rated and exempt, category default vs product override, percent and fixed discounts the day before, on, and after their dates, a tax rate change across midnight in the business zone (an instant at 23:30 local = next day UTC), deposit taxed and not, open price, weighed product 0.375 kg
- [X] T003 [P] Android dependencies in `android/gradle/libs.versions.toml` and `android/app/build.gradle.kts`: Room (runtime, ktx, compiler via KSP, exported schema to `app/schemas`), `androidx.lifecycle:lifecycle-process`, test: Robolectric, JUnit 4, JUnit Vintage engine (exact versions, latest stable); pass `hellopos.contractsDir` to tests as for the pricing cases

## Phase 2: Foundational

- [X] T004 Server revision model in `backend/src/HelloPos.Api/Features/Catalog/CatalogEntities.cs`: `Revision` (database-generated, never sent by EF) on `TaxProfile`, `Category`, `Product`, `ItemDiscount` with index `(business_id, revision)`; entity `CatalogRemoval` (`entity`, `entity_id`, `revision`) with `ConfigureBusinessOwned()`
- [X] T005 Migration `AddCatalogRevisions` with `CatalogRevisionSql`: `revision` columns and indexes, `catalog_removals` with RLS and `UPDATE, DELETE` revoked from the app login, functions and triggers stamping roots with `pg_current_xact_id()` (children stamp their root; deleted discounts recorded); Down drops them
- [X] T006 Tests `backend/tests/HelloPos.Api.Tests/Features/Catalog/CatalogRevisionTests.cs`: every write operation of #11 (tax profile create/rename/version add/delete, category update/deactivate, product incl. barcode-only change, discount create/update/delete, CSV import) stamps exactly the changed roots with a newer revision; a refused (stale) save changes nothing; a deleted discount is recorded
- [X] T007 [P] Android Room database `android/app/src/main/java/com/recbook/hellopos/data/catalog/`: `CatalogDatabase`, entities of data-model.md (`catalog_meta`, `tax_profiles`, `tax_versions` with `components_json`, `categories`, `products` with `search_name`, `barcodes` with `code` PK, `discounts`), `CatalogDao`, Hilt `CatalogModule`
- [X] T008 [P] Android domain models `android/app/src/main/java/com/recbook/hellopos/domain/catalog/CatalogModels.kt` (`CatalogProduct`, `CatalogCategory`, `CatalogTaxProfile` + versions, `CatalogDiscount`, `CatalogSettings`, `CatalogStatus` with revision, refreshedAt, server offset) and `SearchText.normalize` (lower case, accents removed via `java.text.Normalizer`); tests

**Checkpoint**: migration applies, model snapshot and convention tests pass; Android builds with Room.

## Phase 3: User Story 1 - The till gets its catalog (P1) 🎯 MVP

**Independent test**: pair, sign in, catalog downloaded; offline restart keeps it; a cut-off download leaves the previous copy.

- [X] T009 [P] [US1] Tests `backend/tests/HelloPos.Api.Tests/Features/Pos/CatalogSyncTests.cs` (full mode): no `since` → `full` with every category, product (with normalized barcodes), tax profile with all versions and components, current and future discounts only (ended before yesterday left out), business settings, `revision` and `serverTime`; inactive rows included with `active=false`; empty catalog → revision 0 and empty lists; unknown or revoked device → 401; staff session cookie alone → 401; response compressed when asked (`Accept-Encoding: br, gzip`); contract conformance
- [X] T010 [US1] Slice `backend/src/HelloPos.Api/Features/Pos/CatalogSync.cs`: `GET /pos/catalog` with `DevicePolicy` and the POS rate limit; one `REPEATABLE READ` read-only transaction; mapping to minor units and decimal strings with `CatalogSupport`; response compression (Brotli, gzip) enabled for this path only in `Program.cs` (makes T009 pass)
- [X] T011 [P] [US1] Tests `android/app/src/test/java/com/recbook/hellopos/data/catalog/CatalogStoreTest.kt` (Robolectric): applying a full download stores everything and the meta row; a failure in the middle of apply leaves the previous copy unchanged (fault injected after some inserts); reopening the database keeps the copy
- [X] T012 [US1] `CatalogStore` (apply full in one Room transaction; read flows) and `CatalogRepository` in `data/catalog/` mapping generated `CatalogSync` to entities and Room rows to domain models; `CatalogSync` service (download with the generated client, single-flight mutex, outcome `Updated | Failed(reason)`) (makes T011 pass)
- [X] T013 [P] [US1] Tests `android/app/src/test/java/com/recbook/hellopos/data/catalog/CatalogSyncTest.kt` (MockWebServer + fake store): first download after sign-in; network error keeps no copy and reports "not downloaded yet"; two triggers at once make one request
- [X] T014 [US1] Trigger a refresh after a successful sign-in (online or offline) and on app start when paired (`SignInViewModel`/`StartViewModel` via the repository); tests
- [X] T015 [US1] `feature/catalog/`: `CatalogScreen`, `CatalogViewModel`, `CatalogUiState` (status header "Catalog updated …", "not downloaded yet" with Retry, categories in sort order with their active products by name); "Catalog" entry on `HomeScreen`; ViewModel tests with a fake repository

**Checkpoint**: MVP: a till downloads and keeps the catalog and shows it offline.

## Phase 4: User Story 2 - Keep the catalog up to date (P1)

**Independent test**: change, add and deactivate on the web; the till applies exactly those, together, within 5 minutes or on "Refresh now".

- [X] T016 [P] [US2] Tests in `CatalogSyncTests.cs` (changes mode): `since` = current → `changes` with empty lists and the same revision; after a price change → only that product; after a tax version added → that profile whole; discount deleted → `removedDiscountIds`; `since` newer than current or older than `oldest_delta` → `full`; a save committing during the read is either wholly in or wholly out (snapshot test with a held transaction); refresh payload for 10 changed products < 1% of a 10,000-product full download (SC-004)
- [X] T017 [US2] Changes mode in `CatalogSync.cs` (rows with `revision > since`, removals after `since`, discounts changed regardless of dates) (makes T016 pass)
- [X] T018 [P] [US2] Tests in `CatalogStoreTest.kt`: applying `changes` upserts roots, replaces a product's barcodes and a profile's versions, deletes removed discounts, moves the revision; a failing apply keeps the old revision and data; a flow observer never sees a half-applied state
- [X] T019 [US2] Apply `changes` in `CatalogStore`; `CatalogSync` sends `since` = stored revision and handles a `full` answer (makes T018 pass)
- [X] T020 [US2] Periodic refresh every 5 minutes while the app is in the foreground (`ProcessLifecycleOwner` coroutine in `data/catalog/CatalogRefreshScheduler.kt`) and "Refresh now" on the catalog screen; failures keep the copy and retry at the next trigger; tests with a test dispatcher
- [X] T021 [US2] Warnings in `CatalogUiState`: last success over 24 hours ago (never blocks); device clock differs from `serverTime` by over 5 minutes; tests

## Phase 5: User Story 3 - Find a product with the right price (P1)

**Independent test**: barcode, PLU and name lookups offline; prices equal the server's for the same moment.

- [X] T022 [P] [US3] `android/app/src/main/java/com/recbook/hellopos/domain/catalog/BarcodeRules.kt`: Kotlin port of the kernel's rules (same normalization, kinds, problems and messages); test `SharedBarcodeCasesTest.kt` runs every case of `contracts/barcode-cases.json`
- [X] T023 [P] [US3] Server test `backend/tests/HelloPos.Api.Tests/Features/Catalog/ProductTests.cs`: a UPC-A on one product and its EAN-13 form on another is refused as a duplicate (FR-012) (already covered by #11: `ProductTests.ABarcode_BelongsToOneProductPerBusiness_InEitherForm`)
- [X] T024 [US3] Lookup in `CatalogRepository`/`domain/catalog/ProductLookup.kt`: parse with `BarcodeRules` (invalid → the rule's message), find by normalized code, inactive product or inactive category → not offered, none → "No product with this code"; name search on `search_name` containing the normalized query, active only; Robolectric tests incl. 10,000 products under 200 ms (SC-002)
- [X] T025 [P] [US3] `domain/catalog/CatalogPriceFeed.kt`: business-local date from the instant and time zone; tax version in force (latest `effectiveFrom` ≤ date) of the override or category default profile; active discount (`from` ≤ date ≤ `to`); `LineInput`/`SaleInput` for the Kotlin engine with pricing mode, currency, cash rounding, deposit setting; unit tests
- [X] T026 [P] [US3] Kotlin `SharedCatalogPricingCasesTest.kt`: apply `contracts/catalog-pricing-cases.json` through the same mapping as a download and check every expectation; schema validation of the file
- [X] T027 [P] [US3] .NET `backend/tests/HelloPos.Api.Tests/Features/Catalog/CatalogPricingCasesTests.cs`: insert the file's catalog into a test business (its ids), assert `GET /pos/catalog` equals the file's catalog, and price every check through `CatalogPricing.ForAsync` and the engine
- [X] T028 [US3] Catalog screen: search field (digits → code lookup, otherwise name search; Enter from a keyboard-wedge scanner submits), product detail (regular and discounted price, unit and step, tax profile and rate in force, minimum age, deposit, open price); ViewModel tests

## Phase 6: User Story 4 - Only its own business's catalog (P1)

**Independent test**: pair to A, download, unpair, pair to B: nothing of A remains.

- [X] T029 [P] [US4] `backend/tests/HelloPos.Api.Tests/Tenancy/CatalogSyncIsolationTests.cs`: a device of A never receives B's rows in full or changes mode; `since` values from B's revisions give nothing of B; a revoked device of A gets 401
- [X] T030 [US4] Erase: `DeviceRepository.forget()` clears the catalog (`clearAllTables` + `VACUUM`); `CatalogStore` erases on open when `catalog_meta.business_id` differs from the paired business; Robolectric tests incl. no row left after unpair (SC-007) and a pairing to B never showing A's data; `dataExtractionRules` excluding all app data from device-to-device transfer (on Android 12+ `allowBackup="false"` no longer covers it; lint DataExtractionRules)

## Phase 7: Polish

- [X] T031 [P] Docs: `docs/tenancy.md` (catalog revisions and device downloads), README (catalog on the till), `specs/roadmap.md` (#13 done)
- [X] T032 Quickstart: automated results in `checklists/quickstart-results.md`; manual steps for the project owner
- [X] T033 Performance: 10,000-product first download ready under 60 s on the emulator with a throttled network (SC-001), recorded
- [X] T034 Constitution re-check after delivery in `plan.md`; full suites green (backend, web, Android build, unit tests, lint)

---

## Dependencies

- Setup → Foundational → US1 → US2 (changes build on the full download and store).
- US3 needs the store from US1; T022, T023, T025–T027 can start after Phase 2.
- US4 needs the endpoint (T010) and the store (T012).

## Parallel examples

- Phase 1: T002 and T003 alongside T001.
- Phase 2: T007, T008 (Android) alongside T004–T006 (server).
- US3: T022, T023, T025, T026, T027 together.

## Implementation strategy

MVP = Phases 1–3: the till downloads the full catalog, keeps it, and browses it offline. Then US2 (changes and
refresh), US3 (lookup and prices), US4 (erase and isolation), polish.
