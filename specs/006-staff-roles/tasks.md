---
description: "Task list for feature 006: staff and roles"
---

# Tasks: Staff and Roles

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/openapi-additions.yaml](contracts/openapi-additions.yaml),
[quickstart.md](quickstart.md)

**Tests**: Required (Constitution III, VI, VIII). Each story's tests come first and must fail first.
API tests run against real PostgreSQL as the app login, with signed-in back-office clients (feature 005
helpers) and the in-memory email sender.

## Conventions

- .NET through `scripts/dotnet.ps1`. Every new operation goes into `contracts/openapi.yaml` before its
  endpoint; regenerate the Kiota client after contract changes.
- Writes require `X-HelloPos-Client: web`; staff endpoints honour `X-HelloPos-Business` (stale tab).
- Never return, log or audit a PIN or an invitation token. Staff are never deleted.
- Role checks come from `ICurrentStaff` (database, per request), never from a claim or the request.

---

## Phase 1: Setup (kernel rules)

- [X] T001 [P] Kernel tests first in `backend/tests/HelloPos.Kernel.Tests/Staff/StaffRulesTests.cs` (every cell of the research R4 rule table: owner/manager/cashier actor × add, invite, change role to each role, rename, deactivate, reactivate, set PIN, list, on each target role; "owner role requires a back-office account") and `PinRulesTests.cs` ("4–6 ASCII digits"; refuse `0000`, `111111`, `1234`, `4321`, `012345`, `98765`; accept `4826`, `1357`, `907153`); plus `ValidatePosUsername` (`^[a-z0-9._-]{3,32}$` after lower-casing) and display name 1–100
- [X] T002 Implement `backend/src/HelloPos.Kernel/Staff/StaffRole.cs`, `StaffRules.cs`, `PinRules.cs` (pure functions, field → message results like `BusinessSettingsRules`) until T001 passes

---

## Phase 2: Foundational (blocks every story)

