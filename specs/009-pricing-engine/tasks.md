---
description: "Tasks for the pricing engine (feature #10)"
---

# Tasks: Pricing Engine

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/pricing-engine.md](contracts/pricing-engine.md), [quickstart.md](quickstart.md)

**Tests**: required (Constitution I, VI): shared cases first, seen failing, then each engine.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

---

## Phase 1: Setup

- [X] T001 Update `specs/pricing-rules.md`: resolve split payments (rounding only on the cash payment that settles the balance, research R9), container deposits (per-unit, never discounted, taxable by business setting, excluded from the tip base, R7), return lines (none: refunds #19); mark deferred: partial refunds (#19), discount limit default (#12), multi-buy (#28); confirm tax-inclusive order discounts reduce the inclusive price; add the tie-break rule (earlier line) and the inclusive tip base (R8)
- [X] T002 Schema version 2 in `contracts/pricing-cases.schema.json` per research R11 and data-model.md: tax profile objects (`name`, `kind` taxable|zeroRated|exempt, `components`), line `unit`, `minQuantity`, `step`, `originalUnitPrice`, `openPrice`, `depositPerUnit`; input `depositsTaxable`, ordered `payments` (`method`, `amount` | `tendered`); expected per-line `deposit`, tax rows with optional `profile`, `baseByKind`, `cashDue`, `payments[]` (`method`, `applied`, `rounding`, `change`), `cashRounding`, `change`, `remaining`; `expectedError { code, lineId?, field? }` as the alternative to `expected`; case ids `^[a-z0-9][a-z0-9-]*$` unique
- [X] T003 [P] Update the schema tests to v2: `backend/tests/HelloPos.Kernel.Tests/Pricing/PricingCasesFileTests.cs` and `android/app/src/test/java/com/recbook/hellopos/domain/pricing/PricingCasesFileTest.kt` (malformed v2 case rejected; a case with both `expected` and `expectedError` rejected)

## Phase 2: Foundational (both sides)

- [X] T004 [P] C# model in `backend/src/HelloPos.Kernel/Pricing/`: `Currencies` (USD, CAD, INR, EUR, GBP, AUD = 2; JPY = 0; KWD, BHD = 3), `SaleInput`, `LineInput`, `TaxProfile`, `TaxComponent`, `Discount`, `Tip`, `PaymentInput`, `PricedSale` (+ line, tax row, payment result, base by kind), `PricingProblem` with the stable codes of research R10, `PricingResult` (Priced | Refused)
- [X] T005 [P] Kotlin model in `android/app/src/main/java/com/recbook/hellopos/domain/money/` (`Money` backed by `BigDecimal` + currency, `Currencies` same table) and `.../domain/pricing/` (same types as T004)
- [X] T006 [P] Shared-case runner C#: `backend/tests/HelloPos.Kernel.Tests/Pricing/SharedPricingCasesTests.cs` reads every case, maps JSON → `SaleInput`, runs `PricingEngine.Price`, compares every expected field (or the expected error), reports the case id and differing field; asserts the number of cases run equals the file's
- [X] T007 [P] Shared-case runner Kotlin: `android/app/src/test/java/com/recbook/hellopos/domain/pricing/SharedPricingCasesTest.kt`, same behaviour (kotlinx.serialization)
- [X] T008 Engine skeletons `PricingEngine.Price` / `price` returning a not-implemented refusal; input validation (empty sale, unknown currency, price < 0, quantity ≤ 0 / > 3 decimals / below minimum / not a multiple of step, discount out of range, tip < 0, payment amounts ≤ 0) with coded problems naming line and field (FR-006)

**Checkpoint**: both suites compile; validation cases pass, pricing cases fail.

## Phase 3: User Story 1 - Tax-exclusive sale (P1) 🎯 MVP

**Goal**: USA/Canada sales add up exactly. **Independent test**: all `excl-`, `disc-`, `spread-`, `tax-` exclusive, `tip-` exclusive, `dep-` exclusive and `cur-` cases pass on both sides.

- [X] T009 [US1] Cases in `contracts/pricing-cases.json` (hand-derived, arithmetic in descriptions): single and multi-line; weighed quantities (3 decimals) with steps; open price; price override (original carried, new used); item discount percent and fixed per unit, capped at zero; order discount percent and fixed, fixed above subtotal capped; largest-remainder spread with a tie decided by line order; zero-net lines; GST 5% + PST 7% on the same lines and HST 13% elsewhere (rounded once per component); zero-rated and exempt lines with `baseByKind`; tip amount and percent; deposits untaxed and taxed (`depositsTaxable`), deposit on a discounted line; JPY and KWD currencies; ≥ 25 cases
- [X] T010 [US1] C# exclusive path in `backend/src/HelloPos.Kernel/Pricing/PricingEngine.cs` (+ helpers `Rounding`, `DiscountSpread`): steps 1–4, exclusive tax grouping by component, deposits, base by kind, subtotal, tip, total (research R4–R8) until the US1 cases pass
- [X] T011 [US1] Kotlin exclusive path in `.../domain/pricing/PricingEngine.kt` (+ helpers) until the same cases pass

**Checkpoint**: US1 cases pass in both suites.

## Phase 4: User Story 2 - Tax-inclusive sale (P1)

**Independent test**: all `incl-` cases pass on both sides.

- [X] T012 [US2] Cases: CGST 2.5% + SGST 2.5% extraction rounded once per component per profile; two profiles with the same component label (two rows); order discount reducing inclusive prices; item discounts; zero-rated and exempt; tip percent on the per-profile pre-tax base (R8); taxable and untaxed deposits in inclusive mode; INR and a 3-digit currency; ≥ 10 cases
- [X] T013 [US2] C# inclusive path (grouping by profile, extraction, inclusive subtotal/total/tip base)
- [X] T014 [US2] Kotlin inclusive path

## Phase 5: User Story 3 - Cash, rounding, split payments, change (P1)

**Independent test**: all `cash-` and `split-` cases pass on both sides.

- [X] T015 [US3] Cases: steps none, 5, 10, 100 (minor units) with totals rounding down, up and exactly halfway (ties up); `cashDue`; card exact with no rounding; cash tendered below the due (remaining reported, no rounding, no change); card then cash settling (rounding on the cash part only); cash partial then card; card covering everything; card above the balance (`paymentExceedsBalance`); payment after settled (`paymentAfterSettled`); change; ≥ 12 cases
- [X] T016 [US3] C# payments (research R9)
- [X] T017 [US3] Kotlin payments

## Phase 6: User Story 4 - Every implementation agrees (P1)

- [X] T018 [US4] `err-` cases for every refusal code in research R10 (incl. `amountOutOfRange` for an int64 overflow), so both sides refuse identically; total cases ≥ 50 (SC-002)
- [X] T019 [P] [US4] Unit tests beyond the shared file: C# `backend/tests/HelloPos.Kernel.Tests/Pricing/PricingEngineTests.cs` and Kotlin `.../domain/pricing/PricingEngineTest.kt` (purity: same input twice → equal results; a 200-line sale priced under 50 ms (SC-004); results do not depend on the current culture/locale)
- [X] T020 [US4] Fault injection (SC-003, quickstart): per side, break each rounding point in turn (line amount, item discount, order spread incl. tie-break, tax, inclusive extraction, tip, cash rounding) and confirm at least one shared case fails; revert; record results in `checklists/fault-injection.md`

## Phase 7: Polish

- [X] T021 Run backend, web and Android suites in full; Konsist rule still clean
- [X] T022 [P] README: "Pricing" section pointing to `specs/pricing-rules.md`, the shared cases, and how to add a rule (quickstart); roadmap note that #10 is done
- [X] T023 Re-check the Constitution Check against delivered code; note deviations in plan.md

---

## Dependencies

- Phase 1 → Phase 2 → US1 → US2 and US3 (both extend the US1 engine; US2 and US3 touch different code paths but the same engine files, so in sequence) → US4 → Polish.
- In each story: cases first (both suites fail), then C#, then Kotlin.

## Parallel opportunities

- T003, T004, T005, T006, T007 (different files and languages).
- C# and Kotlin implementation of the same story can proceed side by side once its cases exist.

## Implementation strategy

MVP = Phases 1–3 (tax-exclusive pricing on both sides, the first billed markets), then report; then US2,
US3, US4 and polish.
