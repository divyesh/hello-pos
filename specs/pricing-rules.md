# Pricing Rules

Domain rules for how a sale is priced. Every feature that touches money follows this document, and
`contracts/pricing-cases.json` turns each rule into test cases that both the .NET and Kotlin
suites run (Constitution, Principle I).

Status: agreed 2026-10-10. Open questions are listed at the end.

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
   the largest-remainder method, so the parts add up exactly to the order discount.
4. **Taxable amount per line** = line net − its share of the order discount.
5. **Tax, rounded once per invoice**:
   - Tax-exclusive: group lines by tax component (label and rate), add up their taxable amounts,
     multiply by the rate, and round once per component.
   - Tax-inclusive: group lines by tax profile and add up their gross amounts G. For a profile
     with total rate R, each component's tax = G × rate ÷ (1 + R), rounded once per component.
6. **Tip** (optional): an amount, or a preset percentage of the pre-tax subtotal after
   discounts. Rounded. Tips are never taxed.
7. **Total**:
   - Tax-exclusive: taxable subtotal + tax + tip.
   - Tax-inclusive: gross subtotal after discounts + tip (the tax is already inside).
8. **Cash rounding**: applies only when paying in cash. Each business sets a step (none, 0.05,
   0.10, or 1.00). The cash due is the total rounded to the nearest step, with ties rounding up.
   The difference is shown as a separate "Rounding" line. Card payments are always exact.
9. **Change** = cash tendered − cash due.

The backend recalculates steps 1–9 for every sale and rejects any sale whose submitted totals
differ (Constitution, Principle I).

## 6. Sale numbers

- **Invoice number**: unique and sequential per business, resetting each financial year. Each
  business sets its financial-year start month (India: April; default: January). Format:
  `PREFIX/YY-YY/NNNNN`, at most 16 characters (GST limit), e.g. `ABC/26-27/00042`.
- **Ticket number**: short, resets daily in the business's local time (e.g. `#42`). Used by the
  kitchen and for calling orders.
- Both come from concurrency-safe sequences per business (Constitution, Principle II).

## Open questions

- **Split payments**: can one sale be paid partly in cash and partly by card? If so, how does
  cash rounding apply to the cash part?
- **Refunds**: are partial refunds (some items, or part of an amount) needed, or only full
  refunds? How are tax and rounding reversed?
- **Discount limit**: the default limit above which an order discount needs approval (for
  example 10%).
- **Discount and tax-inclusive order**: for a tax-inclusive business, does an order discount
  reduce the tax-inclusive price? The current rules assume yes.
- **Container deposits** (retail, roadmap #11/#14): a deposit is a separate line per item sold.
  Is it taxed? (Varies by province and state: likely a per-business or per-tax-profile setting.)
  Is it discountable? (Assumed no.) Is it refunded with the item? (Assumed yes.)
- **Multi-buy promotions** (roadmap #28): where they sit in the calculation order relative to item
  and order discounts (assumed: after item discounts, before the order discount), how the promotion
  amount is spread across lines for tax and refunds, and whether a product can be in two promotions.
