---
description: "Tasks for catalog setup (feature #11)"
---

# Tasks: Catalog Setup

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/openapi-additions.yaml](contracts/openapi-additions.yaml), [quickstart.md](quickstart.md)

**Tests**: required (Constitution III, VI): integration tests against PostgreSQL, isolation tests for
every operation, written before the slices and seen failing.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

---

## Phase 1: Setup

- [X] T001 Contract first: add every operation and schema of `contracts/openapi-additions.yaml` to `contracts/openapi.yaml` (tags `Catalog`; `X-HelloPos-Business` parameter; 409 `CatalogConflict` with problem type `urn:hellopos:problem:catalog-changed`; money as int64 minor units with `currency`; quantities and rates as decimal strings; `updateBusiness` gains `depositsTaxable`); lint; regenerate Kiota
- [X] T002 [P] `contracts/barcode-cases.json` (+ schema): valid UPC-A, EAN-13, EAN-8, PLU 4/5 digits with their normalized form and kind; invalid: wrong check digit, wrong length, letters, in-store ranges (EAN-13 20–29, UPC-A number system 2), PLU out of range; spaces and dashes normalized; UPC-A and its EAN-13 form normalize to the same code

## Phase 2: Foundational

- [X] T003 [P] Kernel `backend/src/HelloPos.Kernel/Catalog/BarcodeRules.cs` (normalize, validate, kind) and `CatalogRules.cs` (name lengths: profile/category ≤ 60, product ≤ 120, SKU ≤ 40, component label ≤ 20; units `piece|g|kg|ml|l`; quantity > 0 with ≤ 3 decimals, minimum a multiple of step; rate 0–100 with ≤ 3 decimals; min age 1–99; discount percent 0–100 / fixed ≥ 0; to ≥ from); tests `backend/tests/HelloPos.Kernel.Tests/Catalog/` incl. running `contracts/barcode-cases.json`
- [X] T004 Entities and EF configurations in `backend/src/HelloPos.Api/Features/Catalog/` for the 9 tables of data-model.md (composite keys and links via `ConfigureBusinessOwned()`, unique indexes: tax profile and category name lower-case, product SKU lower-case where not null, barcode `(business_id, code)`; version concurrency tokens; `catalog_changes` with `changes jsonb`); `Business.DepositsTaxable`
- [X] T005 Migration `AddCatalog`: tables, `CREATE EXTENSION IF NOT EXISTS pg_trgm` and `btree_gist`, trigram GIN index on `lower(name)`, exclusion constraint on item discounts `(business_id, product_id, daterange(from_date, to_date, '[]') WITH &&)`, `EnableBusinessRowSecurity` on all 9 tables, revoke `UPDATE, DELETE` on `catalog_changes` from the app login; `businesses.deposits_taxable`; Down reverses
- [X] T006 `CatalogAudit.Record(...)` (entity, id, action, `{field: [old, new]}`, actor, import id) and `CatalogProblems` (field errors, `catalog-changed` conflict, not found); test helper `backend/tests/HelloPos.Api.Tests/Features/Catalog/CatalogApi.cs`

**Checkpoint**: migration applies; model snapshot test and convention checks pass.

## Phase 3: User Story 1 - Tax profiles and categories (P1) 🎯 MVP part 1

**Independent test**: profiles of each kind, versions by date, owner-only writes; categories with default profiles.

- [X] T007 [P] [US1] Tests `.../Features/Catalog/TaxProfileTests.cs`: create taxable (≥ 1 component), zero-rated and exempt (no components); names unique case-insensitively; manager can read but gets 403 on every write; add a future version, version in force before and after its date (business time zone, clock); editing or deleting a version in force refused, future one allowed; kind cannot change; deactivate/reactivate; stale version → 409; audit rows; contract conformance
- [X] T008 [P] [US1] Tests `.../Features/Catalog/CategoryTests.cs`: create, update, sort order listing, default tax profile must exist in the same business and be active, names unique, deactivate/reactivate, stale → 409, cashier 403, audit
- [X] T009 [P] [US1] `.../Tenancy/CatalogIsolationTests.cs` (US1 part): A cannot list, read, change or link B's tax profiles and categories (B's ids in a create or update → 404/field error)
- [X] T010 [US1] Slices `Features/Catalog/TaxProfiles/*` and `Categories/*`; `deposits_taxable` on the business update (owner) (makes T007–T009 pass)
- [X] T011 [US1] Web: `Services/CatalogService.cs`; pages `Pages/Catalog/TaxProfiles.razor` (versions list with current/future, add version, owner-only actions) and `Categories.razor`; "Catalog" nav for owners and managers; business settings page shows "Deposits are taxable" (owners); bUnit tests

## Phase 4: User Story 2 - Products (P1) 🎯 MVP part 2

**Independent test**: counted, weighed, open-price, age-restricted and deposit products with barcodes, found by name/barcode; the pricing feed prices them as the shared cases do.

