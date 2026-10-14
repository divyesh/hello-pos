# Feature Specification: Business Settings

**Feature Branch**: `012-business-settings`
**Created**: 2026-10-12
**Status**: Draft
**Input**: Roadmap Phase 3, feature #12. In the web back office, owners set up what selling needs beyond the
catalog: the payment types a till offers, the tables of a restaurant-style business, the limit above which a
cashier's discount needs a manager's approval, and tip presets. Tills receive all of it with their catalog
(#13) so they work offline.

## Clarifications

### Session 2026-10-12

- Q: Who manages payment types? -> A: Owners only (option A); managers can view them. Tables stay with
  owners and managers.
- Q: Default discount limit for a new business? -> A: 0% (option A): every cashier discount needs a
  manager's or owner's approval until the owner raises the limit.

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Payment types recorded at checkout (cash built in; UPI, external card terminal, cheque, vouchers…) | Square and Clover connections and card charging (#20–#22) |
| Areas and tables, a "uses tables" switch, adding many tables at once | Seating orders at tables, open tabs (#17); kitchen stations (#18) |
| Discount approval limit; price overrides always need approval | Asking for and recording the approval at the till (#14) |
| Tip presets | Taking tips at checkout (#14, #15) |
| Settings history (who changed what) | Receipt header and layout (#16), cash drawer and shifts (#29), printers |
| All of the above in the till's catalog download | |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Payment types (Priority: P1)

An owner decides how customers can pay at the till. Cash is always there. They add "UPI", "Card (terminal)"
for an external card machine, "Cheque" and "Gift voucher", put them in the order cashiers should see them,
and say which ones open the cash drawer and which ask the cashier for a reference (a UPI transaction id, a
cheque number).

**Why this priority**: no sale can be completed without knowing how it was paid; India needs UPI and an
external card terminal recorded from the first sale.

**Independent Test**: add three payment types, reorder them, deactivate one; the list and a till's catalog
download show the active ones in order, with Cash first.

**Acceptance Scenarios**:

1. **Given** a new business, **When** an owner opens Payment types, **Then** Cash is listed, active, gives
   change and opens the cash drawer, and cannot be deleted, renamed to another kind or deactivated.
2. **Given** an owner, **When** they add a payment type with a name, a kind (card on an external terminal,
   UPI or other digital wallet, cheque, voucher, other), "opens the cash drawer" and "asks for a reference",
   **Then** it is listed in the chosen position.
3. **Given** a non-cash payment type, **When** it is saved, **Then** it never gives change (only cash does).
4. **Given** payment types, **When** the owner drags them into a new order, **Then** the till shows them in
   that order.
5. **Given** a payment type used by sales, **When** the owner removes it, **Then** it can only be
   deactivated; deactivated types are not offered at the till and can be reactivated.
6. **Given** two payment types, **When** the owner gives them the same name (ignoring case), **Then** the
   second is refused.

---

### User Story 2 - Discount approval limit and price overrides (Priority: P1)

An owner sets how much discount a cashier may give on their own. Above that, the till asks a manager or
owner to approve. Changing the price of a normal (not open-price) product always needs approval.

**Why this priority**: discounts are the most common way money leaks at a till; selling (#14) cannot be built
without the rule.

**Independent Test**: set the limit to 10%; a till's download carries it; the rule is documented in the
pricing rules.

**Acceptance Scenarios**:

1. **Given** an owner, **When** they set the limit to a percentage (0–100) and, optionally, an amount in the
   business currency, **Then** a cashier's discount on one order may not exceed the percentage of the order,
   nor the amount when one is set, without approval.
2. **Given** a manager, **When** they open the setting, **Then** they see it but cannot change it.
3. **Given** the limit, **When** a cashier gives an item discount at the till, **Then** it counts toward the
   same limit as an order discount (scheduled catalog discounts do not count).
4. **Given** a product that is not open-price, **When** a cashier changes its price at the till, **Then**
   approval is always required, whatever the limit.
5. **Given** a new business, **When** nobody has set the limit, **Then** it is 0% with no amount: every
   cashier discount needs approval until the owner raises it.

---

### User Story 3 - Areas and tables (Priority: P2)

The owner or manager of a restaurant turns on "uses tables", adds areas ("Main hall", "Patio") and tables in
them, many at once ("1–20 in Main hall"), with seats, and keeps the plan up to date.

**Why this priority**: restaurant flows (#17) need tables, but retail shops never do, and nothing sells
through tables yet.

**Independent Test**: turn on tables, add an area and tables 1–20 in one step, rename one, move one to the
Patio, deactivate one; the list and a till download match.

**Acceptance Scenarios**:

1. **Given** a business that does not use tables, **When** the owner turns "uses tables" on, **Then** areas
   and tables can be set up; turned off, tables are kept but not offered at tills.
2. **Given** an area, **When** someone adds tables "1" to "20" in one step with 4 seats each, **Then** twenty
   tables are created, named 1 to 20, in that order; names already taken in the business are reported and
   nothing is created.
3. **Given** a table, **When** it is renamed, moved to another area, given another seat count or reordered,
   **Then** the change is saved.
4. **Given** a table or area in use, **When** it is removed, **Then** it can only be deactivated; an area with
   active tables cannot be deactivated.
5. **Given** two tables, **When** they get the same name (ignoring case), **Then** the second is refused:
   staff call tables by name.

---

### User Story 4 - Tip presets (Priority: P3)

An owner who accepts tips turns tipping on and sets up to three preset percentages (e.g. 10%, 15%, 20%) that
the till will offer.

**Why this priority**: useful for restaurants and cafés in North America, not needed to start selling.

**Independent Test**: turn tips on with 10/15/20; a till's download carries them; turn tips off.

**Acceptance Scenarios**:

1. **Given** an owner, **When** they turn tips on and set one to three percentages between 1 and 100, each
   different, **Then** they are saved in ascending order.
2. **Given** tips off, **When** a till downloads, **Then** it gets no presets and offers no tips.

---

### User Story 5 - History and tills (Priority: P2)

Every settings change is recorded (who, when, what changed), and tills receive the settings with their
catalog.

**Why this priority**: settings decide money handling; owners must see who changed them, and tills must have
them offline.

**Independent Test**: change a payment type and the discount limit; the history shows both; a till refresh
receives only what changed.

**Acceptance Scenarios**:

1. **Given** any change in this feature, **When** it is saved, **Then** the history shows who made it, when,
   and each changed value from and to; history cannot be edited.
2. **Given** two people editing the same item, **When** the second saves, **Then** it is refused as changed
   by someone else and the current values are shown.
3. **Given** a paired till, **When** it refreshes its catalog, **Then** it receives payment types, the
   tables setting with areas and tables, the discount limit and tip presets, and after the first download
   only what changed.

### Edge Cases

- An owner deactivates every non-cash type: the till offers Cash only.
- A table name with leading zeros ("07") and "7": different names; staff see what was typed.
- Bulk add with a range larger than 200 tables, or with a prefix ("T1"–"T20"): over 200 is refused; a
  prefix is supported.
- The currency of the business changes (allowed only before any sale, #5): an amount limit is shown in the
  new currency's minor units and the owner is asked to check it.
- A deactivated payment type still appears on past sales and in reports (#24).
- The discount limit is 0%: every cashier discount needs approval.
- Managers trying owner-only changes get a clear refusal, never a half-saved change.

## Requirements *(mandatory)*

### Functional Requirements

**Payment types**

- **FR-001**: Every business MUST have exactly one built-in Cash payment type that is always active, gives
  change, opens the cash drawer, and cannot be deleted, deactivated or changed in kind; its name and position
  may be changed.
- **FR-002**: Owners (managers may only view) MUST be able to add and change payment types with a name (1–40 characters, unique in the business ignoring case),
  a kind (card on an external terminal, UPI or other digital wallet, cheque, voucher, other), "opens the cash
  drawer" (default off), "asks for a reference" (default on for card, UPI and cheque), and a position.
- **FR-003**: Only Cash MUST give change; every other type is recorded for exactly the amount it pays.
- **FR-004**: Payment types MUST be deactivated, never deleted, and may be reactivated; at most 20 active
  types per business.
- **FR-005**: Payment types MUST be shown in the owner's chosen order, Cash first unless moved.

**Discounts and price overrides**

- **FR-006**: Owners MUST be able to set a discount approval limit: a percentage (0–100, at most 2 decimals)
  and optionally an amount in minor units of the business currency; managers may view it. A new business
  starts at 0% with no amount (every cashier discount needs approval).
- **FR-007**: The limit MUST apply to the total of discounts a cashier enters on one sale (order discount
  plus item discounts entered at the till), compared with the sale's amount before those discounts; above
  either the percentage or the set amount, approval by a manager or owner is required. Scheduled catalog
  discounts never count.
- **FR-008**: Changing the price of a product that is not open-price MUST always require manager or owner
  approval, whatever the limit.
- **FR-009**: The rule MUST be recorded in `specs/pricing-rules.md` (it resolves the deferred "discount
  limit" question); asking for and recording approvals at the till is part of selling (#14).

**Tables**

- **FR-010**: Owners and managers MUST be able to turn "uses tables" on or off; off keeps all areas and
  tables but tills do not offer them.
- **FR-011**: Areas MUST have a name (unique, 1–40 characters) and a position; tables MUST have a name
  (unique in the business, 1–20 characters), an area, seats (1–50, default 4), a position and an active flag.
- **FR-012**: Adding tables in bulk MUST create a numbered range (optionally with a prefix and/or suffix) in
  one area, at most 200 at once, all or nothing.
- **FR-013**: Areas and tables MUST be deactivated, never deleted; an area with active tables cannot be
  deactivated.

**Tips**

- **FR-014**: Owners MUST be able to turn tips on or off and set one to three distinct preset percentages
  (1–100, at most 2 decimals), shown in ascending order.

**Everything**

- **FR-015**: Every item MUST carry a version; a save with an older version MUST be refused as changed by
  someone else.
- **FR-016**: Every change MUST be recorded in an append-only settings history (who, when, item, each field
  from and to), visible to owners and managers.
- **FR-017**: All settings MUST be business-owned (Constitution III): every id in a write is checked against
  the open business; another business's items are unknown.
- **FR-018**: A till's catalog download MUST include active and inactive payment types, the tables switch,
  areas and tables, the discount limit and tip presets, with only what changed after the first download.
- **FR-019**: Cashiers have no access to settings in the back office.

### Key Entities

- **Payment type**: name, kind, built-in flag (Cash), opens drawer, asks for reference, gives change (Cash
  only), position, active, version.
- **Area**: name, position, active, version.
- **Table**: name, area, seats, position, active, version.
- **Selling settings** (one per business): uses tables, discount limit percent, optional discount limit
  amount, tips on, tip presets, version.
- **Settings change**: who, when, item, action, changed fields from and to.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An owner sets up 4 payment types, the discount limit and tips in under 5 minutes.
- **SC-002**: A restaurant with 3 areas and 60 tables is set up in under 5 minutes using bulk add.
- **SC-003**: A change saved on the web is on online tills within 5 minutes (catalog refresh), and works on
  tills with no network afterwards.
- **SC-004**: 100% of settings changes appear in the history with who, when and what.
- **SC-005**: No business can see or change another business's settings, by any operation (isolation tests
  on every operation).

## Assumptions

- Businesses, roles and the open business come from #5–#7; owners and managers use the web back office,
  cashiers only the till.
- "Asks for a reference" is a hint for the till (#15 records the reference with the payment); the reference
  is optional at checkout unless the till's later design says otherwise.
- Tables are a restaurant feature; retail businesses simply leave "uses tables" off.
- The amount limit uses the business currency; the percentage limit is the main control.
- Settings are part of the catalog download and change tracking from #13, so tills get them the same way.
- Square and Clover payment types are created by their connection features (#21, #22) and are not editable
  here.
