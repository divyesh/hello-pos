# Data Model: Pricing Engine

No database changes. These are the engine's input and output shapes, identical on both sides (field
names follow the shared cases, `contracts/pricing-cases.json` version 2). Money is minor units at the
boundary and exact decimals inside.

## SaleInput

| Field | Type | Rules |
|-------|------|-------|
| currency | ISO 4217 code | must be in the engine's currency table (research R3) |
| pricing | `taxExclusive` \| `taxInclusive` | |
| cashRoundingStep | minor units | 0/absent = none; else 5, 10 or 100 for 2-digit currencies |
| depositsTaxable | bool | default false |
| lines | LineInput[] | at least 1 |
| orderDiscount | Discount? | percent 0–100 or fixed ≥ 0 |
| tip | `{ amount }` or `{ percentOfSubtotal }` | ≥ 0 |
| payments | PaymentInput[] | applied in order; may be empty |

## LineInput

| Field | Type | Rules |
|-------|------|-------|
| id | string | unique in the sale |
| unitPrice | minor units | ≥ 0; the entered price for open-price lines, the new price when overridden |
| originalUnitPrice | minor units? | present when overridden; not used in the arithmetic |
| openPrice | bool | informational |
| quantity | decimal string | > 0, ≤ 3 decimals, ≥ minQuantity, multiple of step |
| unit | `piece` \| `g` \| `kg` \| `ml` \| `l` | default `piece` |
| minQuantity, step | decimal strings | default `1` |
| itemDiscount | Discount? | percent 0–100, or fixed per unit ≥ 0 |
| depositPerUnit | minor units? | ≥ 0 |
| taxProfile | `{ name, kind, components[] }` | kind `taxable` \| `zeroRated` \| `exempt`; components `{ label, ratePercent }` (none or 0% for the other kinds) |

## PaymentInput

`{ method: cash | card | other, amount? (card/other), tendered? (cash) }`

## PricedSale

| Field | Meaning |
|-------|---------|
| lines[] | id, lineAmount, itemDiscount, orderDiscountShare, taxable (exclusive: pre-tax; inclusive: gross after discounts), deposit |
| taxes[] | label, ratePercent, profile (inclusive only), base, amount |
| baseByKind | taxable, zeroRated, exempt bases |
| subtotal, taxTotal, tip, total | research R8 |
| cashDue | cash-rounded total (when a step is set) |
| payments[] | method, applied, rounding, change |
| cashRounding, change | sums over payments |
| remaining | balance after all payments |

or **Refusal**: problems `{ code, lineId?, field? }` (research R10).

## State

None: the engine is a pure function `price(SaleInput) → PricedSale | Refusal`.
