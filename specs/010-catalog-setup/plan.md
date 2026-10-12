# Implementation Plan: Catalog Setup

**Branch**: `010-catalog-setup` | **Date**: 2026-10-12 | **Spec**: [spec.md](spec.md)

## Summary

The back office gets a catalog: tax profiles kept as dated versions (owners only; versions already in
force are immutable), categories with a default tax profile, products (units, quantity rules, open price,
SKU, barcodes validated by GS1 check digits and unique per business, age restriction, deposit), scheduled
item discounts that cannot overlap, search, CSV import with a full dry run and all-or-nothing idempotent
apply, a price-list export in the import format, and an append-only catalog history. Every table is
business-owned with composite keys and row-level security. A pricing feed turns catalog data into the
pricing engine's inputs (#10) for a given moment, ready for selling (#14) and the server's recalculation
(#15). Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API, kernel, web); no Android work in this feature

**Primary Dependencies**: existing only; PostgreSQL extensions `pg_trgm` and `btree_gist` (contrib,
trusted, available on DigitalOcean Managed PostgreSQL)

**Storage**: PostgreSQL 18: 9 new `pos` tables (data-model.md), `businesses.deposits_taxable`

**Testing**: xUnit v3 integration tests on Testcontainers PostgreSQL; kernel unit tests with shared
`contracts/barcode-cases.json`; bUnit for pages; contract conformance as in earlier features

**Target Platform**: API container, Blazor WASM back office

**Project Type**: web service + web app (vertical slices `Features/Catalog/*`)

**Performance Goals**: search < 1 s at 10,000 products; 1,000-row CSV preview < 10 s, import < 30 s

**Constraints**: Constitution I (money), III (isolation, composite keys, referenced ids checked), V
(contract first), VI (tests first), VII (slices), IX (no silent failures)

**Scale/Scope**: ~30 API operations, ~6 web pages, 1 migration, ~120 new tests

## Constitution Check

| Principle | How this plan complies | Status |
|-----------|------------------------|--------|
| I. Money Is Exact | `numeric(19,4)` storage, int64 minor units on the wire with the business currency; quantities as invariant decimal strings; pricing stays in the kernel engine, the catalog only feeds it. | Pass |
| II. Never Lose or Duplicate a Sale | Nothing a sale may reference is deleted; tax versions in force are immutable; imports are all or nothing. | Pass |
| III. Tenant Isolation | Business from the session only; composite FKs on every link; RLS on every table; every referenced id (category, tax profile, product) checked; isolation tests on every operation incl. import. | Pass |
| IV. Append-Only Record | `catalog_changes` append-only for the app login. | Pass |
| V. Contract-First | All operations added to `contracts/openapi.yaml` first; Kiota regenerated. | Pass |
| VI. Test-First | Integration tests per story written first; barcode shared cases; pricing feed tested with the engine. | Pass |
| VII. Vertical Slices | `Features/Catalog/{TaxProfiles,Categories,Products,Discounts,Import,Export,History,Pricing}`; kernel `Catalog/BarcodeRules`; web talks only to the API. | Pass |
| VIII. Secure by Default | Role checks per request (owners for tax); CSV size and row limits; no formula execution (exports prefix cells starting with `=`, `+`, `-`, `@` to prevent CSV injection in spreadsheets). | Pass |
| IX. No Silent Failures | Every row error reported; stale saves refused (409); unknown ids 404. | Pass |
| X. Simplicity | No new packages (own small CSV parser); offset paging. | Pass |

## Project Structure

### Documentation (this feature)

```text
specs/010-catalog-setup/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/openapi-additions.yaml
└── checklists/requirements.md
```

### Source Code

```text
contracts/openapi.yaml, contracts/barcode-cases.json
backend/src/HelloPos.Kernel/Catalog/           # BarcodeRules, CatalogRules (names, units, quantities, discounts)
backend/src/HelloPos.Api/Features/Catalog/     # TaxProfiles/, Categories/, Products/, Discounts/, Import/,
                                               # Export/, History/, CatalogPricing.cs, entities + configurations
backend/src/HelloPos.Api/Features/Businesses/  # deposits_taxable
backend/src/HelloPos.Api/Infrastructure/Persistence/Migrations/  # AddCatalog
backend/tests/HelloPos.Api.Tests/Features/Catalog/, Tenancy/CatalogIsolationTests.cs
backend/tests/HelloPos.Kernel.Tests/Catalog/
web/src/HelloPos.Web/Pages/Catalog/, Services/CatalogService.cs; tests in web/tests/.../Pages/Catalog/
```

**Structure Decision**: one slice folder per catalog area, as in features 005–007. The kernel holds the
rules the web also checks (barcodes, quantities, names), so both give identical messages.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| Tax profile versions | Rates change on announced dates; completed sales keep theirs | Editing in place cannot pre-enter a future rate |
| Exclusion constraint (`btree_gist`) | Overlapping discounts would make the price ambiguous | An application check alone can race |
| Own CSV parser | One small, well-tested function | A dependency for one format |

## Constitution re-check after delivery (T029)

All gates still pass against the delivered code. Deviations from the design above:

- **Import matching gained a third key**: a row with neither SKU nor barcode matches by exact name when
  exactly one SKU-less product has it (otherwise a row error). Found by the export round-trip test: without
  it, re-importing an export duplicated such products (research R8 updated).
- **History shape on the wire** is a list of `{ field, old, new }` text values instead of a free-form map:
  the free-form schema generated Kotlin that crashed the compiler (contract changed before any client used it).
- **`CatalogSupport.BusinessAsync` selects the open business by id**: the owner-read policy lets an owner see
  all their businesses, so "the single visible row" fails for owners with two (caught by the isolation test).
- **The web upload uses Blazor's `InputFile`** instead of MudBlazor's file upload component.
- Manual quickstart (T027) is for the project owner to run; automated results are in
  `checklists/quickstart-results.md`.

