# Research: Business Settings

Decisions for feature #12. Each: decision, rationale, alternatives considered.

## R1. Slices and history

**Decision**: one slice folder per area, `Features/Settings/{PaymentTypes,Tables,Selling}`, following the
catalog (#11): entities and configurations next to the slices, `ConfigureBusinessOwned()`, composite keys,
row-level security, version tokens (409 `settings-changed` on a stale save), and an append-only
`settings_changes` table (who, when, entity, id, action, `{field: [old, new]}`) read on a History page.

**Rationale**: same shape as staff and catalog history; settings are not catalog data, so a separate history
keeps the catalog history about products.

**Alternatives**: reuse `catalog_changes` (mixes two audiences); one JSON document per business (no per-item
versions, conflicting saves of unrelated items).

## R2. Cash is created with the business

**Decision**: creating a business also creates its Cash payment type (`built_in = true`, kind `cash`) and its
selling-settings row (no tables, 0% limit, tips off), in the same transaction (`CreateBusiness`). The
migration backfills both for existing businesses. A partial unique index allows one built-in type per
business; slices refuse deactivating it or changing its kind.

**Rationale**: every till always has a way to take payment; no "first use" code paths.

**Alternatives**: a virtual Cash type not stored (sales could not reference it by id like the others);
creating on first read (a write inside a read, and races).

## R3. Selling settings: one row per business

**Decision**: `selling_settings (business_id PK, uses_tables, discount_limit_percent, discount_limit_minor?,
tips_enabled, tip_presets numeric[], version)`. One `PUT /settings/selling` with the version; owners may
change everything, managers only `usesTables` (any other changed field from a manager → 403, nothing
saved).

**Rationale**: these are single values per business; a separate table (not `businesses` columns) keeps
row-level security simple for managers and keeps business identity and selling rules apart.

**Alternatives**: columns on `businesses` (managers cannot update that row; owner-read policy complexity);
one row per setting (no single version for related values).

## R4. Payment type kinds and flags

**Decision**: kinds `cash` (built in only), `externalCard`, `digitalWallet` (UPI and similar), `cheque`,
`voucher`, `other`. `gives_change` is not stored: it is true exactly for `cash` (FR-003). `opens_drawer`
defaults on for cash and off otherwise; `asks_reference` defaults on for `externalCard`, `digitalWallet` and
`cheque`. Integrated providers (#21, #22) will add a `provider` column later; not now.

## R5. Tables and bulk add

**Decision**: `table_areas` and `dining_tables` (avoids the SQL keyword `table`). Names unique per business
ignoring case (normalized column, as in the catalog). Bulk add: `POST /settings/tables/bulk { areaId,
from, to, prefix?, suffix?, seats }` with `1 ≤ from ≤ to`, `to − from < 200`; names `prefix + n + suffix`;
all names checked first; any clash refuses the whole request and lists the clashes. Positions continue
after the area's last table.

**Alternatives**: client-side loop of single creates (partial results on failure; 200 round trips).

## R6. Tills get settings with the catalog

**Decision**: `CatalogSync` gains `paymentTypes[]`, `tableAreas[]`, `tables[]` (whole rows, changed-since like
catalog roots) and a `selling` object (always sent, like `business`). The three tables get `revision`
columns stamped by the existing `pos.stamp_catalog_revision()` trigger (research #13 R1). On the till, Room
stores them (database version 2); the copy is cleared and fully downloaded again when the database version
changes, so an existing copy never lacks rows that were not "changed" after its revision.

**Rationale**: tills must have payment types and limits offline (selling #14 uses them); one download, one
change-tracking mechanism.

**Alternatives**: a separate settings endpoint (second refresh path, second staleness state); storing them on
the till only in #14 (a copy refreshed in between would never receive unchanged rows, so #14 would need a
forced full download anyway).

## R7. The discount rule in the pricing rules

**Decision**: `specs/pricing-rules.md` section 4 gains: "A cashier may give discounts (order discount plus
item discounts entered at the till) up to the business's limit: a percentage of the sale before those
discounts and, when set, an amount. Above either, a manager or owner approves at the till. Changing the price
of a product that is not open-price always needs approval. New businesses start at 0%." The deferral is
removed. The engine is unchanged (it computes amounts; the till enforces approval in #14).

## R8. Kernel rules shared with the web

**Decision**: `HelloPos.Kernel.Settings.SettingsRules`: name lengths (payment type 40, area 40, table 20),
seats 1–50, limit percent 0–100 with ≤ 2 decimals, tip presets 1–3 distinct values 1–100 with ≤ 2 decimals,
bulk range rules and name generation. The web uses the same messages before sending.

## R9. Web pages

**Decision**: a **Settings** menu group (owners and managers): Payment types (list with drag-free up/down
ordering, add/edit, deactivate/reactivate; managers read-only), Tables (switch, areas, tables, bulk add),
Discounts and tips (owners edit, managers read-only), History. MudBlazor as elsewhere; bUnit tests with a
fake service.
