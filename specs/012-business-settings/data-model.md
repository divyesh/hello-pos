# Data Model: Business Settings

All tables are business-owned: `business_id` in the primary key, row-level security, composite foreign keys
(Constitution III). Names are unique per business through a lower-cased `normalized_name`.

## `payment_types`

| Column | Type | Rules |
|--------|------|-------|
| id | uuid | |
| name, normalized_name | varchar(40) | 1–40 characters, unique per business |
| kind | varchar(14) | `cash`, `externalCard`, `digitalWallet`, `cheque`, `voucher`, `other` (stored snake_case) |
| built_in | bool | true only for the business's Cash; partial unique index `(business_id) WHERE built_in` |
| opens_drawer | bool | Cash: true |
| asks_reference | bool | |
| position | int | |
| active | bool | built-in is always active (check constraint) |
| version | int | concurrency token |
| revision | bigint | trigger-stamped (feature #13) |
| created_at, updated_at | timestamptz | |

Check: `kind = 'cash'` if and only if `built_in`. At most 20 active types (checked by the slice).

## `table_areas`

| Column | Type | Rules |
|--------|------|-------|
| id | uuid | |
| name, normalized_name | varchar(40) | unique per business |
| position | int | |
| active | bool | cannot become false while it has active tables (slice) |
| version, revision, created_at, updated_at | | as above |

## `dining_tables`

| Column | Type | Rules |
|--------|------|-------|
| id | uuid | |
| area_id | uuid | FK `(business_id, area_id)` → `table_areas` |
| name, normalized_name | varchar(20) | unique per business |
| seats | smallint | 1–50 |
| position | int | order within the area |
| active, version, revision, created_at, updated_at | | as above |

## `selling_settings` (one row per business)

| Column | Type | Rules |
|--------|------|-------|
| business_id | uuid | PK, FK → businesses |
| uses_tables | bool | default false |
| discount_limit_percent | numeric(5,2) | 0–100, default 0 |
| discount_limit_amount | numeric(19,4) null | ≥ 0; minor units on the wire |
| tips_enabled | bool | default false |
| tip_presets | numeric(5,2)[] | 0–3 distinct values 1–100, ascending |
| version | int | |

## `settings_changes` (append-only for the app login)

| Column | Type | Rules |
|--------|------|-------|
| id | uuid | |
| at | timestamptz | |
| actor_staff_id | uuid | FK `(business_id, actor_staff_id)` → staff |
| entity | varchar(20) | `paymentType`, `tableArea`, `table`, `selling` |
| entity_id | uuid | business id for `selling` |
| action | varchar(20) | `created`, `updated`, `deactivated`, `reactivated`, `bulkCreated` |
| changes | jsonb | `{field: [old, new]}` |

## Wire additions to `CatalogSync` (feature #13)

| Field | Content |
|-------|---------|
| paymentTypes[] | id, name, kind, builtIn, opensDrawer, asksReference, givesChange, position, active |
| tableAreas[] | id, name, position, active |
| tables[] | id, areaId, name, seats, position, active |
| selling | usesTables, discountLimitPercent (string), discountLimitAmount? (minor), tipsEnabled, tipPresets[] (strings) |

In `changes` mode the three lists hold rows changed after `since`; `selling` is always sent.

## Till (Room, database version 2)

New tables `payment_types`, `table_areas`, `dining_tables` (columns as on the wire) and `catalog_meta` gains
the selling values. Version 1 → 2 is destructive: the copy is dropped and fully downloaded again (it is
only a copy of the server).
