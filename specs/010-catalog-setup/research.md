# Research: Catalog Setup

Decisions for feature #11. Builds on tenancy (#3), staff and roles (#6), the pricing engine (#10).

## R1. Tables (all business-owned, row-level security, composite keys)

`pos.tax_profiles`, `pos.tax_profile_versions`, `pos.tax_components`, `pos.categories`, `pos.products`,
`pos.product_barcodes`, `pos.item_discounts`, `pos.catalog_changes` (append-only), `pos.catalog_imports`.
Every table has `business_id`; every link includes it (composite foreign keys), so a product can never
point at another business's category or tax profile even if code forgot a check (Constitution III).
Ids are UUIDv7. Details: [data-model.md](data-model.md).

## R2. Tax profiles are versioned by effective date

- A profile (name, kind, active) has one or more versions: `effective_from` (a calendar date in the
  business's time zone) and its components (label, rate `numeric(6,3)`).
- The version in force at an instant is the one with the latest `effective_from` not after the instant's
  local date. The first version's date is the profile's creation date (or earlier if the owner says so).
- Versions whose date has arrived are immutable (sales may have used them); future versions may be
  edited or removed. "Changing a rate" = adding a version.
- Kind is fixed after creation (a taxable profile cannot become exempt; create a new profile instead).
- **Alternatives**: overwrite in place plus history table (completed sales would still be safe, but
  pre-announced rate changes could not be entered ahead of time); validity ranges (from/to) instead of
  from-only (gaps and overlaps to police).

## R3. Money and quantities

- Prices and deposits are stored as `numeric(19,4)` in currency units, sent as int64 minor units with the
  business currency (Constitution I); the API converts with the currency's digits (`Currencies` from
  #10) and refuses values that do not fit the currency (e.g. fractions of a yen are impossible because
  minor units are integers).
- Quantities (`min_quantity`, `step`) are `numeric(9,3)`, sent as invariant decimal strings; the minimum
  must be a positive multiple of the step.
- The kernel has two `PricingMode` enums (business settings from #5, pricing engine from #10); the catalog
  pricing feed maps between them in one place.

## R4. Barcodes

- A shared kernel module `Catalog/BarcodeRules` (used by the API and, for instant feedback, the web
  app) normalizes and validates: strip spaces and dashes; digits only; UPC-A (12), EAN-13 (13), EAN-8 (8)
  with the GS1 mod-10 check digit; UPC-A is stored in its 13-digit form (leading 0) so the same code
  typed as UPC-A or EAN-13 is one barcode; PLU: 4 digits (3000–4999) or 5 digits (9 + 4-digit, organic).
- In-store/variable-measure ranges are refused with an explanation (clarification Q2): EAN-13 prefixes
  20–29, UPC-A number system 2.
- Unique per business on the normalized code (unique index `(business_id, code)`).
- Kotlin will need the same rules for scanning (#13/#14); they get shared cases then
  (`contracts/barcode-cases.json` is created here with the .NET tests so #13 starts from them).

## R5. Feeding the pricing engine

- `CatalogPricing` (API, `Features/Catalog/`) answers, for product ids and an instant: unit price (or
  open price), quantity rules, deposit, tax profile `{ name, kind, components in force }`, item discount
  in force, plus the business's pricing mode, currency, cash step and deposits-taxable flag. It returns
  the kernel's `LineInput`/`SaleInput` pieces directly, so selling (#14) and the server recalculation
  (#15) use identical values. Tested by pricing a catalog-built sale and comparing with a shared case.
- The tax profile name is the engine's grouping key for tax-inclusive sales; names are unique per
  business, so grouping is correct.

## R6. Scheduled item discounts

- `from_date`, `to_date` inclusive calendar dates in the business's time zone (to may be open-ended).
- No overlaps per product: checked in a transaction that locks the product row (like `LastOwnerGuard`),
  plus an exclusion constraint on `(business_id, product_id, daterange)` with `btree_gist` as the
  database-level guarantee.
- Past discounts are kept; a running discount can be ended early (to = today), a future one deleted.

## R7. Search

- Name: `pg_trgm` GIN index on `lower(name)`, `ILIKE '%term%'` (the extension is trusted in PostgreSQL
  13+, created by the migration; available on DigitalOcean Managed PostgreSQL).
- Barcode: exact match on the normalized code. SKU: exact, case-insensitive.
- Filters: category, active. Keyset or offset paging (offset is fine at 10,000 products), 50 per page.

## R8. CSV import and export

- Format: UTF-8 (BOM tolerated), comma-separated, RFC 4180 quoting, header row. Columns: `sku`, `name`,
  `category`, `category_tax_profile`, `tax_profile`, `unit`, `price`, `open_price`, `min_quantity`,
  `step`, `barcodes` (separated by `|`), `min_age`, `deposit`, `active`. Prices and quantities use a dot
  decimal separator. ≤ 10,000 rows, ≤ 5 MB.
- A small RFC 4180 parser in the API (no new dependency, Constitution X).
- **Preview** (`POST /catalog/imports/preview`, multipart): parses and validates every row against the
  current catalog and returns per-row outcomes; saves nothing.
- **Import** (`POST /catalog/imports`): the client sends the same file; the API re-validates (the
  catalog may have changed since the preview), refuses if any row has an error, and applies everything
  in one transaction. Matching: SKU, then any barcode, then (only for a row with neither) an exact name that exactly one SKU-less product has; an ambiguous name is a row error. Unchanged rows are not touched. An import record
  and one catalog change per changed record are written.
- Export (`GET /catalog/products/export`) writes the same columns, so an export re-imports with 0
  changes.

## R9. Roles

- Catalog endpoints: `RequireRole(Owner, Manager)` + `RequireSameBusiness()` (feature 006).
- Tax profile writes and the deposits-taxable setting: `RequireRole(Owner)`; managers can read profiles.

## R10. Audit

- `pos.catalog_changes`: entity type, entity id, action, changed fields (old → new, JSON), actor staff
  id, time, optional import id. `UPDATE`/`DELETE` revoked for the app login (like `staff_changes`).

## R11. Web

- Back-office "Catalog" area (owners and managers): Tax profiles (owner edits), Categories, Products
  (search, filters, paging, edit page with barcodes and discounts), Import (upload, preview table,
  import), Export. MudBlazor, Kiota client, bUnit tests, as in features 005–007.

## R12. Business setting

- `businesses.deposits_taxable boolean not null default false`, editable by owners on the existing
  business settings page (feature 005's update operation gains the field).
