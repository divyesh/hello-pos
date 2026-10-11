# Feature Specification: Staff and Roles

**Feature Branch**: `006-staff-roles`
**Created**: 2026-10-11
**Status**: Draft
**Input**: Roadmap Phase 1, feature #6. In the web back office, an owner with an open business adds
people to that business with a role (owner, manager or cashier), changes roles, and deactivates and
reactivates people. Roles are enforced by the server. Cashiers sign in on the Android POS in #7; this
feature creates the staff records and their sign-in credentials so #7 can use them.

## Clarifications

### Session 2026-10-11

- Q: What does a cashier or manager use to sign in on the POS? → A: A username and a 4–6 digit PIN
  (option A). Device registration and attempt limits that make PINs safe are part of #7.
- Q: Who sets credentials? → A: Cashiers: the owner or manager sets the PIN and hands it over, and
  the cashier can change it themselves. Co-owners and managers: an email invitation, and they set
  their own password (option A).

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Staff list per business; add, change role, deactivate, reactivate | Android sign-in, device registration and revocation (#7) |
| POS username and PIN for each staff member, ready for #7 | Using them to sign in on Android, and a cashier changing their own PIN there (#7) |
| Email invitations for co-owners and managers; accepting one | |
| Co-owners: another owner account linked to the business | Transferring or deleting a business |
| Role rules enforced by the server for every staff action | Role checks on sales, refunds, reports (each later feature) |
| Managers using the back office for staff (cashiers only) | Managers' access to catalog, reports (their features) |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add cashiers and managers (Priority: P1)

An owner with a business open adds the people who work there: each gets a name, a role (manager or
cashier) and sign-in credentials, and appears in the business's staff list. The owner can see who is
active and what role they have.

**Why this priority**: a business cannot sell until it has people who can sign in to the POS (#7
needs these records); the roadmap's Phase 1 goal is "an owner creates a business and a cashier".

**Independent Test**: as an owner, add a cashier with a username and PIN, and invite a manager by
email; the cashier appears active, the invitation appears pending; the stored PIN verifies (so #7 can
use it) and is never readable.

**Acceptance Scenarios**:

1. **Given** an owner with a business open, **When** they add a cashier with a name and credentials,
   **Then** the cashier appears in that business's staff list as active.
2. **Given** an owner, **When** they invite a manager by email, **Then** an invitation is pending; when
   the invited person accepts it (signing in or creating an account), they appear as an active
   manager and can open the business in the back office.
3. **Given** a POS username already used by another person in the same business, **When** the
   owner adds someone with it, **Then** nothing is created and the field says why.
4. **Given** invalid input (empty name, PIN of 3 digits, PIN 1111 or 1234), **When** the owner saves,
   **Then** nothing is created and each invalid field shows why.
5. **Given** two businesses, **When** the owner of one lists staff, **Then** they see only that
   business's staff.

---

### User Story 2 - Change roles, deactivate and reactivate (Priority: P1)

The owner changes a person's role (for example promotes a cashier to manager), deactivates someone
who left (they can no longer sign in, but their history stays), and reactivates them if they return.
The owner can also reset a staff member's credentials.

**Why this priority**: staff turnover is constant in retail; a person who left must lose access
immediately (Constitution VIII), and past sales must keep naming who made them (Constitution IV).

**Independent Test**: change a cashier to manager; deactivate them and confirm they are marked
inactive and their credentials no longer verify; reactivate and confirm they verify again.

**Acceptance Scenarios**:

1. **Given** a cashier, **When** the owner changes their role to manager, **Then** the list shows
   manager.
2. **Given** an active staff member, **When** the owner deactivates them, **Then** they are inactive,
   their credentials no longer verify, and they remain in the list (filtered as inactive).
3. **Given** an inactive staff member, **When** the owner reactivates them, **Then** they are active
   again with the same identity.
4. **Given** a staff member, **When** the owner resets their PIN, **Then** the old PIN no longer
   verifies and the new one does.
5. **Given** a cashier who knows their current PIN, **When** they change it (through the operation
   #7's POS screen will call), **Then** only the new PIN verifies.
6. **Given** a staff member, **When** anyone tries to delete them, **Then** there is no delete: people
   are deactivated, never removed (history stays attributable).

---

### User Story 3 - Co-owners and managers (Priority: P2)

An owner adds another owner (for example a business partner) who then has full rights to the
business. A manager signs in to the back office and manages cashiers only: they cannot see or change
owners' sign-in details, cannot add or change owners or managers, and cannot give anyone a role
above cashier. A business always keeps at least one active owner.

**Why this priority**: shops are often run by partners or by a manager on behalf of the owner; the
rules here are what later features (refunds, reports) rely on.

**Independent Test**: add a co-owner and confirm they can open the business and manage staff; as a
manager, add and deactivate a cashier but fail to add a manager, change an owner, or promote anyone
to manager; try to deactivate the last owner and fail.

**Acceptance Scenarios**:

1. **Given** an owner, **When** they invite a co-owner and the invitation is accepted, **Then** that
   person can open the business and has the same rights as the first owner.
2. **Given** a manager, **When** they add, deactivate, reactivate or reset a cashier, **Then** it
   works.
3. **Given** a manager, **When** they try to add, change or deactivate an owner or a manager, or to
   change anyone's role to manager or owner, **Then** it is refused and nothing changes.
4. **Given** a cashier, **When** they try to use the back office, **Then** they are refused.
5. **Given** a business with one active owner, **When** anyone tries to deactivate that owner or
   change their role, **Then** it is refused with "a business needs at least one active owner".
6. **Given** an owner, **When** they deactivate themselves while another active owner exists, **Then**
   it works and they lose access to that business.
7. **Given** a pending invitation, **When** the owner revokes it, **Then** its link no longer works.

### Edge Cases

- Two people edit the same staff member at the same time: the later save must not silently undo the
  earlier one; it is refused with "changed by someone else, reload".
- A staff member is deactivated while signed in: their next request is refused (enforced fully in #7
  for Android sessions; back-office sessions of managers end on their next request here).
- The same person works at two businesses: they are two separate staff records, one per business
  (tenancy: a staff record belongs to exactly one business).
- An owner removes their own owner role while other owners exist: allowed; the business keeps an
  active owner.
- An owner deactivated in one business keeps access to their other businesses.
- Credentials are never shown again after saving; a forgotten credential is reset, not revealed.

## Requirements *(mandatory)*

### Functional Requirements

**Staff and roles**

- **FR-001**: Each business MUST have a staff list; each staff member belongs to exactly one business
  and has a display name, a role (owner, manager or cashier), an active/inactive state, and creation
  and change times.
- **FR-002**: Owners MUST be able to add cashiers and managers directly (POS username and PIN) and to
  invite managers and co-owners by email (FR-011); an owner always has a back-office account, so owners
  join only by invitation or by creating the business. Managers MUST be able to add only cashiers.
  Cashiers MUST NOT use the back office.
- **FR-003**: Owners MUST be able to change any staff member's role; managers MUST NOT change roles
  at all (they manage cashiers, not promotions).
- **FR-004**: Staff MUST be deactivated and reactivated, never deleted. A deactivated person cannot
  sign in anywhere for that business; their identity stays attached to past records.
- **FR-005**: A business MUST always have at least one active owner; any change that would leave none
  MUST be refused.
- **FR-006**: Every role rule MUST be enforced by the server, whatever the client sends; a refused
  action changes nothing and says why.
- **FR-007**: Concurrent edits to the same staff member MUST NOT overwrite each other silently.

**Sign-in credentials** (used by #7)

- **FR-008**: Every staff member who uses the POS (cashiers and managers; owners optionally) MUST have
  a POS username, unique within the business, and a PIN of 4 to 6 digits. PINs that are trivially
  guessable (all one digit such as 0000, simple runs such as 1234 or 4321) MUST be refused.
- **FR-009**: PINs MUST be stored only as slow, salted hashes and never shown after they are set. An
  owner can set or reset any staff member's PIN; a manager can set or reset cashiers' PINs only.
- **FR-010**: A cashier MUST be able to change their own PIN by giving the current one. The rule and
  its storage are part of this feature; the screen and the signed-in POS session that use it are #7.
- **FR-011**: Co-owners and managers get back-office access through an email invitation: the invited
  person follows a single-use link (valid 7 days), signs in with an existing owner account or creates
  one (email, password, verified email; feature 005 rules), and is then linked to the business with
  the invited role. Nobody else ever sets or sees their password.
- **FR-011a**: Invitations MUST be revocable until accepted, MUST be usable only by the invited email
  address, and MUST NOT reveal whether that address already has an account.

**Isolation and audit**

- **FR-012**: Staff of one business MUST be invisible and unchangeable from any other business; every
  staff endpoint has an isolation test (feature 003 harness).
- **FR-013**: Every staff change (add, role change, deactivate, reactivate, credential reset) MUST be
  recorded with who did it and when.

### Key Entities

- **Staff member**: a person working at one business: display name, role, active state, POS username
  and PIN (hashed), optional link to an owner account for back-office access, version for
  concurrent-edit detection.
- **Invitation**: an offer of a role (owner or manager) in one business to one email address; pending,
  accepted, revoked or expired.
- **Role**: owner, manager or cashier. Owner of a business = staff member with role owner (feature 005
  owners become owner staff members of their businesses).
- **Staff change record**: who changed which staff member, what changed, when.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An owner adds a cashier in under 1 minute from the open business.
- **SC-002**: In isolation tests, 0 staff records of another business are ever listed, read or
  changed, for every staff operation.
- **SC-003**: Every role rule in FR-002–FR-005 has an automated test proving the refused case changes
  nothing (managers raising roles, managers touching owners, removing the last owner).
- **SC-004**: A deactivated staff member's credentials fail verification immediately (0 seconds
  grace), and pass again immediately after reactivation.
- **SC-005**: No credential is ever readable in plain text, in the database, logs or any response.

## Assumptions

- Owners from feature 005 are migrated to owner staff members of the businesses they own; the
  `business_owners` link remains the source for owner accounts (planning decides the exact shape).
- Managers' back-office permissions in this feature cover staff management only; catalog, taxes and
  reports add their own role rules when they are built.
- Credential policy for POS sign-in follows NIST guidance adapted to shared shop devices; rate
  limiting of POS sign-in attempts is part of #7.
- No email is sent to cashiers; their PIN is handed over in person.
- A manager may optionally also be given a POS username and PIN to work the till; owners may too.
