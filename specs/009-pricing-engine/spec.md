# Feature Specification: Pricing Engine

**Feature Branch**: `009-pricing-engine`
**Created**: 2026-10-11
**Status**: Draft
**Input**: Roadmap Phase 3, feature #10. One set of pricing rules (`specs/pricing-rules.md`) computed
identically on the till, on the server and in the back office, so that what the cashier shows, what the
customer pays and what the server records are always the same amounts. The rules are proven by shared,
language-neutral test cases (`contracts/pricing-cases.json`) that every implementation must pass.
Pure computation: no storage, network or clock. Open questions in the pricing rules that affect the
calculation are resolved or explicitly deferred here.

## Clarifications

### Session 2026-10-11

- Q: Split payments? → A: Yes (option A). A sale may be paid with several payments in order; cash
  rounding applies only when cash settles the remaining balance (e.g. card 20.00, then cash for the
  remaining 10.42 → due 10.40). Card and other payments are always exact.
- Q: Container deposits? → A: Priced by this engine now (option A): a deposit amount per unit, shown as
  its own line, never discounted, taxed or not according to a per-business setting.
- Q: Return lines in a sale? → A: No (option A). Quantities are always positive; returns are refunds
  (#19), and an exchange is a refund plus a new sale.

## Scope boundary with later features

| In this feature | Later |
|-----------------|-------|
| Computing a sale: lines, discounts, tax, tip, total, cash rounding, change | Storing products and tax profiles (#11), selling screen (#14) |
| Shared test cases covering every rule, run by every implementation | Saving sales and the server's rejection of mismatches (#15) |
| Resolving pricing-rule open questions that change the arithmetic | Multi-buy promotions (#28), refunds (#19), invoice numbers (#15) |
| Clear refusal of invalid input (negative prices, bad quantities) | Approval prompts for overrides and discounts (#14, limit setting #12) |

## User Scenarios & Testing *(mandatory)*

The "users" of this feature are the cashier and customer (who see the amounts), the owner (whose books
and tax reports depend on them) and the server (which must agree with the till to the cent).

### User Story 1 - A tax-exclusive sale adds up correctly (Priority: P1)

In a Canadian or US shop (prices before tax), a sale with several products, quantities including
weights, item discounts and an order discount produces line amounts, discount shares, tax per tax
component, total, cash rounding and change exactly as the pricing rules say.

**Why this priority**: the USA and Canada are the first billed markets; every sale in them goes through
this path.

**Independent Test**: run the tax-exclusive cases in the shared test file through each implementation;
every expected amount matches exactly.

**Acceptance Scenarios**:

1. **Given** lines with unit prices and quantities up to 3 decimals, **When** priced, **Then** each line
   amount is unit price × quantity rounded half-up to the currency's minor unit.
2. **Given** item discounts (percent of the line, or a fixed amount per unit), **When** priced, **Then**
   each line net is the line amount minus its discount, never below zero.
3. **Given** an order discount (percent, or a fixed amount capped at the sum of line nets), **When**
   priced, **Then** it is spread over lines in proportion to their nets, and the shares add up exactly to
   the discount (largest-remainder method).
4. **Given** lines under several tax components (e.g. GST 5% and PST 7%, or HST 13%), **When** priced,
   **Then** tax is computed per component on the sum of its taxable amounts and rounded once per
   component, not per line.
5. **Given** a tip (amount, or percent of the pre-tax subtotal after discounts), **When** priced,
   **Then** the tip is added to the total and never taxed.

---

### User Story 2 - A tax-inclusive sale adds up correctly (Priority: P1)

In a shop whose prices include tax (typical in India), the same sale features produce a total equal to
the discounted shelf prices, with the tax inside it extracted per component (e.g. CGST 2.5% + SGST 2.5%)
and rounded once per component.

**Why this priority**: the rules must serve every supported country from the start, so the till never
needs a second engine.

**Independent Test**: run the tax-inclusive cases; every amount matches.

**Acceptance Scenarios**:

1. **Given** a profile with total rate R and components, **When** priced, **Then** each component's tax
   is the profile's gross amount × component rate ÷ (1 + R), rounded once per component.
2. **Given** an order discount, **When** priced, **Then** it reduces the tax-inclusive amounts, and the
   tax is extracted from the reduced gross.
3. **Given** zero-rated and exempt items in the same sale, **When** priced, **Then** they carry no tax
   and are reported separately by kind.

---

### User Story 3 - Cash payment, rounding and change (Priority: P1)

When the customer pays cash, the amount due is the total rounded to the business's cash step (none,
0.05, 0.10 or 1.00; ties round up), shown as a separate "Rounding" line, and the change is the cash
tendered minus the amount due. Card payments are exact.

**Why this priority**: Canada has no penny and India rounds to the rupee; wrong change is the most
visible pricing error a shop can make.

**Independent Test**: run the cash cases for each step and for card payments.

**Acceptance Scenarios**:

1. **Given** a total of 10.42 and step 0.05, **When** paying cash, **Then** the due is 10.40, rounding
   −0.02; at 10.43 the due is 10.45, rounding +0.02; with step 0.10 a total of 10.45 (exactly halfway)
   rounds up to 10.50.
2. **Given** the same total paid by card, **When** priced, **Then** there is no rounding line.
3. **Given** cash tendered below the amount due, **When** priced, **Then** the result says how much is
   still due instead of negative change.

---

### User Story 4 - Every implementation agrees (Priority: P1)

The shared test file covers every rule and edge; each implementation (till, server, back office) runs
all of it in its test suite, and a case added for one is automatically run by all. The file is checked
against its schema, so a malformed case fails the build.

**Why this priority**: the server rejects a sale whose totals differ from its own calculation (#15); any
disagreement between implementations would reject real sales.

**Independent Test**: break one rounding step in one implementation; its test suite fails on the shared
cases while the others still pass.

**Acceptance Scenarios**:

1. **Given** the shared cases, **When** each implementation's test suite runs, **Then** every case
   passes, and the number of cases run equals the number in the file.
2. **Given** a case whose expected result is wrong, **When** the suites run, **Then** they fail and name
   the case and the differing amount.
3. **Given** a malformed case, **When** the suites run, **Then** schema validation fails before pricing.

---

### Edge Cases

- Currencies with 0 minor digits (JPY-style), 2 (USD, CAD, INR) and 3 (e.g. KWD): rounding and wire
  amounts follow the currency's digits.
- A 100% item or order discount makes the line or sale zero, never negative; tax is then zero.
- A fixed order discount larger than the subtotal is capped at it.
- Largest-remainder ties (equal remainders) are broken by line order, so every implementation picks the
  same line.
- An empty sale (no lines) is refused.
- Negative unit prices, zero or negative quantities, quantities with more than 3 decimals, quantities
  that are not a multiple of the product's step, discounts over 100% or negative, and unknown currencies
  are refused with a clear reason (quantities are always positive: no return lines).
- Open-price lines carry the price the cashier entered; price-overridden lines carry the new price and
  the original one (the engine prices with the new one; the original is kept for the audit trail).
- A split payment where the card part already covers the whole balance: no cash part, no rounding.
- A cash payment that does not settle the balance (partial cash, then card): exact, no rounding.
- A deposit on a discounted line: the discount applies to the product amount only.
- Very large sales (hundreds of lines, totals near the int64 limit in minor units) compute without
  overflow or loss of precision.
- A tip percentage in a tax-inclusive sale is a percentage of the pre-tax subtotal (gross after
  discounts minus extracted tax).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The engine MUST compute, for a sale input, every amount in `specs/pricing-rules.md`
  section 5 (steps 1–9): per line the line amount, item discount, order-discount share and taxable
  amount; per tax component the base and tax; subtotal, tax total, tip, total, and for cash the rounding,
  amount due and change.
- **FR-002**: The engine MUST be pure: the same input always gives the same output, with no storage,
  network, clock or locale dependence.
- **FR-003**: Money MUST be exact decimal internally; inputs and outputs on the wire are integer minor
  units with the ISO 4217 currency; rounding is half-up to the currency's minor unit unless a rule says
  otherwise.
- **FR-004**: The same rules MUST be implemented for the server and back office and for the till, and
  both MUST pass every case in the shared test file in their normal test runs.
- **FR-005**: The shared test file MUST contain at least one case for every rule in section 5 and every
  edge case above, and MUST be validated against its schema by every suite; the schema MAY be extended
  (new version) for inputs this feature adds.
- **FR-006**: Invalid input MUST be refused with a reason naming the line and field, never priced
  silently.
- **FR-007**: Order-discount allocation MUST be deterministic across implementations, including ties.
- **FR-008**: Tax MUST be rounded once per tax component per sale (tax-exclusive: on the summed taxable
  amounts; tax-inclusive: extracted from the summed gross of each profile).
- **FR-009**: Zero-rated and exempt lines MUST carry zero tax and be reported in the result by kind so
  tax reports can separate them.
- **FR-010**: A sale MAY have several payments, applied in order. After each, the engine reports the
  remaining balance. Cash rounding applies only to a cash payment that settles the remaining balance
  (the balance is rounded to the cash step, ties up); card and other payments are exact and cannot
  exceed the balance. Change arises only from cash tendered above the (rounded) remaining balance.
- **FR-011**: A line MAY carry a container deposit per unit. The deposit total (per unit × quantity,
  rounded) is reported as its own amount per line, is never reduced by item or order discounts, is
  excluded from the tip percentage base, and is taxed with the line's tax profile only when the
  business's "deposits are taxable" setting is on (otherwise untaxed).
- **FR-012**: Quantities MUST be positive; a sale never contains return lines. Returns are refunds
  (#19); an exchange is a refund plus a new sale.
- **FR-013**: `specs/pricing-rules.md` MUST be updated: each open question is marked resolved (with the
  answer) or deferred (with the feature that will decide it).

### Key Entities

- **Sale input**: currency and its minor digits, pricing mode (tax-exclusive or inclusive), cash
  rounding step, deposits-taxable setting, lines, optional order discount, optional tip, payments in
  order (method, amount or cash tendered).
- **Line input**: id, unit price (or entered open price), original price when overridden, quantity,
  unit and step, optional item discount, optional deposit per unit, tax profile (name, kind,
  components).
- **Priced sale**: the computed amounts per line, per tax component and for the sale (FR-001), or a
  refusal with reasons.
- **Shared test case**: id, description, input, expected priced sale (or expected refusal).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of shared cases pass in every implementation; the case count run equals the file's.
- **SC-002**: The shared file has at least 50 cases covering every calculation step, both pricing modes,
  all four cash steps, split payments, deposits and every listed edge case.
- **SC-003**: A deliberate one-unit error in any rounding step of any implementation is caught by the
  shared cases (verified by fault injection during development).
- **SC-004**: Pricing a 200-line sale takes under 50 milliseconds on the till hardware class, so the
  bill updates without visible delay.
- **SC-005**: No open question in the pricing rules that affects the calculation remains unanswered or
  undeferred.

## Assumptions

- Rounding is half-up (away from zero for positive amounts) unless the pricing rules say otherwise.
- Tax-inclusive order discounts reduce the tax-inclusive price (the current rules' assumption, kept).
- The discount approval limit is a setting (#12) and approvals are UI/API concerns (#14, #15); the engine
  only computes amounts.
- Partial refunds and how tax and rounding are reversed are decided with refunds (#19).
- Multi-buy promotions and their place in the calculation order are decided with promotions (#28).
- Unit prices are whole minor units per unit (e.g. 3.49 per kg); finer unit prices are not needed in v1.
