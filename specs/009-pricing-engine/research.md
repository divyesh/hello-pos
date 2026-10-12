# Research: Pricing Engine

Decisions for feature #10. Rules source: `specs/pricing-rules.md`; this file fixes the points the
rules leave open or that two implementations must do identically.

## R1. Where the engine lives

- **Decision**: C# in `backend/src/HelloPos.Kernel/Pricing/` (the kernel is already referenced by the API
  and the web app, has no dependencies, and is the "single domain module" of Constitution I for .NET).
  Kotlin in `android/.../domain/pricing/` with the money type in `domain/money/` (pure Kotlin; the
  existing Konsist rule forbids Android imports there).
- **Rationale**: one module per side, no duplication in endpoints, components or view models.
- **Alternatives**: a shared WebAssembly/Kotlin-Multiplatform module (one implementation, but a new
  toolchain and against the plan's "two implementations + shared cases" design); generating one from
  the other (fragile).

## R2. Numbers and rounding

- **Decision**: .NET `decimal`, Kotlin `BigDecimal`. Amounts are converted from minor units at the input
  and back at the output; everything in between is exact. "Round" always means half-up (away from zero
  for the positive amounts the engine produces) to the currency's minor unit: .NET
  `Math.Round(x, digits, MidpointRounding.AwayFromZero)`, Kotlin `setScale(digits, RoundingMode.HALF_UP)`.
- **Cash rounding** is the one exception: nearest multiple of the step, ties **up** (toward +∞):
  `floor(amount / step + 1/2) × step`, computed in minor units.
- **Percentages** are decimals (`"13"`, `"2.5"`, `"12.345"`); a percent amount is `base × p / 100`,
  rounded once.
- **Alternatives**: integers in minor units throughout (simpler arithmetic, but tax-inclusive
  extraction and fractional quantities need intermediate precision anyway).

## R3. Currencies

- **Decision**: both sides hold the same small table of ISO 4217 codes and minor digits: USD, CAD, INR,
  EUR, GBP, AUD (2), JPY (0), KWD, BHD (3). An unknown code is refused. Cases for JPY and KWD keep the
  two tables honest.
- **Alternatives**: platform currency data (`java.util.Currency`, .NET `RegionInfo`) differ between
  runtimes and versions; too risky for amounts that must match exactly.

## R4. Line amounts, quantities, units

- **Decision**: line amount = round(unit price × quantity). Quantities: decimal strings, > 0, at most 3
  decimals, ≥ the line's minimum quantity, and a whole multiple of its step (both default 1 for pieces,
  e.g. 0.001 for weighed goods). Unit (`piece`, `g`, `kg`, `ml`, `l`) is carried for display and
  validation only. Open-price lines carry the entered price as the unit price; overridden lines carry
  both prices and are priced with the new one.
- **No return lines** (clarification): quantity must be positive.

## R5. Discounts and the order-discount spread

- **Item discount**: percent → round(line amount × p / 100); fixed → round(amount per unit × quantity);
  capped at the line amount (line net ≥ 0). Percent must be 0–100; fixed ≥ 0.
- **Order discount**: base = Σ line nets (products only, never deposits). Percent → round(base × p /
  100); fixed → min(amount, base).
- **Spread (largest remainder)**, in minor units: each line's exact share is D × net_i ÷ base; take the
  floor; give the leftover units one each to the lines with the largest fractional parts; **ties go to
  the earlier line** (input order). Lines with net 0 get 0. Shares sum exactly to D.
- **Alternatives**: rounding each share (shares may not sum to D); giving the remainder to the largest
  line (less fair, still deterministic).

## R6. Tax

- **Tax-exclusive**: per line, taxable = line net − order share (+ deposit when deposits are taxable).
  Group by component (label, rate) across all lines and profiles; component tax = round(Σ taxable ×
  rate / 100). Output one row per component with its base.
