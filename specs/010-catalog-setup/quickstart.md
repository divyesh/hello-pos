# Quickstart: Catalog Setup

## Automated

- Backend (`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`): tax profiles and versions (owner
  only, future versions editable, version in force by date), categories, products (quantity rules, open
  price, age, deposit, override), barcodes (check digits, UPC-A/EAN-13 equivalence, in-store ranges
  refused, duplicates refused), item discounts (no overlap, in force by date), search (name part,
  barcode, SKU, filters, paging), CSV preview/import/idempotence/all-or-nothing, export round trip,
  catalog history, isolation for every operation (including ids from another business in an import),
  the pricing feed priced by the engine, contract conformance.
- Kernel: `BarcodeRules` against `contracts/barcode-cases.json`.
- Web (`scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx`): catalog pages (bUnit).

## Manual (local environment)

1. `scripts/env.ps1 up --api`; run the web app; sign in as an owner with a business (CAD, tax-exclusive).
2. Catalog → Tax profiles: "HST 13%", "Zero-rated", "Exempt"; add a version of HST from next month at
   15% and see both listed. Sign in as a manager: profiles are read-only.
3. Categories: Beverages (HST), Produce (Zero-rated).
4. Products: "Cola 355 ml" 1.99, deposit 0.10, barcode 0 67000 00456 8 (UPC-A); "Bananas" kg, 0.69,
   min/step 0.005, PLU 4011; "Beer 6-pack" 19+, deposit 0.60. Try a wrong check digit and a code starting
   with 2: refused with the reason.
5. Discount: 20% off Cola this week; try an overlapping one: refused.
6. Search "col", search the barcode, filter Produce.
7. Export the price list; import it again: preview shows all unchanged. Edit a price and a bad row in
   the file: preview lists the bad row; fix it; import; history shows the change.
8. SC-001: time setting up 3 profiles, 5 categories and 20 products by hand (< 30 minutes).
