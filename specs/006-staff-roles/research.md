# Research: Staff and Roles

No new packages. Builds on feature 003 (tenancy) and feature 005 (back-office accounts, sessions,
email, invitations reuse its link and email patterns).

## R1. One staff table replaces `business_owners`

- **Decision**: new business-owned table `pos.staff_members` holds everyone who works at a business:
  display name, role (`owner`, `manager`, `cashier`), active flag, optional POS username and PIN hash,
  optional link to a back-office account (`account_id`, the feature 005 `OwnerAccount`), version.
  Feature 005's `pos.business_owners` rows are migrated into it as active owners and the table is
  dropped. "Which businesses may this account open" becomes "active staff rows of this account with
  role owner or manager".
- **Rationale**: one source of truth for "who works here and in what role"; co-owners and managers
  are the same kind of row as the first owner; deactivating an owner is the same operation as
  deactivating a cashier.
- **Naming**: the C# type `OwnerAccount` and `ICurrentOwner` keep their names (renaming touches all of
  005 for no behaviour change); docs call them "back-office account". The column is `account_id`.
- **Alternatives**: keep `business_owners` beside a staff table (two places to keep in sync; the last-
  owner rule would span both); staff as Identity users (cashiers have no email; mixes POS PINs with
  back-office passwords).

## R2. Row-level security for the account dimension

- `staff_members`: policy `business_scope` (feature 003) plus read-only `owner_read`:
  `account_id = pos.current_owner_id()`, so a signed-in account sees its own memberships before a
  business is open (to list businesses and check access on open).
- `businesses.owner_read` (feature 005) is replaced: `EXISTS (active staff row for this business with
  account_id = current_owner_id() AND role IN ('owner','manager'))`. Cashiers' rows never grant
  back-office visibility.
- Writes still require acting as the business (005 decision): creating a business inserts its first
  owner staff row while acting as the new business.

## R3. Role of the current request

- **Decision**: `ICurrentStaff` (scoped) looks up, once per request, the active staff row for (current
  business, signed-in account) and exposes its role. Endpoint filter `RequireRole(...)` answers `403`
  when the account has no active staff row or the wrong role; `401` without a session or business.
- **Rationale**: the role is read from the database on every request, so a role change or deactivation
  takes effect on the very next request (SC-004, edge case "deactivated while signed in") without
  re-issuing cookies. One indexed lookup per request, like the 005 security-stamp check.
- **Existing endpoints**: `GET /business` → owner or manager; `PUT /business` → owner only; opening a
  business → active owner or manager. Creating a business needs no role (the creator becomes owner).
- **Alternatives**: role claim in the cookie (stale until re-sign-in: a demoted manager would keep
  rights); ASP.NET authorization policies with a claims transformer (same lookup, more indirection).

## R4. Role rules in the shared kernel

- `HelloPos.Kernel.Staff.StaffRules` (pure functions): what an actor role may do to a target role
  (add, change role to X, deactivate, reactivate, set PIN), the last-active-owner rule's decision
  given counts, and PIN validation (`PinRules`: 4–6 digits; refuse one repeated digit and ascending or
  descending runs). Used by the API (authoritative) and the web (to hide actions and validate forms).
- Rule table (actor → allowed on target):

  | Action | Owner | Manager | Cashier |
  |--------|-------|---------|---------|
  | Add POS staff (`cashier`, or `manager` without account) | any | cashier only | — |
  | Invite (`owner`, `manager`) | yes | no | — |
  | Change role | any target, any role (subject to R6 and "owner needs an account") | no | — |
  | Deactivate / reactivate | any (subject to R6) | cashiers only | — |
  | Set or reset PIN, POS username | any | cashiers only | — |
  | Rename | any | cashiers only | — |
  | List staff, list invitations | yes | yes (invitations: no) | — |

- A role of `owner` requires a linked back-office account (owners must be able to sign in to the back
  office and receive security emails); owners are only added by invitation or by being the creator.

## R5. PINs

- Stored with ASP.NET Core Identity's `PasswordHasher` (PBKDF2, per-hash salt, current iteration count)
  in `pin_hash`; never returned, never logged. POS username unique per business, case-insensitive.
