# Data Model: Catalog Setup

All tables are in schema `pos`, business-owned (`business_id` first in every key and every link),
with row-level security (`EnableBusinessRowSecurity`). Times are `timestamptz`; dates are calendar
dates in the business's time zone. Money: `numeric(19,4)` (minor units on the wire).

## tax_profiles

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | key (business_id, id) |
| name | text ≤ 60 | unique per business (case-insensitive) |
| kind | text | `taxable` \| `zero_rated` \| `exempt`; fixed after creation |
| active | bool | |
| version | int | concurrency token |
| created_at, updated_at | timestamptz | |

## tax_profile_versions

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| tax_profile_id | uuid | → tax_profiles (business_id, id) |
| effective_from | date | unique per profile; immutable once ≤ today (business local) |

## tax_components

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| tax_profile_version_id | uuid | → tax_profile_versions |
| position | smallint | order shown and passed to the engine |
| label | text ≤ 20 | unique within the version |
| rate_percent | numeric(6,3) | 0–100 |

Taxable profiles' versions have ≥ 1 component; zero-rated and exempt have none.

## categories

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| name | text ≤ 60 | unique per business (case-insensitive) |
| sort_order | int | |
| default_tax_profile_id | uuid | → tax_profiles (business_id, id) |
| active, version, created_at, updated_at | | |

## products

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| name | text ≤ 120 | `pg_trgm` index on lower(name) |
| sku | text ≤ 40, null | unique per business (case-insensitive) when present |
| category_id | uuid | → categories |
| tax_profile_id | uuid, null | override → tax_profiles; null = category default |
| unit | text | `piece` \| `g` \| `kg` \| `ml` \| `l` |
| unit_price | numeric(19,4), null | ≥ 0; null iff open_price |
| open_price | bool | |
| min_quantity, step | numeric(9,3) | > 0; min a multiple of step; defaults 1/1 |
| min_age | smallint, null | 1–99; null = not age-restricted |
| deposit_per_unit | numeric(19,4) | ≥ 0, default 0 |
| active, version, created_at, updated_at | | |

## product_barcodes

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| product_id | uuid | → products |
| code | text | normalized (UPC-A stored as 13 digits); unique (business_id, code) |
| kind | text | `ean13` \| `ean8` \| `plu` (UPC-A is an EAN-13 with a leading 0) |

## item_discounts

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| product_id | uuid | → products |
| kind | text | `percent` \| `fixed` |
| value | numeric(19,4) | percent 0–100 (3 decimals) or a fixed amount per unit ≥ 0 |
| from_date | date | |
| to_date | date, null | inclusive; null = open-ended; ≥ from_date |
| created_at | timestamptz | |

Exclusion constraint: no two rows for the same (business_id, product_id) with overlapping
`daterange(from_date, to_date, '[]')` (`btree_gist`).

## catalog_changes (append-only)

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| at | timestamptz | |
| actor_staff_id | uuid | → staff_members |
| entity | text | `taxProfile`, `taxProfileVersion`, `category`, `product`, `barcode`, `itemDiscount`, `business` |
| entity_id | uuid | |
| action | text | `created`, `updated`, `deactivated`, `reactivated`, `deleted` (future versions/discounts only), `imported` |
| changes | jsonb | `{ field: [old, new] }`; never secrets |
| import_id | uuid, null | → catalog_imports |

`UPDATE` and `DELETE` revoked for the app login.

## catalog_imports

| Column | Type | Rules |
|--------|------|-------|
| business_id, id | uuid | |
| at, actor_staff_id | | |
| file_name | text ≤ 200 | |
| rows, created, updated, unchanged | int | |

## businesses (changed)

| Column | Type | Rules |
|--------|------|-------|
| deposits_taxable | bool | default false; owners only |

## State rules

- Deactivated tax profiles and categories are not offered for new links; existing links keep working.
- A product is sellable iff it is active (its category's active flag does not hide it).
- Tax version in force at instant t: latest `effective_from` ≤ local date of t.
- Item discount in force at t: the one whose range contains the local date of t (at most one).
