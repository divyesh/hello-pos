# Implementation Plan: Staff and Roles

**Branch**: `006-staff-roles` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

## Summary

One business-owned table, `pos.staff_members`, holds everyone who works at a business (owner, manager,
cashier) and replaces feature 005's `business_owners`: existing owners are migrated as owner staff.
Back-office access comes from a staff row linked to a back-office account (owners and managers, added by
email invitation); POS access comes from a POS username and a hashed 4–6 digit PIN (set by an owner or
manager; cashiers can change their own, wired to the POS in #7). The caller's role is read from the
database on every request (`ICurrentStaff`, `RequireRole`), and the role rule table lives in the shared
kernel so the API enforces it and the web uses it to shape forms. The last-active-owner rule runs under a
business-row lock; concurrent edits use a version number; every change is written to an append-only
audit table. Invitation tokens are found through a `SECURITY DEFINER` function, so the business comes
from the secret token, never from request data. Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API, web, kernel)

**Primary Dependencies**: existing only (ASP.NET Core Identity `PasswordHasher` for PINs, EF Core +
Npgsql, MailKit via `IEmailSender`, MudBlazor)

**Storage**: PostgreSQL 18: new `pos.staff_members`, `pos.staff_invitations`, `pos.staff_changes`
(append-only); `pos.business_owners` migrated and dropped; function `pos.invitation_business`

**Testing**: kernel unit tests (rule table, PIN rules), xUnit v3 + Testcontainers API tests as the app
login, isolation harness (owner variant from 005), concurrency tests (two connections), contract tests,
bUnit

**Target Platform**: API container, web static site, local compose (Mailpit for invitation emails)

**Project Type**: full-stack feature (API slices + web pages + kernel rules)

**Performance Goals**: add a cashier in under 1 minute (SC-001); role lookup adds one indexed query per
staff request

**Constraints**: business only from the session (or the invitation token's own business); PINs never
readable; staff never deleted; ≥ 1 active owner per business at all times

**Scale/Scope**: ~13 API operations, 3 web pages, 1 migration (+ data migration), ~70 new tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I. Money Is Exact | No money. | N/A |
| II | No sales or charges. | N/A |
| III. Tenant Isolation | All three tables business-owned with composite keys and RLS; staff endpoints act only on the session's business; invitation acceptance finds its business from the secret token via a narrow definer function, never from request data; isolation harness test for every staff endpoint; references (inviter, accepted staff) use composite foreign keys. | Pass |
| IV. Sales Are an Append-Only Record | No sales yet; the same stance applied to staff so later sales stay attributable: staff are deactivated, never deleted, and `staff_changes` is append-only (app login has no UPDATE/DELETE). | Pass |
| V. Contract-First | Operations added to `contracts/openapi.yaml` first; web client regenerated; contract tests. | Pass |
| VI. Test-First | Kernel rule tests and API tests per story before implementation, real PostgreSQL. | Pass |
| VII. Vertical Slices | `Features/Staff/*` and `Features/Invitations/*` slices; rules in the kernel; web talks only to the API. | Pass |
| VIII. Secure by Default | Roles enforced server-side on every request from the database (no stale role claims); PINs hashed, never returned or logged; trivial PINs refused; invitation tokens single-use, hashed at rest, 7-day expiry, email-bound, no enumeration; rate limit on preview/accept. PIN brute-force protection is #7 (recorded in research R5). | Pass |
| IX. No Silent Failures | Every refusal is a problem detail with a specific type (last-owner, staff-changed, already-member); email failures logged and resend available. | Pass |
| X. Simplicity | One staff table instead of owners + staff; no new packages; three roles, no permission matrix. | Pass |

Result: **pass**. Re-checked after design: pass.

## Project Structure

### Documentation (this feature)

```text
specs/006-staff-roles/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/openapi-additions.yaml
├── checklists/requirements.md
└── tasks.md                     # /speckit-tasks
```

### Source Code (repository root)

```text
contracts/openapi.yaml                         # + staff and invitation operations, currentRole, role
backend/src/HelloPos.Kernel/Staff/             # StaffRole, StaffRules (rule table), PinRules
backend/src/HelloPos.Api/
├── Infrastructure/Tenancy/ICurrentStaff.cs    # role of the current request + RequireRole filter
├── Features/Staff/                            # StaffMember entity/config, List, Get, Add, Update,
│                                              # Deactivate, Reactivate, SetPin, StaffCredentials (Verify/ChangeOwn for #7),
│                                              # StaffChange audit, LastOwnerGuard
├── Features/Invitations/                      # StaffInvitation entity, Invite, List, Revoke, Resend,
│                                              # Preview, Accept, invitation email
├── Features/Businesses/                       # Create/List/Open/Current switched to staff_members; roles applied
├── Features/Owners/Me                          # + currentRole
└── Infrastructure/Persistence/Migrations/     # AddStaff (create tables, migrate business_owners, drop it, policies, function, grants)
backend/tests/HelloPos.Api.Tests/
├── Features/Staff/*, Features/Invitations/*
├── Persistence/StaffMigrationTests.cs         # 005 owners become owner staff
└── Tenancy/StaffIsolationTests.cs
backend/tests/HelloPos.Kernel.Tests/Staff/*    # rule table and PIN rules
web/src/HelloPos.Web/
├── Services/StaffService.cs, InvitationService.cs
├── Pages/Staff/Staff.razor (+ StaffForm, PinDialog), Pages/Invitations/AcceptInvitation.razor
└── Layout/ (staff link by role; businesses list shows role)
web/tests/HelloPos.Web.Tests/Pages/Staff/*, Pages/Invitations/*
```

**Structure Decision**: two new slice folders (staff, invitations); the business slices from 005 switch
from `business_owners` to `staff_members`; shared rules in the kernel.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| `SECURITY DEFINER` lookup for invitation tokens | The accepting person has no business open; the business must come from the token, not the request | Putting the business id in the link makes request data choose the business (Principle III); a non-business-owned invitations table breaks the tenancy conventions |
| Per-request role lookup | Role changes and deactivation must apply on the next request | Role in the cookie stays stale until the person signs in again |
| Business-row lock for the last-owner rule | Two owners could otherwise deactivate each other at the same moment | A plain count check races; a database trigger is harder to reason about and test |
