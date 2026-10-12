# Quickstart results: Catalog on Android

**Date**: 2026-10-12

## Automated

| Suite | Result |
|-------|--------|
| Backend (`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`) | 510 passed |
| Web (`scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx`) | 102 passed |
| Android (`./gradlew assembleDebug testDebugUnitTest lintDebug`) | 236 passed, no new lint findings |
| Contract lint | valid |

Shared cases: `barcode-cases.json` (29 cases) passes in both suites; `catalog-pricing-cases.json`
(2 catalogs, 16 checks) passes in both, and the server's download of each catalog equals the file.

Measured in the suites (JVM and test containers, not a till):

| Criterion | Measured |
|-----------|----------|
| SC-002 lookups with 10,000 products | code 6 ms, name 9 ms (bound 200 ms) |
| SC-004 refresh with 10 changed products | under 1% of the 10,000-product full download (asserted) |
| SC-001 server side of a 10,000-product download | under 10 s (asserted); saving it on the JVM: 682 ms |
| SC-005 till and server prices | 16 of 16 checks equal |
| SC-006 no half-applied catalog | 20 change sets under a concurrent reader: none seen half-applied |
| SC-007 nothing left after erase | no row, and the erased text is not in the database file |

Fault checks: with the read position switched to the newest transaction id, the "save committing after
the read" test fails (so it guards the commit-order rule). Removing `VACUUM` does not make the erase test
fail, because Android's SQLite overwrites deleted rows (secure delete); the test still proves SC-007.

## Manual (emulator or tablet): pending

Steps 1-8 of `quickstart.md`, including SC-001 on a real till over a shop connection (10,000 products in
under 60 seconds), are to be run by the project owner and recorded here.

| Step | Result | Notes |
|------|--------|-------|
| 1 First download | | |
| 2 Offline restart | | |
| 3 Lookups (UPC-A, EAN-13 form, PLU, wrong check digit, unknown, name) | | |
| 4 Prices vs the web, future tax and discount dates | | |
| 5 Changes within 5 minutes / Refresh now | | |
| 6 Stale warning | | |
| 7 Revoke / pair to another business | | |
| 8 SC-001 and SC-002 timing on a till | | |
