# Quickstart results: Catalog Setup

**Date**: 2026-10-12

## Automated

| Suite | Result |
|-------|--------|
| Backend (`scripts/dotnet.ps1 test --solution backend/HelloPos.slnx`) | 492 passed |
| Web (`scripts/dotnet.ps1 test --solution web/HelloPos.Web.slnx`) | 102 passed |
| Android (`./gradlew assembleDebug testDebugUnitTest lintDebug`) | 149 passed, lint clean |
| Contract lint | valid |

Measured in the suites: 10,000-product search under 1 second (SC-003); 1,000-row CSV preview under
10 seconds and import under 30 seconds (SC-002). Fault check: switching off the "refuse a file with errors"
rule makes the import test fail.

## Manual (local environment): pending

Steps 1-8 of `quickstart.md`, including the SC-001 timing (3 profiles, 5 categories, 20 products by hand in
under 30 minutes), are to be run by the project owner and recorded here.

| Step | Result | Notes |
|------|--------|-------|
| 1-2 Tax profiles, future version, manager read-only | | |
| 3 Categories | | |
| 4 Products, barcode checks | | |
| 5 Discounts, overlap refused | | |
| 6 Search and filters | | |
| 7 Export, re-import unchanged, edit and import | | |
| 8 SC-001 timing | | |
