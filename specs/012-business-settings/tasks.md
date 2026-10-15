---
description: "Tasks for business settings (feature #12)"
---

# Tasks: Business Settings

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/openapi-additions.yaml](contracts/openapi-additions.yaml), [quickstart.md](quickstart.md)

**Tests**: required (Constitution III, VI): integration tests against PostgreSQL, isolation tests for every
operation, written before the slices and seen failing.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

---

## Phase 1: Setup

- [X] T001 Contract first: add every operation and schema of `contracts/openapi-additions.yaml` to `contracts/openapi.yaml` (tag `Settings`; `X-HelloPos-Business` parameter; 409 with problem type `urn:hellopos:problem:settings-changed`; amount limit int64 minor units with `currency`; percentages as decimal strings; explicit `changes` list as in `CatalogChange`); lint; regenerate Kiota. The `CatalogSync` additions moved to T021 so the contract never declares fields the server does not send yet
- [X] T002 [P] Kernel `backend/src/HelloPos.Kernel/Settings/SettingsRules.cs` (names: payment type and area 1–40, table 1–20; seats 1–50; limit percent 0–100 ≤ 2 decimals; amount ≥ 0; tip presets 1–3 distinct 1–100 ≤ 2 decimals, sorted; bulk range `1 ≤ from ≤ to`, `to − from < 200`, names `prefix + n + suffix` within 20 characters; payment kinds) with messages; tests `backend/tests/HelloPos.Kernel.Tests/Settings/SettingsRulesTests.cs`

## Phase 2: Foundational

- [X] T003 Entities and configurations `backend/src/HelloPos.Api/Features/Settings/SettingsEntities.cs` for `payment_types`, `table_areas`, `dining_tables`, `selling_settings`, `settings_changes` per data-model.md (composite keys, unique normalized names, partial unique index on `(business_id) WHERE built_in`, check `kind = 'cash'` iff `built_in`, built-in always active, seats 1–50, percent 0–100, version tokens, `ConfigureCatalogRevision()` on the three list tables)
- [X] T004 Migration `AddBusinessSettings`: tables, RLS on all five, `UPDATE, DELETE` revoked on `settings_changes`, revision triggers (`pos.stamp_catalog_revision`) on `payment_types`, `table_areas`, `dining_tables`, backfill Cash and a selling row for every existing business; Down reverses
- [X] T005 `SettingsSupport` (problems: field errors, `settings-changed` 409, not found; `RecordChange`; `SellingAsync`) and `CreateBusiness` creating Cash and the selling row in the same transaction; test helper `backend/tests/HelloPos.Api.Tests/Features/BusinessSettings/SettingsApi.cs` (namespace `BusinessSettings`: `Settings` clashes with an existing test helper); test: a new business has Cash and a 0% limit
- [X] T006 Web `Services/SettingsService.cs` skeleton, Settings menu group (owners and managers), `Fakes/FakeSettingsService.cs`

**Checkpoint**: migration applies; model snapshot and convention tests pass.

## Phase 3: User Story 1 - Payment types (P1) 🎯 MVP part 1

- [X] T007 [P] [US1] Tests `.../Features/BusinessSettings/PaymentTypeTests.cs`: Cash present, cannot be deactivated or change kind, can be renamed and moved; create each kind with defaults (`asksReference` on for externalCard, digitalWallet, cheque); `givesChange` only for cash; names unique ignoring case; reorder (every type exactly once, else 400); deactivate/reactivate; 20 active maximum; managers read but 403 on every write; cashier 403; stale version 409; history rows; contract conformance
- [X] T008 [US1] Slices `Features/Settings/PaymentTypes/*` (makes T007 pass)
- [X] T009 [US1] Web page `Pages/Settings/PaymentTypes.razor` (list, inline add/edit as on the catalog pages, up/down ordering, deactivate/reactivate, read-only for managers) and bUnit tests

## Phase 4: User Story 2 - Discount approval limit (P1) 🎯 MVP part 2

