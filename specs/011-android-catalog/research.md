# Research: Catalog on Android

Decisions for feature #13. Each: decision, rationale, alternatives considered.

## R1. Change tracking: transaction ids stamped by triggers

**Decision** (revised during implementation; the first design is kept below as rejected): every catalog root
(`tax_profiles`, `categories`, `products`, `item_discounts`) has a `revision bigint` set by a `BEFORE INSERT OR
UPDATE` trigger to the writing transaction's id (`pg_current_xact_id()`, 64-bit, never wraps). Children stamp
their root through `AFTER` triggers (tax version or component → profile, barcode → product), and deleting a
discount inserts a row into `catalog_removals (business_id, id, entity, entity_id, revision)`.

A download reads, in the same snapshot as the data, `pg_snapshot_xmin(pg_current_snapshot())`: the oldest
transaction still running at that moment. That number is the till's next `since`; a refresh returns rows with
`revision >= since`. Every transaction below it had finished before the read, so its rows were in the snapshot;
every one at or above it is picked up next time. A change that commits after the read is therefore never
skipped, whatever order transactions started or committed in. Rows changed by transactions still running at the
read may be sent twice, which is harmless (whole aggregates are upserted).

**Rationale**: no locks, so no deadlocks between catalog writes; triggers mean no slice, import or future
feature can forget to stamp; one place in the database to test.

**Tests**: every write operation of #11 (tax profile create, rename, version add and delete; category update,
stale update refused, deactivate; product update with only barcodes changed, deactivate; discount create,
update, delete; CSV import) stamps exactly the changed roots, and a removal row is written for a deleted discount.

**Alternatives**: a per-business counter row locked by each catalog transaction (the planned design; correct,
but a transaction holding a product lock and waiting for the counter can deadlock with one holding the counter
and updating that product); the `catalog_changes` history id or `updated_at` as a cursor (assigned before
commit, so a till can skip a change that commits late); explicit calls in every slice (easy to miss).

## R2. One endpoint for full and incremental downloads

**Decision**: `GET /pos/catalog?since={revision}` returns a `CatalogSync`:
`mode` (`full` or `changes`), `revision`, `serverTime`, the business pricing settings (always), and
lists of tax profiles (each whole, with all versions and components), categories, products (each whole,
with barcodes), discounts, and `removedDiscountIds`. Without `since`, or when `since` is newer than the
business's revision (restored database) (beyond the database's current transaction ids: a restored database or another server), the server answers
`full`. Removals are never pruned today; pruning them later would add an "oldest answerable" check here.

The whole response is read in one `REPEATABLE READ` transaction, so it is one consistent revision.

**Rationale**: one round trip, whole aggregates (a product with all its barcodes) keep the device's
apply step simple: upsert the roots, replace their children, delete removed ids. Business settings are
small and always sent, so changing them needs no revision.

**Alternatives**: separate snapshot and changes endpoints (same data twice in the contract); field-level
patches (complex apply, little saving); paging (the device must apply all at once anyway; 10,000
products is a few megabytes uncompressed).

## R3. Which discounts a full download contains

