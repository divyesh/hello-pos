# Data Model: Repository Skeleton

This feature stores no data: there is no database yet (roadmap #2). It defines two shapes that
cross application boundaries.

## HealthStatus

Returned by `GET /health` ([contract](contracts/openapi.yaml)) and shown on the web and Android
start screens.

| Field | Type | Rules |
|-------|------|-------|
| `service` | string | Always `hello-pos-api`. |
| `version` | string | Build version of the running API; `0.1.0-dev` until CI assigns versions (#4). |
| `status` | enum | `available` or `degraded`. This feature always returns `available`; `degraded` is reserved for when dependencies such as the database exist. |
| `serverTimeUtc` | date-time | Current UTC time, ISO 8601 ending in `Z`. |

No other fields are allowed (`additionalProperties: false`), so the response cannot leak
configuration or internals (FR-007).

### Client view state

Both clients map the call to one of three start-screen states:

| State | When | Shown |
|-------|------|-------|
| Checking | Call in flight | Progress indicator |
| Available | `200` with a valid body | Status and version |
| Unavailable | Timeout (5 s), network error, non-`200`, or invalid body | "API unavailable" and a retry action |

## PricingCase file

`contracts/pricing-cases.json`, validated by
[pricing-cases.schema.json](contracts/pricing-cases.schema.json).

| Field | Type | Rules |
|-------|------|-------|
| `version` | integer | `1` |
| `cases` | array of PricingCase | Empty in this feature; cases are added from the pricing engine (#10) onward |

A **PricingCase** has an `id` (lowercase, hyphenated), a `description`, an `input` (currency,
tax-inclusive or exclusive pricing, cash rounding step, lines with unit price, quantity, item
discount and tax components, order discount, tip, payment) and an `expected` result (per-line
amounts, tax lines with base and amount, subtotal, tax total, tip, total, cash rounding, amount
due, change). All money is integer minor units; quantities and percentages are invariant-culture
decimal strings. The calculation each case checks is defined in
[specs/pricing-rules.md](../pricing-rules.md).