- **Tax-inclusive**: per line, gross = line net − order share (+ deposit when taxable). Group by **tax
  profile**; G = Σ gross of the profile; for each component of the profile, tax = round(G × rate ÷ (100
  + R)), R = the profile's total rate in percent. Output one row per profile component (base = G). Two
  profiles with an identically labelled component produce two rows (the rules round per profile).
- **Kinds**: zero-rated and exempt profiles have no components (or 0% components) and produce no tax;
  the result also reports the base per kind (taxable, zero-rated, exempt) for tax reports (FR-009).

## R7. Deposits (clarification Q2)

- **Decision**: per line, deposit = round(deposit per unit × quantity), shown separately; never touched
  by item or order discounts. When the business setting `depositsTaxable` is on, the deposit joins the
  line's taxable base (exclusive) or gross (inclusive) under the line's profile; otherwise it is
  untaxed (and reported under no kind).
- Deposits are included in the subtotal and total but **excluded from the tip percentage base**.

## R8. Subtotal, tip, total

- **Subtotal**: exclusive → Σ product taxable + Σ deposits (pre-tax); inclusive → Σ product gross after
  discounts + Σ deposits (tax inside).
- **Tip**: an amount, or round(base × p / 100) where base = the products' pre-tax amount after discounts:
  exclusive → Σ product taxable; inclusive → Σ over profiles of round(product gross of the profile ×
  100 ÷ (100 + R)), one rounding per profile (deposits excluded). Never taxed.
- **Total**: exclusive → subtotal + tax total + tip; inclusive → subtotal + tip.

## R9. Payments and cash rounding (clarification Q1)

- Payments are applied in order to the remaining balance (initially the total):
  - **card / other**: exact; amount must be > 0 and ≤ the remaining balance (else refused).
  - **cash** with tendered T: let D = cash-round(remaining). If T ≥ D, the cash payment **settles** the
    sale: rounding = D − remaining (may be negative), change = T − D, remaining = 0. If T < D, it is a
    partial cash payment: exact, applied amount = T, no rounding, no change. If T is at least the exact
    remaining balance but below D (only possible when the balance rounds up), the payment is refused
    (`invalidPayment`): it neither settles the sale nor fits as a part payment.
  - Any payment after the balance reached 0 is refused.
- The result also reports `cashDue` = cash-round(total) (what the screen shows for an all-cash sale) and
  the remaining balance after all payments (0 when fully paid).
- Step 0 / absent means no cash rounding.

## R10. Refusals

- The engine returns either a priced sale or a list of problems `{ code, lineId?, field }` with stable
  codes (`emptySale`, `unknownCurrency`, `invalidPrice`, `invalidQuantity`, `belowMinimumQuantity`,
  `notMultipleOfStep`, `invalidDiscount`, `invalidTip`, `invalidPayment`, `paymentExceedsBalance`,
  `paymentAfterSettled`, `amountOutOfRange`, `duplicateLineId`). Shared cases may expect a refusal by
  code.
- Results above ±9 223 372 036 854 775 807 minor units are refused (`amountOutOfRange`), not overflowed.

## R11. Shared cases (schema version 2)

- `contracts/pricing-cases.json` is empty today, so version 2 replaces version 1's starting shape
  without migration. Changes: tax profile objects (name, kind, components); line `unit`, `minQuantity`,
  `step`, `originalUnitPrice`, `openPrice`, `depositPerUnit`; input `depositsTaxable` and ordered
  `payments`; expected per-line `deposit`, tax rows with `profile`, `baseByKind`, `cashDue`, payment
  results, `change`, `remaining`; or `expectedError`.
- **How expected values are derived**: by hand from the rules, with the arithmetic written in the case
  description for any non-obvious step. When an implementation disagrees, the amount is re-derived by
  hand; expected values are **never** copied from an implementation's output.
- Each suite asserts it ran exactly as many cases as the file holds (SC-001).

## R12. Test-first and fault injection

- Order of work: schema v2 and cases first (both suites fail: no engine), then the C# engine, then
  Kotlin. After both pass, deliberately break each rounding point in turn (line, item discount, order
  spread, tax, tip, cash) in each implementation and confirm a shared case fails (SC-003); revert.
