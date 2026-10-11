# Data Model: Tenancy and API Infrastructure

Schema `pos`, owned by `hellopos_migrator`. Data rights granted to group role `pos_app`.

## pos.businesses (production, tenant root)

| Column | Type | Rules |
|--------|------|-------|
| `id` | `uuid` | PK; UUIDv7; default `uuidv7()` |
| `name` | `text` | required, 1–200 chars |
| `country_code` | `char(2)` | `IN`, `US` or `CA` |
| `currency_code` | `char(3)` | ISO 4217, upper case (for example `INR`, `USD`, `CAD`) |
| `pricing_mode` | `text` | `tax_inclusive` or `tax_exclusive` |
| `financial_year_start_month` | `smallint` | 1–12 |
| `time_zone` | `text` | IANA name (for example `Asia/Kolkata`) |
| `created_at` | `timestamptz` | UTC, set on insert |
| `updated_at` | `timestamptz` | UTC, set on every update |

RLS: `business_scope` — `id = pos.current_business_id()` for read and write.

## Test-only sample tables (never in production migrations)

Shaped like the old system's categories → products (spec FR-017).

### sample_categories

| Column | Type | Rules |
|--------|------|-------|
| `business_id` | `uuid` | → `businesses(id)` |
| `id` | `uuid` | PK `(business_id, id)` |
| `name` | `text` | required |
| `created_at` | `timestamptz` | UTC |

### sample_products

| Column | Type | Rules |
|--------|------|-------|
| `business_id` | `uuid` | → `businesses(id)` |
| `id` | `uuid` | PK `(business_id, id)` |
| `category_id` | `uuid` | FK `(business_id, category_id)` → `sample_categories(business_id, id)` |
| `name` | `text` | required |
| `unit_price` | `numeric(19,4)` | ≥ 0 |
| `created_at` | `timestamptz` | UTC |

Both: RLS `business_scope` on `business_id`.

## Rules every business-owned table follows (checked automatically, R11)

1. Non-null `business_id uuid`, FK to `pos.businesses(id)`.
2. Primary key `(business_id, id)`, `id` UUIDv7.
3. Every FK to another business-owned table is `(business_id, x_id) → (business_id, id)`.
4. `ENABLE ROW LEVEL SECURITY` with policy `business_scope`.
5. Timestamps `timestamptz`; money `numeric(19,4)`; no `real`/`double precision`.

## Session state

| Setting | Set by | Meaning |
|---------|--------|---------|
| `pos.business_id` | connection interceptor on open; reset on close; `DISCARD ALL` on pool return | current business for RLS; empty = none (no rows visible or writable) |
