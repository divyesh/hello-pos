# Quickstart: Catalog on Android

Validates feature #13 end to end. Prerequisites: the local environment (`podman compose up`), the web back
office, an Android emulator or tablet, and a business with a catalog set up as in
`specs/010-catalog-setup/quickstart.md` (at least: two tax profiles, one with a future rate change; three
categories; 20 products including a weighed one, an open-price one, one with a deposit, one age-restricted,
UPC-A and EAN-13 barcodes and a PLU; one running and one future discount).

## Automated

```powershell
.\scripts\dotnet.ps1 test --solution backend/HelloPos.slnx      # sync endpoint, revisions, isolation, catalog pricing cases
cd android; $env:JAVA_HOME='C:\Program Files\Android\Android Studio2\jbr'; .\gradlew assembleDebug testDebugUnitTest lintDebug
```

Both suites run `contracts/barcode-cases.json`, `contracts/pricing-cases.json` and
`contracts/catalog-pricing-cases.json`.

## Manual

1. **First download**: pair the till, sign in as a cashier. The catalog screen (Home → Catalog) shows all
   active categories in sort order and "Catalog updated just now".
2. **Offline**: switch the emulator to airplane mode, restart the app, sign in offline. The catalog is
   complete; the header shows its age.
3. **Lookup**: type each product's UPC-A, then the same code with a leading zero (EAN-13), a PLU, a code
   with a wrong last digit (message about the check digit) and an unknown code ("No product with this
   code"). Search part of a name with different case and without accents.
4. **Prices**: compare 5 products (including the discounted, weighed, deposit and inclusive-tax ones) with
   the web's product page; prices and tax match. Move the emulator date to the day of the future tax change
   and the future discount; the till shows the new values with no network.
5. **Changes**: back online, change a price, add a product and deactivate one on the web. Within 5 minutes
   (or "Refresh now") the till shows all three; the deactivated product is no longer found.
6. **Stale warning**: stay offline for over 24 hours (or move the clock); the header warns, lookups still work.
7. **Erase**: revoke the till on the web (or unpair). The app returns to pairing; pair it to a second
   business: nothing of the first is shown, only the second business's catalog after download.
8. **Timing**: with 10,000 imported products, time the first download (SC-001, < 60 s) and lookups (SC-002,
   < 200 ms).
