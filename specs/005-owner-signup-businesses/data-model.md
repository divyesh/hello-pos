# Data Model: Owner Sign-up and Businesses

## Schema `identity` (platform data, not business-owned; owned by the migrator)

Identity tables (`owner_accounts`, `owner_logins`, `owner_tokens`, `owner_claims`, ...) with snake_case
names. Key fields of **owner_accounts**:

| Column | Type | Rules |
|--------|------|-------|
| `id` | `uuid` | UUIDv7 |
| `display_name` | `text` | 1–100 chars |
| `email` / `normalized_email` | `text` | unique (normalized, case-insensitive) |
| `email_confirmed` | `boolean` | must be true to create/open businesses |
| `password_hash` | `text` | PBKDF2 (Identity) |
| `security_stamp` | `text` | changes on password reset and email confirmation (invalidates tokens and sessions) |
| `lockout_end`, `access_failed_count` | | 5 failures → 15 min |
| `created_at` | `timestamptz` | UTC |

`data_protection_keys`: Data Protection key ring (R4).

## Schema `pos`

### businesses (extended)

| Column | Change |
|--------|--------|
| `cash_rounding_step` | new, `numeric(19,4)`, one of `0`, `0.05`, `0.10`, `1.00`; default `0` |

RLS policy `business_scope` (replaced): visible/writable when `id = current_business_id()` **or** the
current owner owns it (via `business_owners`).

### business_owners (new, business-owned)

| Column | Type | Rules |
|--------|------|-------|
| `business_id` | `uuid` | → `businesses(id)`; PK `(business_id, id)` |
| `id` | `uuid` | UUIDv7 |
| `owner_id` | `uuid` | the owner account (no FK across schemas; enforced by the API) |
| `created_at` | `timestamptz` | UTC |

Unique `(business_id, owner_id)`. RLS `business_scope`: `business_id = current_business_id() OR
owner_id = current_owner_id()`.

## Session settings

| Setting | Source | Meaning |
|---------|--------|---------|
| `pos.business_id` | cookie claim `business_id` (opened business) | feature 003 |
| `pos.owner_id` | cookie claim `owner_id` (signed-in owner) | lets the owner see the businesses they own |

## Country defaults (kernel)

| Country | Currency | Pricing | FY start | Time zone | Cash rounding |
|---------|----------|---------|----------|-----------|---------------|
| IN | INR | tax-inclusive | 4 | Asia/Kolkata | 1.00 |
| US | USD | tax-exclusive | 1 | America/New_York | 0 (none) |
| CA | CAD | tax-exclusive | 1 | America/Toronto | 0.05 |

## Validation (kernel `BusinessSettingsRules`, used by API and web)

name 1–200 chars; country ∈ {IN, US, CA}; currency `^[A-Z]{3}$`; pricing ∈ {tax_inclusive,
tax_exclusive}; financial-year month 1–12; time zone a known IANA id; rounding step ∈ {0, 0.05, 0.10, 1.00}.
