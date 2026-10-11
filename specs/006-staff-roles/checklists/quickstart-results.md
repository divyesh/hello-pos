# Quickstart Results: Staff and Roles

**Date**: 2026-10-11 · **Environment**: `scripts/env.ps1 up --api` (PostgreSQL with feature 005 data,
Mailpit, API image); emails read from Mailpit.

## Automated

| Suite | Result |
|-------|--------|
| Backend (`backend/HelloPos.slnx`, real PostgreSQL) | 224 passed (kernel 67 incl. the full role table) |
| Web (`web/HelloPos.Web.slnx`, bUnit) | 67 passed |
| Android unit tests (contract regenerated) | passed |
| Contract lint (Redocly) | valid |

Sabotage checks (each made the named tests fail, then restored):

- Accepting an invitation without the email check → `InvitationTests` fail.
- Last-owner rule without the business-row lock → two owners deactivating each other both succeed;
  `RoleRulesTests.TwoOwnersDeactivatingEachOther_AtOnce_LeaveExactlyOneOwner` fails.

## Migration on real data

`AddStaff` applied to the local database created during feature 005: all 4 businesses' owners became
active owner staff members; `business_owners` dropped; nothing lost.

## Manual steps against the local API

| Step | Check | Result |
|------|-------|--------|
| 2 | Owner listed as owner; add cashier ravi/4826 (201); PIN 1234 refused on `pin` | Pass |
| 3–4 | Invite manager (pending); email in Mailpit; invited address signs up, previews ("QS6 Shop as manager"), accepts; business listed as manager | Pass |
| 5 | Manager deactivates and reactivates the cashier; promoting the cashier, deactivating the owner, changing settings → 403 | Pass |
| 6 | Owner deactivates the manager → manager's next request 403; only owner deactivating self → 409 `last-owner` | Pass |
| 7 | Two edits from the same version → second 409 `staff-changed` | Pass |
| 8 | Invite a second owner, revoke → link answers 404 | Pass |

## Still for the owner, in the browser

- Visual pass of the staff page (add, edit, PIN, deactivate, invitations) and the accept page;
  SC-001 timing (add a cashier in under 1 minute).

## Found and fixed during this feature

- String component parameters bound as literals (`FormError="formError"`): the business forms (feature
  005) and the add-staff form always showed a red "formError" alert. Fixed with `@`, regression tests
  added.