**Decision**: current and future discounts (`to_date` null or ≥ yesterday in the business's time zone).
An incremental download sends every changed discount. The device ignores ended discounts when pricing
and drops them on the next full download.

**Rationale**: a till offline for days must price correctly as discounts start; ended discounts never
change a price again. "Yesterday" covers the gap between server and device local dates near midnight.

## R4. Authorization: the device credential

**Decision**: the endpoint uses `PosAuthentication.DevicePolicy` (a paired, not revoked device), like
`/pos/device` and offline access; the business comes from the device's credential. A staff session is
not required (spec FR-002, amended at planning). Revoked devices get 401 and the app forgets everything
(existing `DeviceRepository.verify`).

**Rationale**: the catalog belongs to the business; refreshing it on the lock screen and between shifts
keeps tills current. Cashiers cannot change anything through it (read only).

## R5. Response compression

**Decision**: enable ASP.NET Core response compression (Brotli and gzip) for `/pos/catalog` only.
OkHttp decompresses gzip transparently.

**Rationale**: SC-001 (10,000 products in under 60 s on a shop connection); JSON of this shape compresses
about 8–10×. Limited to this endpoint: it reflects no secrets and no attacker-controlled input, so
compression side channels (BREACH) do not apply; other endpoints are unchanged.

## R6. Storage on the till: Room

**Decision**: a Room database `catalog.db` (Room is in the constitution's Android stack) with tables
`catalog_meta` (business id, revision, last refresh, server time offset, pricing settings), `tax_profiles`,
`tax_versions` (components as a JSON column: always read whole), `categories`, `products` (with a
`search_name`: lower-cased, accents removed), `barcodes` (`code` primary key → `product_id`) and
`discounts`. Applying a download is one Room transaction: `full` clears and inserts; `changes` upserts
roots, replaces their children and deletes removed ids. Screens observe Room flows, which emit only after
the transaction commits, so no screen sees a half-applied state (FR-007).

Not encrypted (clarification Q2); session secrets stay in the existing encrypted file store.

**Alternatives**: a single JSON file replaced atomically (simple and atomic, but lookups need the whole
catalog in memory and name search scans everything); DataStore (not for relational data).

## R7. Erasing on unpair and business change

**Decision**: `DeviceRepository.forget()` also erases the catalog (clear all tables, then `VACUUM` so
the data does not stay in free pages). As a second guard, the catalog repository compares
`catalog_meta.business_id` with the paired business on every open and erases on mismatch before
returning anything (FR-019, SC-007).

## R8. Barcode rules and lookup on the till

**Decision**: port `HelloPos.Kernel.Catalog.BarcodeRules` to Kotlin (`domain/catalog/BarcodeRules.kt`),
same messages, and run every case of `contracts/barcode-cases.json` in the Kotlin suite (as with the
pricing cases). Lookup parses the input: a refused code shows the rule's message; a valid one is looked
up by its normalized form, so UPC-A and EAN-13 (and 12 or 13 digits from a scanner) find the same product.
Uniqueness per business already holds on the server (#11 stores UPC-A as EAN-13); a server test adds the
two-form case (FR-012).

## R9. Prices on the till: a Kotlin price feed

**Decision**: `domain/catalog/CatalogPriceFeed.kt` mirrors the server's `CatalogPricing`: for a product
and an instant, take the business-local date (business time zone), the tax version in force on it (latest
`effectiveFrom` ≤ date) of the product's profile or its category's default, and the active discount; build
the pricing engine's `LineInput` and `SaleInput` with the business's pricing mode, currency, cash rounding
and deposit setting. Pure Kotlin in `domain` (no Android imports).

**Parity (FR-017, SC-005)**: a new shared file `contracts/catalog-pricing-cases.json` (with schema) holds a
catalog in the `CatalogSync` wire shape and checks `{product, at, quantity, enteredPrice?, expected:
unit price, discount, tax, line total}`, hand-derived like the pricing cases. The .NET suite inserts the
catalog into a test business, compares `GET /pos/catalog` with the file, and prices each check through
`CatalogPricing.ForAsync` and the engine; the Kotlin suite applies the same file to its catalog copy and
prices through `CatalogPriceFeed`. Both must give the expected values. Cases cover tax-inclusive and
exclusive, multi-component tax, zero-rated and exempt, category default and product override, percent and
fixed discounts starting and ending, a tax rate change on a date across midnight in the business zone,
deposits taxed and not, open price, weighed quantity.

## R10. When the till refreshes

**Decision**: one `CatalogSync` service with a mutex (concurrent triggers share one run):
after a successful sign-in, every 5 minutes while the app is in the foreground (a coroutine tied to the
process lifecycle), on "Refresh now", and on app start when paired. Failures keep the copy and are
retried at the next trigger. WorkManager is not used: refreshing in the background with the app closed
brings nothing for this feature.

**Staleness and clock**: the screen warns when the last success is over 24 hours ago (clarification Q1,
never blocks) and when the device clock differs from `serverTime` by more than 5 minutes.

## R11. Testing Room on the JVM

**Decision**: DAO and apply tests run as local unit tests with Robolectric (JUnit 4 runner through the
JUnit Vintage engine alongside the existing JUnit 5 tests), so CI's `testDebugUnitTest` covers them.
Repository and sync logic are also tested with fakes; domain code (barcodes, price feed) is plain JVM.

**Alternatives**: instrumented tests (not run in CI; no device there); an in-memory fake only (would not
prove the transaction and the index).

## R12. Read-only catalog screen

**Decision**: `feature/catalog` with `CatalogScreen`, `CatalogViewModel`, one `CatalogUiState`
(constitution: ViewModels do not call Room or Retrofit directly; they use `CatalogRepository`). Reached from
the home screen. Category chips in sort order, a search field (name, or a code: all digits → barcode
lookup; a scanner acting as a keyboard ends with Enter), a product list, and a product detail with price
(regular and discounted), unit and step, tax profile and rate, age, deposit, open price. A header shows
"Catalog updated …", the 24-hour and clock warnings, and "Refresh now".