- [X] T012 [P] [US2] Tests `.../Features/Catalog/ProductTests.cs`: create/update with every field; tax override vs category default (and following later default changes); open price ⇒ no unit price and vice versa; quantity rules (defaults 1/1, kg 0.005, minimum multiple of step); age 1–99; deposit ≥ 0; money in minor units round-trips for 0-, 2- and 3-digit currencies; barcodes (invalid refused with reason, in-store range refused, duplicate in the business refused naming the product, the same code allowed in another business, replaced as a set on update); SKU unique; deactivate/reactivate; stale → 409; cashier 403; audit with old/new values
- [X] T013 [P] [US2] Tests `.../Features/Catalog/ProductSearchTests.cs`: name part (case-insensitive), full barcode (either UPC-A or EAN-13 form), SKU, category and active filters, paging; 10,000 products searched < 1 s (SC-003)
- [X] T014 [P] [US2] Tests `.../Features/Catalog/CatalogPricingTests.cs`: for products and an instant, the feed returns unit price, quantity rules, deposit, tax profile with the version in force (before/after a future version's date), discount in force, business mode/currency/cash step/deposits-taxable; pricing a catalog-built sale with the engine gives the amounts of a matching shared case (SC-005)
- [X] T015 [P] [US2] `CatalogIsolationTests.cs` (US2 part): products, barcodes, search and the feed never show or accept B's data; B's category or tax profile id in a product → refused
- [X] T016 [US2] Slices `Features/Catalog/Products/*` (create, get, update, list/search, deactivate, reactivate) and `CatalogPricing.cs` (makes T012–T015 pass)
- [X] T017 [US2] Web: `Pages/Catalog/Products.razor` (search box, category/active filters, paging) and `ProductEdit.razor` (all fields, barcodes with instant validation from the kernel, effective tax profile shown, money in the business currency's decimals); bUnit tests

**Checkpoint (MVP)**: an owner sets up tax profiles, categories and products on the web; the feed prices them.

## Phase 5: User Story 3 - Scheduled item discounts (P2)

- [X] T018 [P] [US3] Tests `.../Features/Catalog/ItemDiscountTests.cs`: percent and fixed, from/to (open-ended), in force by local date (clock), overlap refused naming the other (including two concurrent creates: one wins, the database constraint holds), running discount: only end early; future: edit/delete; past: read-only; isolation; audit
- [X] T019 [US3] Slices `Features/Catalog/Discounts/*`; feed uses the discount in force
- [X] T020 [US3] Web: discounts section on `ProductEdit.razor` (status future/running/ended); bUnit tests

## Phase 6: User Story 4 - CSV import (P2)

- [X] T021 [P] [US4] Tests `.../Features/Catalog/CatalogImportTests.cs`: preview outcomes per row (create, update, unchanged, error with row/column/reason) saves nothing; import refused while any row errs; all or nothing (a failure mid-way leaves nothing); missing categories created with the named tax profile, missing profile → row error; match by SKU then barcode; re-import → 0 changes; quoted fields, commas in names, BOM, `|`-separated barcodes, decimal comma refused; > 10,000 rows or > 5 MB refused; B's names/ids cannot link to B's records; import record + one history row per change; 1,000 rows preview < 10 s, import < 30 s (SC-002)
- [X] T022 [US4] `Features/Catalog/Import/*` (RFC 4180 parser `CsvReader.cs` with its own unit tests, row validation reusing the product rules, preview, import in one transaction)
- [X] T023 [US4] Web: `Pages/Catalog/Import.razor` (upload, preview table with filters for errors, import button enabled only without errors, result counts; template download); bUnit tests

## Phase 7: User Story 5 - Export and history (P3)

- [X] T024 [P] [US5] Tests: export CSV columns as the import format, cells starting with `=`, `+`, `-`, `@` escaped, export → import preview reports 0 changes; history list per entity, newest first, paged; isolation
- [X] T025 [US5] Slices `Features/Catalog/Export/*`, `History/*`; web export button and a history panel on the product page

## Phase 8: Polish

- [X] T026 Run backend, web and Android suites; contract lint; Kiota up to date
- [ ] T027 Run quickstart.md manually; record `checklists/quickstart-results.md` (SC-001 timing)
- [X] T028 [P] Docs: `docs/tenancy.md` (exclusion constraint and composite links as examples), README catalog section, roadmap #11 done
- [X] T029 Re-check the Constitution Check against delivered code; note deviations in plan.md

---

## Dependencies

- Setup → Foundational → US1 → US2 (products need categories and profiles) → US3, US4 (both need products; independent of each other) → US5 → Polish.
- Within a story: tests first (fail), slices, web.

## Parallel opportunities

- T002, T003 alongside T001; story tests [P] in parallel; US3 and US4 in parallel after US2.

## Implementation strategy

MVP = Phases 1–4 (tax profiles, categories, products and the pricing feed), then report; then US3–US5 and polish.