- [X] T010 [P] [US2] Tests `.../Features/BusinessSettings/SellingSettingsTests.cs` (limit part): new business 0% and no amount; owner sets 10% and 20.00 (minor units in the business currency); invalid values field errors; managers read, 403 on the limit; stale 409; history
- [X] T011 [US2] Slice `Features/Settings/Selling/*` (get and update; per-field role check: managers only `usesTables`) (makes T010 pass)
- [X] T012 [P] [US2] `specs/pricing-rules.md` section 4: the discount approval rule (research R7), deferral removed
- [X] T013 [US2] Web page `Pages/Settings/DiscountsAndTips.razor` (limit part; owners edit, managers read-only) and bUnit tests

**Checkpoint**: MVP: payment types and the discount limit work on the web.

## Phase 5: User Story 3 - Areas and tables (P2)

- [X] T014 [P] [US3] Tests `.../Features/BusinessSettings/TableTests.cs`: "uses tables" by owner and manager; areas create/rename/reorder; tables create/update/move area/seats; names unique ignoring case; bulk 1–20 with prefix and suffix, positions continue; bulk clash refuses all and lists names; bulk over 200 refused; area with active tables cannot be deactivated; deactivate/reactivate; cashier 403; stale 409; history (one `bulkCreated` row with the count); 200 tables bulk in under 2 s
- [X] T015 [US3] Slices `Features/Settings/Tables/*` (makes T014 pass)
- [X] T016 [US3] Web page `Pages/Settings/Tables.razor` (switch, areas, tables, bulk add dialog with preview of names) and bUnit tests

## Phase 6: User Story 4 - Tip presets (P3)

- [x] T017 [P] [US4] Tests in `SellingSettingsTests.cs`: tips on with 10/15/20 saved ascending; duplicates, more than 3, out of range refused; tips off keeps no presets offered; managers 403
- [x] T018 [US4] Tips in the selling slice and `DiscountsAndTips.razor` (makes T017 pass); bUnit tests

## Phase 7: User Story 5 - History and tills (P2)

- [x] T019 [P] [US5] Tests `.../Features/BusinessSettings/SettingsHistoryTests.cs`: list by entity and all, newest first, field from/to, paging; history is append-only for the app login
- [x] T020 [US5] Slice `Features/Settings/SettingsHistory.cs` and web page `Pages/Settings/History.razor` with bUnit tests
- [x] T021 [P] [US5] Contract: `CatalogSync` gains required `paymentTypes`, `tableAreas`, `tables`, `selling` (lint, regenerate Kiota). Tests in `backend/tests/HelloPos.Api.Tests/Features/Pos/CatalogSyncTests.cs`: full download carries payment types (Cash first by position), areas, tables and `selling`; changes after a payment type rename carry only it; `selling` always present
- [x] T022 [US5] `CatalogSync.cs`: settings lists (changed since `since`) and `selling` (makes T021 pass)
- [x] T023 [P] [US5] `backend/tests/HelloPos.Api.Tests/Tenancy/SettingsIsolationTests.cs`: another business's payment types, areas, tables, selling settings and history are invisible and unchangeable by every operation (ids in paths and in bodies, e.g. `areaId`); a till receives only its business's settings
- [x] T024 [US5] Android: Room version 2 (`payment_types`, `table_areas`, `dining_tables`; selling values in `catalog_meta`; destructive migration so the copy is downloaded again), mapping from the generated client, apply in the same transaction; domain models `PaymentTypeInfo`, `TableAreaInfo`, `DiningTableInfo`, `SellingRules`; repository `paymentTypes()`, `sellingRules()`; Robolectric tests (stored, replaced on changes, version change triggers a full download)

## Phase 8: Polish

- [ ] T025 [P] Docs: `docs/tenancy.md` (settings tables), README (setting up payment types, tables, discounts), `specs/roadmap.md` (#12 done)
- [ ] T026 Quickstart: automated results in `checklists/quickstart-results.md`; manual steps for the project owner
- [ ] T027 Constitution re-check after delivery in `plan.md`; full suites green (backend, web, Android, contract lint)

---

## Dependencies

- Setup → Foundational → US1 and US2 (MVP) → US3, US4 → US5.
- US5's download (T021–T022) needs the entities of US1 and US3; T024 needs T022 and the regenerated client.

## Parallel examples

- Phase 1: T002 alongside T001.
- US1/US2: T007, T010, T012 together.

## Implementation strategy

MVP = Phases 1–4 (payment types and the discount limit on the web). Then tables, tips, history and the till
download with storage on the till, then polish.
