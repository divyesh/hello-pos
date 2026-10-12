# Feature Specification: Catalog on Android

**Feature Branch**: `011-android-catalog`
**Created**: 2026-10-12
**Status**: Draft
**Input**: Roadmap Phase 3, feature #13. A paired Android till downloads its business's catalog (set up
on the web in #11) and keeps it on the device, so it can look up and price products with no network:
a full first download, then only what changed, applied all at once; a barcode index for instant lookup;
prices from the till's pricing engine (#10) that match the server's; and a read-only catalog screen so
the catalog can be checked on the till before selling (#14) exists.

## Clarifications

### Session 2026-10-12

- Q: How old may a till's catalog get, and is selling ever blocked? -> A: Warn after 24 hours without a
  successful refresh; never block (option A). Shops keep selling through long outages.
- Q: Must the stored catalog be encrypted on the device? -> A: No (option A). It holds prices and product
  names, not secrets; Android's private app storage is enough, and session secrets stay separate.

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Download, keep and refresh the catalog on the till; show how old it is | Selling screen, cart, camera and Bluetooth/USB scanner integration (#14) |
| Barcode and PLU lookup on the device, including equivalent code forms | Unknown-barcode handling during a sale (#14) |
| Prices on the till at a given moment, matching the server | Server recalculation of submitted sales, sale sync (#15) |
| Read-only catalog screen (browse, search, look up a code) | Promotions (#28), stock levels (Phase 9), product images |
| Erasing the catalog when the till is unpaired or moves business | |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The till gets its catalog (Priority: P1)

A newly paired till, or a staff member signing in on one, gets the business's whole catalog: categories,
products (units, quantity rules, open price, barcodes, age restriction, container deposit), tax profiles
with all their dated rates, scheduled item discounts, and the business's pricing mode, currency and
"deposits are taxed" setting. The till keeps it, so restarting the app or losing the network does not
lose it.

**Why this priority**: nothing can be looked up, priced or sold on the till without the catalog.

**Independent Test**: set up a catalog on the web, pair a till and sign in; switch the network off and
restart the app; the catalog is complete and the till shows when it was last updated.

**Acceptance Scenarios**:

1. **Given** a paired till with no catalog, **When** a staff member of any role signs in with the network
   available, **Then** the whole catalog of that business is downloaded and kept on the till.
2. **Given** a downloaded catalog, **When** the network is off and the app restarts, **Then** the full
   catalog is still available and the till shows how old it is ("Catalog updated 2 hours ago").
3. **Given** a download that fails or is cut off part way, **When** the till next looks at its catalog,
   **Then** it still has its previous copy unchanged (or none, on a first download, with a clear message
   and a way to retry); it never holds a partly downloaded catalog.
4. **Given** a till with no catalog at all and no network, **When** a staff member signs in, **Then** the
   till says the catalog has not been downloaded yet and offers to retry.

---

### User Story 2 - Keep the catalog up to date (Priority: P1)

When a manager changes a price, adds a product or schedules a discount on the web, the tills pick it up
without staff having to do anything, and only the changes are downloaded.

**Why this priority**: a till selling from yesterday's prices charges customers wrongly.

**Independent Test**: with a till holding the catalog, change a price, add a product and deactivate
another on the web; within the refresh interval (or on "Refresh now") the till shows all three changes,
and the download contains only the changes.

**Acceptance Scenarios**:

1. **Given** a till with a catalog, **When** a staff member signs in, the refresh interval passes while the
   app is open, or someone taps "Refresh now", **Then** the till asks for what changed since its copy and
   applies it.
2. **Given** several changes made on the web, **When** the till refreshes, **Then** all of them are
   applied together; at no moment does the till show some but not others.
3. **Given** nothing changed, **When** the till refreshes, **Then** almost nothing is downloaded and "last
   updated" moves to now.
4. **Given** the network is down, **When** a refresh is due, **Then** the till keeps its copy, shows its
   age, and tries again later; staff are not interrupted.
5. **Given** a till whose copy is too old for changes alone to bring it up to date, **When** it refreshes,
   **Then** it downloads a full copy instead, still applied all at once.
6. **Given** no successful refresh for more than 24 hours, **When** staff look at the till, **Then** a
   warning shows the catalog's age; the till keeps working (it never blocks on age).

---

### User Story 3 - Find a product by barcode or name, with the right price (Priority: P1)

On the till, staff browse categories, search by name, or type (or scan with a scanner that acts as a
keyboard) a barcode or PLU code, and see the product with its current price, any active discount, unit,
tax profile and flags (age restriction, deposit, open price).

**Why this priority**: this is exactly what the selling screen (#14) will need; checking it now proves the
catalog on the till is right before money is involved.

**Independent Test**: look up products by each barcode type, by PLU code and by name with the network
off; compare each shown price with the web's price for the same moment.

**Acceptance Scenarios**:

1. **Given** a product with barcode 067000004568 (UPC-A), **When** someone enters 067000004568 or its
   EAN-13 form 0067000004568, **Then** the same product is found.
2. **Given** a PLU code such as 4011, **When** it is entered, **Then** the product with that PLU is found.
3. **Given** a code with a wrong check digit or an unknown code, **When** it is entered, **Then** the till
   says why ("check digit is wrong" or "no product with this code") and shows nothing else.
4. **Given** a scheduled discount active today in the business's time zone, **When** the product is
   shown, **Then** the price shows the regular and the discounted price; a discount that starts tomorrow
   is not applied yet.
5. **Given** a tax rate change taking effect at the start of a date, **When** the product is shown on the
   day before and on that day, **Then** each shows the tax in force on that day.
6. **Given** a deactivated product or a product in an inactive category, **When** someone searches or
   enters its barcode, **Then** it is not offered (it stays on the device for sales history).
7. **Given** categories, **When** staff browse, **Then** active categories are shown in sort order with
   their active products by name.

---

### User Story 4 - A till only ever holds its own business's catalog (Priority: P1)

When a till is unpaired or paired to another business, the old catalog is erased before anything else is
shown.

**Why this priority**: Constitution III; one business must never see another's prices or products, even on
a shared device.

**Independent Test**: pair a till to business A, download; unpair; pair to business B; nothing from A is
visible or stored.

**Acceptance Scenarios**:

1. **Given** a till holding business A's catalog, **When** it is unpaired, **Then** the catalog is erased.
2. **Given** a till paired to business A, **When** it is paired to business B, **Then** A's catalog is
   erased and B's is downloaded; no screen ever shows A's data under B.
3. **Given** a request for catalog data, **When** the server answers, **Then** it contains the paired
   business's catalog only, whatever the request says.

### Edge Cases

- A refresh arrives while someone is looking at a product: the screen moves to the new data at once and
  never shows a mix of old and new.
- A product's barcode moves to another product on the web: after the refresh the code finds the new
  product only.
- A product is deactivated and later reactivated: it is offered again after the refresh.
- The business changes currency or pricing mode: the till shows prices in the new setting after the
  refresh; nothing is converted.
- The same code typed as UPC-A on one product and as EAN-13 on another: cannot happen, because #11 already
  stores every UPC-A in its EAN-13 form and barcodes are unique per business; the till relies on this and
  a test proves it.
- The device clock is wrong: prices for "now" use the device's time; the till shows a warning when its
  clock differs from the server's by more than 5 minutes at refresh.
- A very large catalog (10,000 products): first download and lookups stay within the success criteria.
- The till's storage is full: the refresh fails cleanly and the previous copy stays.
- Staff sign out: the catalog stays on the till for the next sign-in (it is the business's, not the
  person's).

## Requirements *(mandatory)*

### Functional Requirements

**Download and keep**

- **FR-001**: A paired till MUST be able to download its business's whole catalog: active and inactive
  categories and products, all barcodes, tax profiles with every dated version and its components,
  scheduled item discounts (past ones may be left out), and the business's pricing mode, currency,
  time zone and "deposits are taxed" setting.
- **FR-002**: A paired till MUST be allowed to download the catalog of the business it is paired to,
  whether or not a staff member is signed in (the catalog is the business's, and the lock screen must be
  able to refresh it); any staff role may use it. The business MUST come from the till's credentials
  only, never from the request; a revoked till MUST be refused.
- **FR-003**: The till MUST keep the catalog across app restarts and with no network.
- **FR-004**: The till MUST record when its copy was last brought up to date and show its age wherever
  the catalog is shown.

**Refresh**

- **FR-005**: The till MUST refresh when a staff member signs in, every 5 minutes while the app is in
  use and online, and when staff tap "Refresh now".
- **FR-006**: After the first download, a refresh MUST transfer only what changed since the till's copy;
  when the server cannot provide changes for that copy, the till MUST fall back to a full download.
- **FR-007**: A download or refresh MUST be applied all at once or not at all; the till MUST never show,
  look up or price from a partly applied catalog.
- **FR-008**: A failed refresh MUST leave the previous copy in use, MUST NOT interrupt staff, and MUST be
  retried at the next due time.
- **FR-009**: The till MUST show a warning when its copy has not been refreshed successfully for more than
  24 hours, and MUST NOT block anything because of the catalog's age.

**Lookup**

- **FR-010**: The till MUST find a product by any of its barcodes or PLU codes in under the time in
  SC-002, with no network.
- **FR-011**: Lookup MUST treat equivalent forms of one code as the same code: UPC-A and the EAN-13 with
  a leading zero; leading zeros a scanner may add or drop on these forms. The barcode rules on the till
  MUST give the same answers as the server's for every case in the shared barcode test file.
- **FR-012**: A code MUST belong to at most one product of the business in any of its equivalent forms
  (already guaranteed by #11's normalization and per-business uniqueness; this feature adds a test).
- **FR-013**: A code that fails its check digit or matches nothing MUST be reported with the reason.
- **FR-014**: Search by name MUST match any part of the name, ignoring case and accents, and offer only
  active products in active categories.
- **FR-015**: Deactivated products and inactive categories MUST stay on the till (sales history may refer
  to them) but MUST NOT be offered by browse, search or lookup.

**Prices**

- **FR-016**: The till MUST price a product for a given moment using the pricing engine on the till, fed
  with the tax profile version and item discount in force on that moment's date in the business's time
  zone, the category's default tax profile unless the product overrides it, and the business's pricing
  mode, currency and deposit setting.
- **FR-017**: For every product and moment, the till's unit price, discount and tax MUST equal the
  server's for the same catalog and moment; the shared pricing test cases MUST keep passing on both.

**Screen**

- **FR-018**: The till MUST offer a read-only catalog screen to all staff roles: categories in sort order,
  products by name, search, code lookup (typed, or entered by a scanner acting as a keyboard), and for a
  product its name, price (regular and discounted when a discount is active), unit and quantity step, tax
  profile, and flags for age restriction (with minimum age), container deposit and open price.

**Isolation and storage**

- **FR-019**: Unpairing the till or pairing it to another business MUST erase the stored catalog before
  any other screen is shown.
- **FR-020**: The catalog MUST be stored only in the app's private storage, separate from session secrets;
  it is not encrypted beyond what the device provides (it holds no secrets).
- **FR-021**: Every catalog download MUST be covered by an isolation test proving a till paired to
  business A never receives business B's data.

### Key Entities

- **Catalog copy (on the till)**: the business's categories, products, barcodes, tax profiles with
  versions, discounts and pricing settings, plus the catalog version it corresponds to and when it was
  last brought up to date.
- **Catalog version**: a number that grows with every catalog change of a business; a till's copy is "at"
  one version, and a refresh asks for what changed after it.
- **Catalog change set**: everything added, changed or deactivated between two versions, applied to the
  copy at once.
- **Barcode index**: every stored code in its normalized form, pointing at one product.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A catalog of 10,000 products downloads and is ready to use on a typical till over a normal
  shop connection in under 60 seconds.
- **SC-002**: A barcode, PLU or name lookup on the till shows its result in under 200 ms with 10,000
  products and no network.
- **SC-003**: A price change made on the web shows on an online till within 5 minutes with no staff action,
  and immediately after "Refresh now".
- **SC-004**: A refresh with 10 changed products transfers less than 1% of a full download of a 10,000
  product catalog.
- **SC-005**: For a test catalog covering every pricing case (pricing modes, inclusive and exclusive tax,
  multi-component taxes, discounts, deposits, dated tax changes), 100% of till prices match the server's.
- **SC-006**: In fault tests that cut the network or stop the app at every step of a refresh, the till
  never shows a partly applied catalog (0 occurrences).
- **SC-007**: After unpairing, none of the previous business's catalog remains on the device.

## Assumptions

- Tills are paired and staff sign in as delivered in #7; one till belongs to one business at a time.
- The catalog is the business's, not a staff member's: signing out keeps it; unpairing removes it.
- Scheduled discounts that ended before the download may be left out; current and future ones are needed
  so a till that stays offline keeps pricing correctly as days pass.
- "Every 5 minutes" is a default for this version; push notifications of changes are not needed yet.
- Clock checks use the server's time seen at refresh; the till does not correct its own clock.
- Price- or weight-embedded scale barcodes stay out of scope (decided in #11).
- The web is unchanged; the server gains a catalog download for tills.
