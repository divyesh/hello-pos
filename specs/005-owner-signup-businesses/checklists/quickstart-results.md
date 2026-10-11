# Quickstart Results: Owner Sign-up and Businesses

**Date**: 2026-10-11 · **Environment**: `scripts/env.ps1 up --api` (PostgreSQL, Mailpit, API image),
web app via `scripts/dotnet.ps1 -Port 5090 run --project web/src/HelloPos.Web`.

## Automated

| Suite | Result |
|-------|--------|
| Backend (`backend/HelloPos.slnx`, real PostgreSQL) | 121 passed |
| Web (`web/HelloPos.Web.slnx`, bUnit) | 43 passed |
| Android unit tests (contract regenerated) | passed |
| Contract lint (Redocly) | valid |

Sabotage check: with the ownership check in `OpenBusiness` disabled, `OwnerIsolationTests` and
`OpenAndEditTests` fail (the isolation tests are load-bearing).

## Manual steps against the local API (emails read from Mailpit)

| Step | Check | Result |
|------|-------|--------|
| 2 | Sign up → link from Mailpit verifies (204) → sign in (204); list is empty | Pass |
| 3 | India business: INR, tax_inclusive, April, Asia/Kolkata, 100 minor units; opened | Pass |
| 4 | Second business (Canada); edit India, switch to Canada; Canada unchanged, India renamed | Pass |
| 5 | Stale tab (page for India, session on Canada) → `409 business-changed`, nothing saved | Pass |
| 6 | Second owner: empty list; opening the first owner's business → 404, same as an unknown id | Pass |
| 7 | Forgot password → reset link from Mailpit (204) → first session's `/owner/me` → 401 | Pass |
| 8 | Five wrong passwords → right password gets the same generic 401 | Pass |

## In the browser (owner)

- Sign up, Mailpit link, sign in: confirmed by the owner (2026-10-11).
- Country pre-fill, open/switch in the app bar and the stale-tab message are covered by bUnit; a
  visual pass in the browser is still to do by the owner. SC-001 (under 3 minutes from sign-up to an
  open business) is not yet timed.

## Found and fixed during this run

- A malformed JSON body returned 500 in Development; now `400` problem details in every environment
  (`BadRequestExceptionHandler`, test `MalformedJson_Is400_NotAServerError`).
