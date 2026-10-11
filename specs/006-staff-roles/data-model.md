# Data Model: Staff and Roles

All tables are business-owned (feature 003 conventions: key `(business_id, id)`, `business_id` first,
UUIDv7 ids, row-level security `business_scope`).

## pos.staff_members (new; replaces pos.business_owners)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK `(business_id, id)` |
| `display_name` | `varchar(100)` | 1–100 chars, trimmed |
| `role` | `varchar(10)` | `owner`, `manager`, `cashier` (check) |
| `active` | `boolean` | default true |
| `account_id` | `uuid` null | back-office account (`identity.owner_accounts.id`, no cross-schema FK); unique per business when not null; required when role = `owner` (check) |
| `pos_username` | `varchar(32)` null | `^[a-z0-9._-]{3,32}$` (stored lower-case); unique per business when not null |
| `pin_hash` | `text` null | PBKDF2 hash; set together with `pos_username` |
| `pin_changed_at` | `timestamptz` null | for #7 to end older POS sessions |
| `version` | `integer` | concurrency token, +1 on every change |
| `created_at`, `updated_at` | `timestamptz` | UTC |

Checks: `role IN (...)`; `role <> 'owner' OR account_id IS NOT NULL`; `(pos_username IS NULL) =
(pin_hash IS NULL)`; at least one of `account_id`, `pos_username` (a person can sign in somewhere).

Indexes: unique `(business_id, lower(pos_username))` where not null; unique `(business_id, account_id)`
where not null; `(account_id)` for listing an account's businesses.

RLS: `business_scope` (all commands) + `owner_read` (`FOR SELECT USING account_id =
pos.current_owner_id()`).

Invariant (enforced by the API under a business-row lock, R6): every business has ≥ 1 row with
`role = 'owner' AND active`.

**Migration from 005**: each `business_owners` row → `staff_members` row (`role = owner`,
`active = true`, `account_id = owner_id`, `display_name` = the account's display name, `version = 1`).
Then `business_owners` and its policies are dropped and `businesses.owner_read` is redefined (R2).

## pos.staff_invitations (new)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK |
| `email` / `normalized_email` | `varchar(256)` | normalized = upper-invariant; one `pending` per (business, normalized_email) (partial unique index) |
| `role` | `varchar(10)` | `owner` or `manager` |
| `display_name` | `varchar(100)` | proposed name for the staff row |
| `token_hash` | `bytea` | SHA-256 of the emailed token; unique |
| `status` | `varchar(10)` | `pending`, `accepted`, `revoked` |
| `expires_at` | `timestamptz` | created + 7 days |
| `invited_by_staff_id` | `uuid` | → `staff_members (business_id, id)` |
| `accepted_staff_id` | `uuid` null | → `staff_members (business_id, id)` |
| `created_at`, `updated_at` | `timestamptz` | |

Function `pos.invitation_business(token_hash bytea) RETURNS uuid`: `SECURITY DEFINER`, owned by the
migrator, `search_path` pinned; returns `business_id` of the pending, unexpired invitation with that
hash, else null. `EXECUTE` granted to `pos_app` only.

## pos.staff_changes (new, append-only)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK |
| `staff_member_id` | `uuid` null | → `staff_members`; null for invitation-only events |
| `invitation_id` | `uuid` null | → `staff_invitations` |
| `actor_staff_id` | `uuid` | who did it; for an accepted invitation, the new staff member themselves |
| `action` | `varchar(30)` | see research R8 |
| `old_value`, `new_value` | `varchar(100)` null | role, name, username or state; never a PIN |
| `at` | `timestamptz` | |

Grants: `pos_app` has `INSERT, SELECT` only (`UPDATE, DELETE` revoked).

## Session and request context

| Value | Source | Use |
|-------|--------|-----|
| Signed-in account | cookie claim `owner_id` (005) | memberships before a business is open |
| Current business | cookie claim `business_id` (005), set by open after checking an active owner/manager row | all staff endpoints |
| Current role | `ICurrentStaff`: active staff row for (business, account), read per request | `RequireRole(...)` |

## Kernel rules (shared by API and web)

- `StaffRole` = Owner, Manager, Cashier.
- `StaffRules.Can(actor, action, targetRole, newRole?)` per research R4.
- `PinRules.Validate(pin)`: 4–6 ASCII digits; not all the same digit; not an ascending or descending
  run (`0123`, `98765`, wrapping not considered).
- `StaffRules.ValidateDisplayName`, `ValidatePosUsername` (`^[a-z0-9._-]{3,32}$` after lower-casing).
