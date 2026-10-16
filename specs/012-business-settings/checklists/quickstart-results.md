# Quickstart results: Business Settings

**Date**: 2026-10-15

## Automated

| Suite | Result |
|-------|--------|
| Backend (`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`) | 582 passed |
| Web (`scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx`) | 143 passed |
| Android (`./gradlew assembleDebug testDebugUnitTest lintDebug`) | 241 passed, no new lint findings |
| Contract lint (`npx @redocly/cli@2.62.1 lint contracts/openapi.yaml`) | valid |

`contracts/catalog-pricing-cases.json` carries the settings in each catalog's download; both suites
still pass it, and the server's download of each catalog equals the file.

Covered by the suites:

| Criterion | How |
|-----------|-----|
| SC-004 every change in the history | `PaymentTypeTests` and `TableTests` check the entry for every write; `SettingsHistoryTests` checks who, when and each field's old and new value, and that the history is append-only |
| SC-005 no cross-business access | `Tenancy/SettingsIsolationTests`: every settings operation and the till download, run as another business |
| SC-003 on tills (server and Android sides) | `CatalogSyncTests` (settings in full and change downloads, no tip presets when tips are off); `TillSettingsStoreTest` (stored, replaced on changes, erased with the copy, a version 1 copy is dropped so the next refresh is a full download) |

One flaky web test was found while running the suites alongside the Android build:
`PaymentTypesPageTests.Owner_MovesATypeUp_EditsAndDeactivates` asserted straight after a click and lost
the race under load. It now waits for each render, like the other page tests.

## Manual (web back office and a till): pending

Steps 1-7 of `quickstart.md` are to be run by the project owner and recorded here.

| Step | Result | Notes |
|------|--------|-------|
| 1 Payment types (owner) | | |
| 2 Manager: read-only payment types and discounts, editable tables | | |
| 3 Discount limit and tips | | |
| 4 Areas and tables, bulk add, refusals | | |
| 5 History | | |
| 6 Till download contains the settings | | |
| 7 SC-001 (under 5 min) and SC-002 (3 areas, 60 tables, under 5 min) | | |
