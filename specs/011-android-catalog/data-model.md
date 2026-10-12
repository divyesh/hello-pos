# Data Model: Catalog on Android

## Server (PostgreSQL, schema `pos`)

All new tables are business-owned: `business_id` in the key, row-level security, composite foreign keys
(Constitution III).

### `catalog_removals` (new, append-only for the app login)

| Column | Type | Rules |
|--------|------|-------|
| business_id | uuid | PK part |
| id | uuid | PK part |
| entity | text | `item_discount` (the only removable root today) |
| entity_id | uuid | the removed row |
| revision | bigint | id of the removing transaction (written by a trigger); index `(business_id, revision)` |

### Changed tables

| Table | New column | Stamped when |
|-------|------------|--------------|
| tax_profiles | revision bigint not null default 0 | the profile, any of its versions or their components change (triggers) |
| categories | revision bigint not null default 0 | the category changes |
| products | revision bigint not null default 0 | the product or its barcodes change |
| item_discounts | revision bigint not null default 0 | the discount changes |

Each gets an index `(business_id, revision)`. Revisions are transaction ids set by triggers (research R1);
existing rows keep 0 and reach tills through their first full download.

## Wire: `CatalogSync` (contracts/openapi-additions.yaml)

| Field | Notes |
|-------|-------|
| mode | `full` (replace everything) or `changes` (apply on top of `since`) |
| revision | int64; the oldest transaction running at the read; the next `since` |
| serverTime | date-time; for the clock check |
| business | currency, pricingMode, timeZone, cashRoundingStep (minor units), depositsTaxable |
| taxProfiles[] | id, name, kind, active, versions[] { id, effectiveFrom, components[] { label, ratePercent } } |
| categories[] | id, name, sortOrder, defaultTaxProfileId, active |
| products[] | id, name, sku?, categoryId, taxProfileId?, unit, unitPrice? (minor), openPrice, minQuantity, step, minAge?, depositPerUnit (minor), active, barcodes[] (normalized codes) |
| discounts[] | id, productId, kind (`percent`, `fixed`), value (percent string or minor units string), from, to? |
| removedDiscountIds[] | only in `changes` |

Money is int64 minor units of `business.currency`; quantities and rates are invariant decimal strings
(Constitution I), as in the catalog API.

## Till (Room database `catalog.db`)

| Table | Columns | Notes |
|-------|---------|-------|
| catalog_meta | single row: business_id, revision, refreshed_at, server_offset_ms, currency, pricing_mode, time_zone, cash_rounding_step, deposits_taxable | business_id checked against the paired business on open (R7) |
| tax_profiles | id PK, name, kind, active | |
| tax_versions | id PK, tax_profile_id (index), effective_from, components_json | replaced with their profile |
| categories | id PK, name, sort_order, default_tax_profile_id, active | |
| products | id PK, name, search_name (index), sku, category_id (index), tax_profile_id, unit, unit_price, open_price, min_quantity, step, min_age, deposit_per_unit, active | `search_name`: lower case, accents removed |
| barcodes | code PK, product_id (index) | replaced with their product; code is the normalized form |
| discounts | id PK, product_id (index), kind, value, from_date, to_date | |

### Apply rules (one transaction)

- `full`: delete all rows, insert everything, write `catalog_meta`.
- `changes`: for each profile: delete its versions, upsert profile and versions; for each product:
  delete its barcodes, upsert product and barcodes; upsert categories and discounts; delete
  `removedDiscountIds`; write `catalog_meta` (revision, refreshed_at, offset, settings).
- On any error the transaction rolls back and the previous copy stays.

### Domain types (Kotlin, `domain/catalog`)

- `CatalogProduct`, `CatalogCategory`, `CatalogTaxProfile` (with `versions`), `CatalogDiscount`,
  `CatalogSettings`: plain data mapped from Room.
- `BarcodeRules` (`parse(input) → Barcode | BarcodeProblem`, `message(problem)`), same as the kernel.
- `CatalogPriceFeed`: `line(product, at, quantity, enteredPrice?) → LineInput`, `sale(lines) → SaleInput`.
- `ProductLookup` result: `Found(product)`, `Invalid(message)`, `NotFound`, `NotOffered` (inactive).
