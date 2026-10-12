# Pricing Rules

Domain rules for how a sale is priced. Every feature that touches money follows this document, and
`contracts/pricing-cases.json` turns each rule into test cases that both the .NET and Kotlin
suites run (Constitution, Principle I).

Status: agreed 2026-10-10; open questions resolved or deferred 2026-10-11 by the pricing engine
(feature #10, `specs/009-pricing-engine/`). Remaining deferrals are listed at the end.

## 1. Money

- Amounts are stored as `decimal` / `BigDecimal` / Postgres `numeric`, and sent over the API as
  int64 minor units (cents, paise) with the business's ISO 4217 currency code.
- Unless a rule below says otherwise, rounding is half-up to the currency's minor unit.

## 2. Products, units and quantities

- Units: piece, g, kg, ml, l.
- Each product has a price per unit, a minimum sale quantity, and a step size (quantity goes up
  and down by the step).
- Quantities have up to 3 decimal places. The cashier types weights by hand; there is no scale
  integration in v1.
- **Open-price products**: the cashier enters the price at checkout (e.g. a custom cake).
- **Price override** on a normal product requires manager or owner approval. Every override is
  recorded in the sale's audit trail (who approved it, original and new price).
- Quantities are always positive. A sale never contains return lines: returns are refunds (#19), and
  an exchange is a refund plus a new sale.
- **Container deposits**: a product may carry a deposit per unit (e.g. 0.10 per can). The line's deposit
  = deposit per unit × quantity, rounded, shown separately. Deposits are never discounted and are left
  out of the tip percentage base. A business setting decides whether deposits are taxable; if so the
  deposit is taxed with the line's tax profile.

## 3. Tax profiles

- A business defines any number of tax profiles. A profile has one or more tax components (label
  plus rate) and a kind: **taxable**, **zero-rated**, or **exempt**.
  - Examples: *Zero-rated* (0%); *HST 13%*; *GST 5%* = CGST 2.5% + SGST 2.5%; *Exempt*.
- A category has a default profile; a product may override it.
- A profile change takes effect from a chosen date. Completed sales keep the tax lines they were
  sold with.
- Each business is either **tax-exclusive** (tax added on top, typical in USA/Canada) or
  **tax-inclusive** (prices include tax, typical in India).
- For India the system reports collected tax, split by profile kind; filing is the owner's job.

## 4. Discounts

- **Item discounts** are scheduled on a product (from/to dates) as a percentage, or a fixed
  amount per unit.
- **Order discount** is entered at checkout as a percentage or a fixed amount. Above a
  per-business limit it needs manager or owner approval.
- Tax is always calculated after discounts.

## 5. Calculation order

For each sale:

1. **Line amount** = unit price × quantity, rounded.
2. **Item discount**: a percentage applies to the line amount; a fixed amount × quantity. Rounded
   per line. Line net = line amount − item discount, never below zero.
3. **Order discount**: a percentage applies to the sum of line nets; a fixed amount is capped at
   that sum. It is then spread across lines in proportion to their line net. The allocation uses
   the largest-remainder method, so the parts add up exactly to the order discount: each share is
   rounded down to the minor unit, and the leftover units go one each to the lines with the largest
   remainders; equal remainders go to the earlier line. Deposits take no part in this.
4. **Taxable amount per line** = line net − its share of the order discount (+ the line's deposit when
   deposits are taxable). For a tax-inclusive business this is the line's gross amount.
5. **Tax, rounded once per invoice**:
   - Tax-exclusive: group lines by tax component (label and rate), add up their taxable amounts,
     multiply by the rate, and round once per component.
   - Tax-inclusive: group lines by tax profile and add up their gross amounts G. For a profile
     with total rate R, each component's tax = G × rate ÷ (1 + R), rounded once per component.
6. **Tip** (optional): an amount, or a preset percentage of the products' pre-tax subtotal after
   discounts (deposits excluded). Rounded. Tips are never taxed. For a tax-inclusive business the
   pre-tax subtotal is, per tax profile, its product gross × 100 ÷ (100 + R), rounded once per profile.
7. **Total** (deposits are part of the subtotal):
   - Tax-exclusive: taxable subtotal + deposits not already in it + tax + tip.
   - Tax-inclusive: gross subtotal after discounts + deposits not already in it + tip (the tax is
     already inside).
8. **Payments and cash rounding**: a sale may be paid by several payments in order. Card and other
   payments are exact and may not exceed the remaining balance. Each business sets a cash step (none,
   0.05, 0.10, or 1.00). A cash payment that settles the remaining balance pays that balance rounded to
   the nearest step, with ties rounding up; the difference is shown as a separate "Rounding" line. A
   cash payment smaller than that is applied exactly, with no rounding. Example: total 30.42, card
   20.00, then cash settles 10.42 → 10.40 (rounding −0.02).
9. **Change** = cash tendered − the (rounded) balance it settled.

The backend recalculates steps 1–9 for every sale and rejects any sale whose submitted totals
differ (Constitution, Principle I).

## 6. Sale numbers

- **Invoice number**: unique and sequential per business, resetting each financial year. Each
  business sets its financial-year start month (India: April; default: January). Format:
  `PREFIX/YY-YY/NNNNN`, at most 16 characters (GST limit), e.g. `ABC/26-27/00042`.
- **Ticket number**: short, resets daily in the business's local time (e.g. `#42`). Used by the
  kitchen and for calling orders.
- Both come from concurrency-safe sequences per business (Constitution, Principle II).

## Resolved and deferred questions

Resolved 2026-10-11 (feature #10):

- **Split payments**: allowed; rounding only on the cash payment that settles the balance (step 8).
- **Discount and tax-inclusive order**: an order discount reduces the tax-inclusive price; tax is then
  extracted from the reduced gross (confirmed).
- **Container deposits**: priced now (section 2). Not discountable; taxable per business setting;
  refunded with the item when refunds exist (#19).
- **Return lines in a sale**: not allowed (section 2).

Deferred:

- **Refunds**: partial refunds and how tax and rounding are reversed — decided in refunds (#19).
- **Discount limit**: the default limit above which an order discount needs approval — decided in
  business settings (#12). The engine only computes amounts.
- **Multi-buy promotions**: their place in the calculation order, how the promotion amount is spread
  for tax and refunds, and whether a product can be in two promotions — decided in promotions (#28).
