# Data Model: Login and Sessions

All tables are business-owned (feature 003 conventions: key `(business_id, id)`, UUIDv7, RLS
`business_scope`). Secrets are stored only as SHA-256 hashes (tokens, codes, credentials) or slow
hashes (PINs, from 006).

## pos.device_pairings (new)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK |
| `code_hash` | `bytea` | SHA-256 of the 8-character code (upper-case, no dash); unique |
| `expires_at` | `timestamptz` | created + 5 minutes |
| `created_by_staff_id` | `uuid` | → `staff_members` (owner or manager) |
| `used_at` | `timestamptz` null | set once, by pairing |
| `device_id` | `uuid` null | → `devices`, the device created by this code |
| `created_at` | `timestamptz` | |

Function `pos.pairing_business(code_hash bytea) RETURNS uuid`: `SECURITY DEFINER` (owned by the
migrator), pinned `search_path`; returns the business of an unused, unexpired code, else null;
`EXECUTE` to `pos_app` only.

## pos.devices (new)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK |
| `name` | `varchar(60)` | 1–60 chars |
| `credential_hash` | `bytea` | SHA-256 of the device credential secret |
| `public_key` | `bytea` | the device's RSA public key (X.509 SubjectPublicKeyInfo), for the offline bundle |
| `registered_by_staff_id` | `uuid` | → `staff_members` |
| `registered_at`, `last_used_at` | `timestamptz` | `last_used_at` updated at most once a minute |
| `revoked_at` | `timestamptz` null | set once; a revoked device is never usable again |
| `revoked_by_staff_id` | `uuid` null | → `staff_members` |
| `failed_signins` | `integer` | consecutive failed PIN sign-ins on this device |
| `next_attempt_at` | `timestamptz` null | back-off: 2^(n−3) s after the 3rd failure, max 5 min |
| `version` | `integer` | concurrency token for rename and revoke |

## pos.staff_sessions (new)

| Column | Type | Rules |
|--------|------|-------|
| `business_id`, `id` | `uuid` | PK |
| `staff_member_id` | `uuid` | → `staff_members` |
| `device_id` | `uuid` | → `devices` |
| `access_token_hash` | `bytea` | current access token; unique |
| `access_expires_at` | `timestamptz` | issue + 15 minutes |
| `refresh_token_hash` | `bytea` | current refresh token; unique |
| `previous_refresh_token_hash` | `bytea` null | the one just rotated, to detect reuse |
| `started_at` | `timestamptz` | |
| `expires_at` | `timestamptz` | started + 12 hours (absolute) |
| `last_active_at` | `timestamptz` | at most once a minute |
| `offline_signed_in_at` | `timestamptz` null | when the session began as an offline sign-in |
| `ended_at`, `end_reason` | `timestamptz`, `varchar(20)` null | `sign_out`, `refresh_reuse`, `pin_changed`, `deactivated`, `device_revoked`, `expired` |

A session is valid when `ended_at is null`, now < `expires_at`, the access token is current and unexpired,
the device is not revoked, the staff member is active, and `started_at >= staff.pin_changed_at`
(`pin_changed_at` null counts as "never changed").

## pos.staff_members (changed)

| Column | Change |
|--------|--------|
| `pin_failed_count` | new, `integer` default 0 |
| `pin_locked_until` | new, `timestamptz` null |

## pos.staff_changes (reused)

New actions: `device_registered`, `device_renamed`, `device_revoked`, `signed_in`, `signed_in_offline`,
`sign_in_failed`, `locked_out`, `signed_out`, `pin_changed` (own change). `device_id` is recorded in the
new nullable column `device_id` (→ `devices`). Never a PIN, token, code or credential.

## On the device (Android, app-private, AES-GCM under a Keystore key)

| Item | Contents |
|------|----------|
| Device registration | device credential, device id, business id and name |
| Session | access token, refresh token, staff id, name, role, expiry |
| Offline access | the encrypted bundle as received, its version and time |
| Local lockout | per username: failures, locked until; device back-off state |
| Pending reports | offline sign-ins and failed attempts not yet sent |