- A 4–6 digit PIN space is small: the hash slows offline guessing only. Online guessing is stopped in
  #7 (registered devices only, attempt limits, lockout); this feature records the decision so #7
  cannot skip it.
- `StaffCredentials` service: `SetPin` (owner/manager per R4), `VerifyPin(username, pin)` (fails for
  inactive staff; used by #7), `ChangeOwnPin(staff, current, new)` (FR-010; the endpoint arrives with
  the cashier's POS session in #7). Each PIN change bumps the row's `pin_changed_at` so #7 can end POS
  sessions issued before it.

## R6. Last active owner, concurrency

- **Last owner**: every operation that can remove an active owner (deactivate, role change away from
  owner) runs in one transaction that first locks the business row (`SELECT … FOR UPDATE`), then counts
  active owners. Two owners deactivating each other concurrently are serialised; one of them fails
  with `409 last-owner`.
- **Concurrent edits**: `staff_members.version` (integer, EF concurrency token). Update, deactivate,
  reactivate and PIN reset send the version they read; a mismatch is `409 staff-changed` ("changed by
  someone else, reload"). Creation and invitation acceptance need no version.

## R7. Invitations

- Table `pos.staff_invitations` (business-owned): email (normalized), role (`owner` or `manager`),
  display name, token hash, status (`pending`, `accepted`, `revoked`), expires at (7 days), invited by,
  accepted by account, timestamps. One pending invitation per (business, email).
- Token: 32 random bytes, base64url, sent only in the email link
  (`{Web:BaseUrl}/accept-invitation?token=…`); the database stores its SHA-256. Revoking or accepting
  makes it unusable; expiry by the injected clock.
- **Finding the business without trusting the request**: the accept page knows only the token. A
  `SECURITY DEFINER` function `pos.invitation_business(token_hash)` (owned by the migrator, which owns
  the table, so row-level security does not hide the row from it) returns only the business id of a
  pending, unexpired invitation with that hash. The API then acts as that business
  (`FixedCurrentBusiness`) to read and update the invitation. The business id comes from the secret
  token, never from request data.
- **Accepting**: the person must be signed in with a verified account whose email equals the
  invitation's (case-insensitive). Otherwise the same `404` as an unknown token (no enumeration of who
  was invited). On accept: create an active staff row with the role and account, or, if this account
  already has a row in the business, `409 already-member`.
- **Preview**: `POST /invitations/preview {token}` returns business name and role so the page can say
  "Patel Grocers invites you as manager" before the person signs in; it reveals nothing without the
  token.
- Emails: invitation email (link, business, role, inviter's name). Sent through `IEmailSender`;
  failures logged, invitation still created and can be resent (`POST /staff/invitations/{id}/resend`
  issues a new token, invalidating the old one).

## R8. Audit trail

- `pos.staff_changes` (business-owned, append-only): staff member, actor (account or staff), action
  (`added`, `invited`, `invitation_accepted`, `invitation_revoked`, `role_changed`, `renamed`,
  `deactivated`, `reactivated`, `pin_set`, `pin_changed`, `username_changed`), old and new values
  (never PINs), at. The app role gets `INSERT, SELECT` only (`UPDATE`/`DELETE` revoked), matching the
  constitution's append-only stance for history.
- Written in the same transaction as the change it records.

## R9. Web

- `/owner/me` gains `currentRole` (role in the open business). The web hides owner-only actions using
  the kernel rules; the API enforces them regardless.
- Pages: `Staff.razor` (active / inactive tabs; add POS staff; edit name, username, role; deactivate /
  reactivate; set PIN), invitations panel (owners), `AcceptInvitation.razor` (preview; sign in or
  sign up, then accept). Business list shows the role per business; managers see staff only.
- Stale-tab guard (005) applies to all staff endpoints.

## R10. Contract

- New operations (see `contracts/openapi-additions.yaml`): staff list/get/create/update/deactivate/
  reactivate/set-pin, invitations list/create/revoke/resend, invitation preview/accept;
  `OwnerProfile.currentRole`; `BusinessSummary.role`.
- Problem types: `urn:hellopos:problem:last-owner`, `urn:hellopos:problem:staff-changed`,
  `urn:hellopos:problem:already-member`, plus existing ones.