- [X] T003 Entities and configurations per data-model.md in `backend/src/HelloPos.Api/Features/Staff/`: `StaffMember` (`display_name varchar(100)`; `role varchar(10)` in owner/manager/cashier; `active`; `account_id uuid null`, unique per business when not null; `pos_username varchar(32)` lower-case, unique `(business_id, lower(pos_username))` when not null; `pin_hash text null`; `pin_changed_at timestamptz null`; `version integer` concurrency token; checks: owner ⇒ account, username ⇔ pin, account or username present), `StaffChange` (append-only fields per data-model), and `Features/Invitations/StaffInvitation` (normalized email `varchar(256)`, role owner/manager, `token_hash bytea` unique, status pending/accepted/revoked, `expires_at`, composite FKs to `staff_members` for `invited_by_staff_id` and `accepted_staff_id`, partial unique index one pending per (business, normalized_email)); all `ConfigureBusinessOwned()`; remove `BusinessOwner`
- [X] T004 Migration `AddStaff` in `Infrastructure/Persistence/Migrations/`: create the three tables with `business_scope` RLS; `owner_read` on `staff_members` (`account_id = pos.current_owner_id()`); copy `business_owners` rows into `staff_members` as active owners (display name from `identity.owner_accounts`, version 1); drop `business_owners` and its policies; redefine `businesses.owner_read` to require an active owner or manager staff row of the current account; `REVOKE UPDATE, DELETE ON pos.staff_changes FROM pos_app`; create `pos.invitation_business(bytea)` `SECURITY DEFINER` with pinned `search_path`, `EXECUTE` to `pos_app` only; `Down` reverses
- [X] T005 [P] Tests `backend/tests/HelloPos.Api.Tests/Persistence/StaffMigrationTests.cs` (a database migrated to 005 with owners and businesses, then to `AddStaff`: each owner is an active owner staff row with the account's name; `business_owners` gone) and `Tenancy/StaffRowLevelSecurityTests.cs` (raw SQL as the app login: an account reads its own staff rows only; a cashier-role row does not make the business visible; app login cannot UPDATE or DELETE `staff_changes`; `invitation_business` returns the business only for a pending, unexpired hash and nothing for revoked, expired or unknown); schema convention check passes
- [X] T006 Switch the 005 business slices to staff: `CreateBusiness` inserts the creator's owner staff row (and an `added` audit row) in the same transaction acting as the new business; `ListMyBusinesses` returns businesses with an active owner/manager row of the account, plus `role`; `OpenBusiness` requires such a row (404 otherwise); update `OwnerRowLevelSecurityTests`, `CreateBusinessTests`, `OpenAndEditTests`, `OwnerIsolationTests` for the new table; all existing tests pass
- [X] T007 `Infrastructure/Tenancy/ICurrentStaff.cs` (active staff row for current business + signed-in account, loaded once per request) and `RequireRole(params StaffRole[])` endpoint filter (401 no session/business, 403 no active row or wrong role); apply: `GET /business` owner+manager, `PUT /business` owner only; `GET /owner/me` adds `currentRole`; tests for each (manager reads settings, cannot update; deactivated manager refused on next request)
- [X] T008 [P] Shared staff infrastructure in `Features/Staff/`: `StaffProblems` (types `urn:hellopos:problem:last-owner`, `…:staff-changed`, `…:already-member`), `StaffAudit` (writes `staff_changes` rows in the caller's transaction, never PIN or token values), `LastOwnerGuard` (locks the business row `FOR UPDATE`, counts active owners), `StaffCredentials` (`SetPin`, `VerifyPin(username, pin)` failing for inactive staff, `ChangeOwnPin(staff, current, new)`), PIN hashing with Identity `PasswordHasher`
- [X] T009 [P] Test infrastructure: `TenancyApp` helpers `AddStaffAsync(business, role, …)` (acting as the business) and `SignedInStaffClientAsync(email, business, role)` (account + linked staff row + opened business); `StaffIsolationTests` base wiring on the shared harness with owner clients (005 pattern)

**Checkpoint**: every 005 test passes on the new staff table; roles are enforced on the business endpoints.

---

## Phase 3: User Story 1 - Add cashiers and managers (P1) 🎯 MVP

**Goal**: owners add POS staff (cashiers, managers with a PIN) and invite managers and co-owners by email;
invited people accept and get back-office access.

**Independent test**: add a cashier with a username and PIN, invite a manager; the cashier is listed
active, the invitation pending; the invited person accepts and can open the business as manager.

### Tests first

- [X] T010 [P] [US1] Contract: add `GET/POST /staff`, `GET /staff/{staffId}`, `GET/POST /staff/invitations`, `POST /invitations/preview`, `POST /invitations/accept`, schemas `StaffMember` (never a PIN: `hasPin` only), `NewStaffMember`, `Invitation`, `NewInvitation`, `InvitationPreview`, `OwnerProfile.currentRole`, `BusinessSummary.role`, problem responses, to `contracts/openapi.yaml`; lint
- [X] T011 [P] [US1] `backend/tests/HelloPos.Api.Tests/Features/Staff/AddStaffTests.cs`: owner adds a cashier (201, listed active, `hasPin`, no PIN anywhere in the body); duplicate username in the same business → 400 on `posUsername` (same username in another business is fine); invalid name/username/PIN → 400 per field; `VerifyPin` succeeds with the PIN; list filters `active|inactive|all`; responses match the contract
- [X] T012 [P] [US1] `.../Features/Invitations/InvitationTests.cs`: invite → 201 pending + invitation email with a link; preview shows business and role; accept by a signed-in verified account with the invited email → 204, staff row active with the role, the business listed with that role and openable; the invitation email is sent whether or not the address already has an account and the invite response is identical either way (FR-011a); wrong-email account, unknown token, used token, expired (clock + 7 days) → identical 404; duplicate pending invite → 409; account already a member → 409 `already-member`; preview/accept rate limited; responses match the contract
- [X] T013 [P] [US1] `.../Tenancy/StaffIsolationTests.cs` on the harness: business A cannot list B's staff, read B's staff by id, or see B's invitations (SC-002); later stories add modify cases

### Implementation

- [X] T014 [US1] Slices `Features/Staff/{ListStaff,GetStaff,AddStaff}.cs` (owner+manager per kernel rules; add writes an `added` audit row; username uniqueness → field error) (makes T011, T013 pass)
- [X] T015 [US1] Slices `Features/Invitations/{InviteStaff,ListInvitations,PreviewInvitation,AcceptInvitation}.cs` and the invitation email in `OwnerEmails` (link `{Web:BaseUrl}/accept-invitation?token=…`); token 32 random bytes, SHA-256 stored; accept finds the business through `pos.invitation_business`, then acts as it (`FixedCurrentBusiness`); audit `invited`, `invitation_accepted` (makes T012 pass)
- [X] T016 [US1] Web: regenerate Kiota; `Services/StaffService.cs`, `InvitationService.cs`; `Pages/Staff/Staff.razor` (staff list with active/inactive filter, add POS staff form with kernel validation, invite form and pending invitations for owners); `Pages/Invitations/AcceptInvitation.razor` (preview, sign in or sign up and come back, accept); businesses list shows role; layout "Staff" link for owners and managers; bUnit tests in `web/tests/HelloPos.Web.Tests/Pages/Staff/` and `Pages/Invitations/`

**Checkpoint**: MVP: an owner sets up a cashier and a manager.

---

## Phase 4: User Story 2 - Change roles, deactivate, reactivate, PINs (P1)

**Independent test**: promote a cashier to manager; deactivate and confirm the PIN no longer verifies;
reactivate and confirm it does; reset a PIN; a stale edit is refused.

- [X] T017 [P] [US2] Contract: `PUT /staff/{staffId}` (`StaffUpdate`: displayName, role, posUsername, version), `POST /staff/{staffId}/deactivate`, `/reactivate` (version), `PUT /staff/{staffId}/pin` (pin, version); 409 responses
- [X] T018 [P] [US2] `.../Features/Staff/ChangeStaffTests.cs`: role change, rename, username change saved and audited; deactivate → inactive, `VerifyPin` fails at once (SC-004), still listed under inactive; reactivate → verifies again, same id; PIN reset → old fails, new verifies, `pin_changed_at` set; stale version → 409 `staff-changed`, nothing saved; no delete operation exists; audit rows for every change, none containing a PIN; `ChangeOwnPin` (service) needs the current PIN; isolation harness gains modify cases (update, deactivate, reactivate, set PIN on B's staff → 404, unchanged)
- [X] T019 [US2] Slices `Features/Staff/{UpdateStaff,DeactivateStaff,ReactivateStaff,SetStaffPin}.cs` with version checks and audit (makes T018 pass)
- [X] T020 [US2] Web: edit staff (name, username, role per kernel rules), deactivate/reactivate, set/reset PIN dialog (PIN never shown again), "changed by someone else, reload" on 409; bUnit tests

---

## Phase 5: User Story 3 - Co-owners, managers and the last owner (P2)

**Independent test**: a co-owner invited and accepted has full rights; a manager manages cashiers but is
refused for everything else; the last active owner cannot be removed.

- [X] T021 [P] [US3] Contract: `POST /staff/invitations/{invitationId}/revoke`, `/resend`; `last-owner` 409 documented on update and deactivate
- [X] T022 [P] [US3] `.../Features/Staff/RoleRulesTests.cs`: every refused manager case from the rule table (add/invite manager or owner, change any role, rename/deactivate/set PIN of an owner or manager, raise a cashier) → 403 and nothing changed (SC-003); a manager's allowed cashier actions work; an account linked as cashier gets 403 on back-office staff endpoints; co-owner via invitation can do everything an owner can; last active owner: deactivate or demote → 409 `last-owner`; two owners deactivating each other concurrently (two connections) → exactly one succeeds; self-deactivation with another owner works and the next request is refused; revoke → link 404; resend → old token 404, new one works
- [X] T023 [US3] Implement `LastOwnerGuard` use in update and deactivate, `Features/Invitations/{RevokeInvitation,ResendInvitation}.cs`, and any missing rule checks (makes T022 pass)
- [X] T024 [US3] Web: role-aware staff page (managers see cashier actions only; owners see invitations with revoke/resend), last-owner message, manager's business view without settings editing; bUnit tests

---

## Phase 6: Polish

- [X] T025 Run backend and web suites; add a test that runs add / set PIN / verify / change PIN / invite / accept with captured logs and asserts no PIN or token appears in any response body or log event (SC-005)
- [X] T026 Run quickstart.md manual steps with Mailpit; record in `checklists/quickstart-results.md`
- [X] T027 [P] Update `docs/tenancy.md` (staff table replaces `business_owners`, `ICurrentStaff`/`RequireRole`, why invitations use a `SECURITY DEFINER` lookup) and README (staff, invitations)
- [X] T028 Re-check the Constitution Check against delivered code; note deviations in plan.md

---

## Dependencies

- Phase 1 → Phase 2 → stories. US1 (Phase 3) is the MVP. US2 needs US1's staff slices; US3 needs US1's
  invitations and US2's update/deactivate. Polish last.
- Within each story: contract and tests first (fail), then slices, then web.

## Parallel opportunities

- T001 alongside T003 (kernel vs API); T005, T008, T009 in parallel after T004.
- In each story the contract task and the test files are [P] (different files).

## Implementation strategy

MVP first: Phases 1–3, then report; then US2, US3 and polish. Each story ends with all suites green.
