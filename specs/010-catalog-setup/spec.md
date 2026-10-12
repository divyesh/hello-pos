# Feature Specification: Catalog Setup

**Feature Branch**: `010-catalog-setup`
**Created**: 2026-10-12
**Status**: Draft
**Input**: Roadmap Phase 3, feature #11. In the web back office, owners and managers set up everything a
business sells: tax profiles, categories, products (units, quantities, open price, barcodes, age
restriction, container deposit) and scheduled item discounts, plus CSV import with a dry run, search,
and a price list export. The data feeds the pricing engine (#10) unchanged.

## Clarifications

### Session 2026-10-12

- Q: Who changes tax profiles? -> A: Owners only (option A). Managers manage categories, products and
  discounts and may choose a product's tax profile, but cannot create or change tax profiles.
- Q: Price- or weight-embedded barcodes (scale labels)? -> A: Later (option A), with selling (#14) or a
  scale feature. This feature stores normal barcodes and PLU codes only.
- Q: Scheduled price changes? -> A: Immediate only (option A). A new price applies when saved;
  temporary reductions use scheduled item discounts.

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Tax profiles with dated versions, categories, products, barcodes, scheduled item discounts | Multi-buy promotions (#28) |
| CSV import (dry run first) and price list export | Catalog download and offline barcode lookup on Android (#13) |
| Search and filter on the web | Payment types, tables, discount approval limit (#12) |
| Deactivating instead of deleting | Stock levels and receiving (Phase 9) |
| | Images, variants and modifiers, price levels, customer groups |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set up tax profiles and categories (Priority: P1)

An owner sets up how their goods are taxed: profiles such as "HST 13%", "GST 5% + PST 7%", "Zero-rated"
and "Exempt" (or "GST 5%" = CGST 2.5% + SGST 2.5% in India), then categories ("Beverages", "Snacks",
"Produce") each with a default tax profile.

**Why this priority**: every product needs a tax treatment; categories with a default tax profile make
adding products quick and consistent.

**Independent Test**: create profiles of each kind and categories pointing at them; list them; change a
profile's rate from a future date and see both the current and the upcoming version.

**Acceptance Scenarios**:

1. **Given** a manager, **When** they try to create or change a tax profile, **Then** they cannot; they
   can still pick an existing profile for a category or product.
2. **Given** an open business, **When** the owner creates a taxable profile with one or more components
   (label and rate), **Then** it is listed with its total rate and components.
3. **Given** a zero-rated or exempt profile, **When** it is saved, **Then** it has no tax components.
4. **Given** a profile, **When** the owner changes its rates effective from a chosen date, **Then** the
   current version stays in force until that date, the new one applies from it, and both are visible;
   sales already completed keep the tax they were sold with.
5. **Given** categories, **When** the owner sets each one's name, sort order and default tax profile,
   **Then** they are listed in sort order.
6. **Given** a profile or category in use, **When** the owner tries to remove it, **Then** it can only be
   deactivated, and deactivated ones are not offered for new products.

---

### User Story 2 - Add and maintain products (Priority: P1)

A manager adds products: name, category, unit, price, quantity rules, open price, barcodes, age
restriction and deposit; and keeps them up to date (price changes, deactivation).

**Why this priority**: without products nothing can be sold; this is the heart of the catalog.

**Independent Test**: add a counted product with two barcodes, a weighed product (kg, step 0.005), an
open-price product, a 19+ age-restricted product with a 0.10 deposit; find each by name and barcode;
the pricing engine prices a sale of them as the shared cases say.

**Acceptance Scenarios**:

1. **Given** a category, **When** a manager adds a product with a unit price and no tax override,
   **Then** it uses the category's tax profile; with an override, it uses the chosen one.
2. **Given** a weighed product, **When** its unit is kg with minimum 0.005 and step 0.005, **Then** those
   rules are saved and shown (a counted product defaults to minimum 1, step 1).
3. **Given** barcodes, **When** they are added, **Then** UPC-A, EAN-13 and EAN-8 are accepted only with a
   correct check digit, PLU codes (4–5 digits) are accepted for produce, and a barcode already used by
   another product of the same business is refused naming that product.
4. **Given** an open-price product, **When** it is saved, **Then** it needs no unit price (the cashier
   enters it at checkout).
5. **Given** an age-restricted product, **When** it is saved, **Then** a minimum age (e.g. 18, 19, 21) is
   required.
6. **Given** a container deposit per unit, **When** it is saved, **Then** it is a non-negative amount in
   the business currency.
7. **Given** two managers editing the same product, **When** the second saves stale data, **Then** they are
   told it changed and nothing is overwritten.
8. **Given** a product that may appear in past sales, **When** a manager removes it, **Then** it is
   deactivated (hidden from selling, kept for history), and can be reactivated.

---

### User Story 3 - Scheduled item discounts (Priority: P2)

A manager schedules a discount on a product for a period ("20% off cola, 1–7 November", or "0.50 off
each loaf this weekend").

**Why this priority**: common in retail, and the pricing engine already applies item discounts; this
gives them a source.

**Independent Test**: schedule a percent and a fixed discount; ask which discount applies on dates
before, during and after; overlapping periods on the same product are refused.

**Acceptance Scenarios**:

1. **Given** a product, **When** a manager schedules a percentage (0–100) or a fixed amount per unit with
   from and to dates, **Then** it is listed with its period.
2. **Given** a discount, **When** a sale happens inside its period (business local time), **Then** that
   discount is the one in effect; outside it, none.
3. **Given** a period overlapping another discount on the same product, **When** saved, **Then** it is
   refused, naming the other discount.
4. **Given** a fixed discount larger than the price, **When** applied, **Then** the line never goes below
   zero (pricing engine rule).

---

### User Story 4 - Import from a spreadsheet (Priority: P2)

An owner moving from another system uploads a CSV of products, sees a preview that lists every row that
would be created or updated and every error, fixes the file if needed, and then imports. Importing the
same file again changes nothing.

**Why this priority**: shops have hundreds or thousands of products; typing them in is not realistic.

**Independent Test**: upload a 1,000-row file with a few bad rows; the dry run lists each bad row and
field and saves nothing; after fixing, the import creates the products and missing categories;
re-importing reports "no changes".

**Acceptance Scenarios**:

1. **Given** a CSV with the documented columns, **When** previewed, **Then** each row is reported as
   create, update, unchanged or error (with row number, column and reason), and nothing is saved.
2. **Given** a preview with errors, **When** the owner tries to import, **Then** only an error-free file
   can be imported; the import is all or nothing.
3. **Given** rows naming categories that do not exist, **When** imported, **Then** those categories are
   created with the tax profile named in the row (a new category without one is a row error).
4. **Given** a product matched by SKU (or by barcode when no SKU), **When** re-imported with the same
   values, **Then** it is unchanged; with different values, it is updated.
5. **Given** a file another business's ids or names appear in, **When** imported, **Then** nothing of
   another business can be referenced or changed.

---

### User Story 5 - Find products and export a price list (Priority: P3)

A manager searches by name or barcode, filters by category and active state, and exports the price list
(CSV) for printing shelf labels or checking prices.

**Independent Test**: search a 5,000-product catalog by part of a name and by full barcode; filter by
category; export and open the file.

**Acceptance Scenarios**:

1. **Given** a large catalog, **When** a manager types part of a name or a full barcode, **Then** matching
   products are listed within a second.
2. **Given** filters for category and active state, **When** applied, **Then** only matching products are
   listed, paged.
3. **Given** the export, **When** downloaded, **Then** it has one row per active product with name, SKU,
   barcodes, category, unit, price, tax profile, deposit, age restriction, and re-imports cleanly.

---

### Edge Cases

- Business currency with 0 or 3 minor digits: prices and deposits are entered and shown with the right
  number of decimals.
- Tax-inclusive businesses enter shelf prices including tax; tax-exclusive businesses enter prices before
  tax (business setting from #5); the catalog stores what was entered and the engine applies the mode.
- A product whose category is deactivated stays sellable (it keeps its own active flag) but the category
  is not offered for new products.
- A tax profile version effective in the past cannot be edited (sales may have used it); a new version is
  added instead.
- A barcode typed with spaces or dashes is normalized; leading zeros are significant (UPC-A vs EAN-13
  forms of the same code are treated as the same barcode).
- Names: duplicates are allowed for products (different sizes), not for categories or tax profiles
  within a business.
- CSV files with a byte-order mark, quoted fields, commas in names, decimal commas from some locales
  (refused with a clear error unless the column uses a dot), and more than 10,000 rows (refused;
  split the file).
- Owners and managers only; cashiers have no back office; a manager of business A can never see or
  change business B's catalog.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Owners and managers of the open business MUST be able to create, edit, deactivate and
  reactivate categories, products and item discounts; only owners MAY create, change, deactivate or
  reactivate tax profiles (managers may choose a product's tax profile). Cashiers MUST NOT use the
  catalog.
- **FR-002**: A tax profile MUST have a unique name, a kind (taxable, zero-rated, exempt) and, if taxable,
  one or more components (label, rate 0–100 with up to 3 decimals); it is kept as dated versions, the
  version in force at a moment being the latest whose effective date is not after it.
- **FR-003**: A category MUST have a unique name, a sort order and a default tax profile.
- **FR-004**: A product MUST have a name, a category, a unit (piece, g, kg, ml, l), a minimum quantity and
  a step (positive, at most 3 decimals, the minimum a multiple of the step), and either a unit price
  (non-negative) or the open-price flag; optionally a SKU (unique in the business), a tax-profile
  override, barcodes, an age restriction with minimum age, and a deposit per unit.
- **FR-005**: Barcodes MUST be validated (UPC-A, EAN-13, EAN-8 check digits; PLU 4–5 digits) and unique
  within the business. Price- or weight-embedded scale-label barcodes are out of scope (later, with
  selling #14 or a scale feature); codes in the in-store range (EAN-13 starting with 2) are refused with
  that explanation.
- **FR-006**: A price change MUST take effect when saved (no future-dated prices); the catalog history
  keeps the old price. Temporary reductions use scheduled item discounts (FR-007).
- **FR-007**: Scheduled item discounts MUST have a kind (percent 0–100 or fixed per unit ≥ 0) and a
  period (from/to dates in the business's local time); periods on the same product MUST NOT overlap.
- **FR-008**: The catalog MUST provide, for a product at a given moment, exactly the inputs the pricing
  engine needs (unit price, quantity rules, deposit, tax profile with its components in force, item
  discount in force), so selling (#14) and the server's recalculation (#15) read the same values.
- **FR-009**: Concurrent edits MUST be detected (a stale save is refused, nothing overwritten).
- **FR-010**: Nothing a sale may reference (tax profiles, categories, products, barcodes) is deleted;
  it is deactivated and can be reactivated.
- **FR-011**: CSV import MUST preview every row (create, update, unchanged, error with row, column and
  reason) without saving, import only an error-free file, all or nothing, create missing categories,
  match existing products by SKU then barcode, and be idempotent.
- **FR-012**: Search MUST find products by part of the name or a full barcode, filter by category and
  active state, page results, and answer within a second for 10,000 products.
- **FR-013**: The price list export MUST produce a CSV in the import format.
- **FR-014**: Every catalog record MUST belong to one business and every referenced id (category, tax
  profile, product) MUST be checked to belong to the caller's business (Constitution III); isolation
  tests cover every operation.
- **FR-015**: Every change MUST be recorded in an append-only catalog history (who, when, what changed,
  old and new values), including imports (one entry per changed record, plus the import itself).
- **FR-016**: The business MUST have a "deposits are taxable" setting (owners only, default off), which
  the pricing engine uses for container deposits (pricing rules, section 2).

### Key Entities

- **Tax profile** and **tax profile version**: name, kind, active; versions with effective date and
  components (label, rate).
- **Category**: name, sort order, default tax profile, active.
- **Product**: name, SKU, category, unit, unit price or open price, minimum quantity, step, tax-profile
  override, age restriction (minimum age), deposit per unit, active, version.
- **Barcode**: code (normalized), type (UPC-A, EAN-13, EAN-8, PLU), product.
- **Item discount**: product, kind, value, from, to.
- **Catalog change**: append-only history of every change.
- **Import**: who, when, file name, counts, outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An owner sets up 3 tax profiles, 5 categories and 20 products by hand in under 30 minutes.
- **SC-002**: A 1,000-row CSV previews in under 10 seconds and imports in under 30 seconds; re-importing
  the same file reports 0 changes.
- **SC-003**: Search returns results in under 1 second for a 10,000-product catalog.
- **SC-004**: 100% of invalid barcodes in the test set (wrong check digit, wrong length, duplicates) are
  refused with the reason.
- **SC-005**: Prices built from catalog data match the shared pricing cases (the catalog feeds the
  engine without conversion errors).
- **SC-006**: Isolation tests prove no business can read or change another business's catalog through
  any operation, including import.

## Assumptions

- Owners and managers use the web back office; the Android catalog download is #13.
- Prices are stored as the business enters them (tax-inclusive or exclusive per the business setting).
- Dates for tax versions and discounts are calendar days in the business's time zone, starting at local
  midnight.
- One tax profile per product (no per-component overrides); a product without an override follows its
  category, including later changes to the category's default.
- SKU is optional and free text (up to 40 characters); barcodes are the primary scan identifiers.
- CSV is UTF-8, comma-separated, with a header row; a documented template is provided.
