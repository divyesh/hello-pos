# Contract: Pricing Engine

The engine is a library, not an endpoint. Its contract is the shared case file and the one function
each side exposes.

## Function

- .NET (`HelloPos.Kernel.Pricing`): `PricingEngine.Price(SaleInput input) → PricingResult` where
  `PricingResult` is either `Priced(PricedSale)` or `Refused(IReadOnlyList<PricingProblem>)`.
- Kotlin (`com.recbook.hellopos.domain.pricing`): `PricingEngine.price(input: SaleInput): PricingResult`
  with the same two outcomes.
- Inputs and outputs carry money as minor units (`long` / `Long`) plus the currency, matching the wire
  format of Constitution I; internal arithmetic is exact decimal.

## Shared cases: `contracts/pricing-cases.json`, schema version 2

Validated by `contracts/pricing-cases.schema.json`. Shape (fields as in [data-model.md](../data-model.md)):

```json
{
  "$schema": "./pricing-cases.schema.json",
  "version": 2,
  "cases": [
    {
      "id": "excl-two-lines-hst",
      "description": "2 × 3.49 + 1.25 kg × 4.00, HST 13% on 11.98 = 1.5574 → 1.56",
      "input": {
        "currency": "CAD",
        "pricing": "taxExclusive",
        "lines": [
          { "id": "a", "unitPrice": 349, "quantity": "2",
            "taxProfile": { "name": "HST", "kind": "taxable", "components": [ { "label": "HST", "ratePercent": "13" } ] } },
          { "id": "b", "unitPrice": 400, "quantity": "1.25", "unit": "kg", "step": "0.005", "minQuantity": "0.005",
            "taxProfile": { "name": "HST", "kind": "taxable", "components": [ { "label": "HST", "ratePercent": "13" } ] } }
        ],
        "payments": [ { "method": "card", "amount": 1354 } ]
      },
      "expected": {
        "lines": [
          { "id": "a", "lineAmount": 698, "itemDiscount": 0, "orderDiscountShare": 0, "taxable": 698, "deposit": 0 },
          { "id": "b", "lineAmount": 500, "itemDiscount": 0, "orderDiscountShare": 0, "taxable": 500, "deposit": 0 }
        ],
        "taxes": [ { "label": "HST", "ratePercent": "13", "base": 1198, "amount": 156 } ],
        "baseByKind": { "taxable": 1198, "zeroRated": 0, "exempt": 0 },
        "subtotal": 1198, "taxTotal": 156, "tip": 0, "total": 1354,
        "payments": [ { "method": "card", "applied": 1354, "rounding": 0, "change": 0 } ],
        "cashRounding": 0, "change": 0, "remaining": 0
      }
    }
  ]
}
```

A case expecting a refusal has `"expectedError": { "code": "notMultipleOfStep", "lineId": "b" }` instead
of `expected`.

## Rules for the file

- Every case id is unique, lower-case with dashes, prefixed by area: `excl-`, `incl-`, `disc-`,
  `spread-`, `tax-`, `tip-`, `cash-`, `split-`, `dep-`, `cur-`, `err-`.
- Expected values are derived by hand (research R11); descriptions show the arithmetic when it is not
  obvious.
- Both suites run every case and assert the count; a new pricing rule starts with new cases here.
