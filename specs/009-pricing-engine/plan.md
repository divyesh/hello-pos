# Implementation Plan: Pricing Engine

**Branch**: `009-pricing-engine` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

## Summary

A pure pricing function, implemented once in the C# kernel (used by the API and the web app) and once in
the Android domain layer (pure Kotlin), that turns a sale input into every amount of
`specs/pricing-rules.md` section 5: line amounts, item discounts, the order-discount spread (largest
remainder, ties to the earlier line), tax per component rounded once per sale (tax-exclusive by
component, tax-inclusive by profile), tip, deposits, total, and ordered payments with cash rounding on
the settling cash payment and change. Both are driven by the same hand-derived cases in
`contracts/pricing-cases.json` (schema version 2, at least 50 cases), which every suite runs in full.
Open questions in the pricing rules are resolved (split payments, deposits, return lines) or explicitly
deferred. Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (kernel); Kotlin 2.4.21 (Android domain)

**Primary Dependencies**: none new. Kernel stays dependency-free; tests use the existing xUnit v3 and
JsonSchema.Net (kernel tests) and JUnit 6, kotlinx.serialization and the existing JSON schema validator
(Android tests).

**Storage**: N/A (pure computation).

**Testing**: shared cases run by `HelloPos.Kernel.Tests` and the Android unit tests; schema validation in
both; fault injection for SC-003.

**Target Platform**: .NET 10 (API container, Blazor WASM) and Android 8.0+ (JVM BigDecimal).

**Project Type**: shared domain library on two platforms.

**Performance Goals**: a 200-line sale priced in under 50 ms on a low-end Android device (SC-004).

**Constraints**: exact decimals only (Constitution I); no clock, locale, I/O; identical results on both
sides including tie-breaking.

**Scale/Scope**: ~10 source files per side, ≥ 50 shared cases, schema v2, pricing-rules update.

## Constitution Check

| Principle | How this plan complies | Status |
|-----------|------------------------|--------|
| I. Money Is Exact | `decimal` / `BigDecimal` inside, int64 minor units at the boundary, half-up at the rule-defined points only; one pricing module per side; shared cases run by both suites; invariant decimal strings for quantities and percentages. | Pass |
| II. Never Lose or Duplicate a Sale | Not affected (no storage); the engine is what #15 uses to reject mismatched totals. | Pass |
| III. Tenant Isolation | No data access. | N/A |
| IV. Append-Only Sales | Not affected. | N/A |
| V. Contract-First | The shared case file and its schema are the contract; changed first (schema v2 + cases) before code. | Pass |
| VI. Test-First | Cases written and seen failing before either engine; fault injection proves they catch errors. | Pass |
| VII. Vertical Slices | Kernel `Pricing/` and Android `domain/pricing/` are the domain modules; no pricing in endpoints, components or view models; Konsist rule keeps `domain` Android-free. | Pass |
| VIII. Secure by Default | No secrets or I/O. | N/A |
| IX. No Silent Failures | Invalid input refused with coded problems, never priced; overflow refused. | Pass |
| X. Simplicity | Two plain implementations and one case file; no multiplatform toolchain. | Pass |

Re-checked after design: still passing.

## Project Structure

### Documentation (this feature)

```text
specs/009-pricing-engine/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/pricing-engine.md
└── checklists/requirements.md
```

### Source Code (repository root)

```text
contracts/pricing-cases.schema.json            # version 2
contracts/pricing-cases.json                   # ≥ 50 hand-derived cases
specs/pricing-rules.md                          # open questions resolved or deferred; deposits, payments
backend/src/HelloPos.Kernel/Pricing/            # Currencies, SaleInput/LineInput/PaymentInput, PricedSale,
                                                # PricingProblem, PricingEngine (+ small internal helpers)
backend/tests/HelloPos.Kernel.Tests/Pricing/    # PricingCasesFileTests (v2), SharedPricingCasesTests,
                                                # PricingEngineTests (unit edges), performance smoke test
android/app/src/main/java/com/recbook/hellopos/domain/money/    # Money (BigDecimal + currency), Currencies
android/app/src/main/java/com/recbook/hellopos/domain/pricing/  # same model and PricingEngine
android/app/src/test/java/com/recbook/hellopos/domain/pricing/  # PricingCasesFileTest (v2), SharedPricingCasesTest,
                                                                # PricingEngineTest, performance smoke test
```

**Structure Decision**: one domain module per side (Constitution I, VII). The web app gets the engine
through its existing kernel reference; no web or API code changes in this feature.

## Complexity Tracking

| Item | Why | Simpler alternative rejected because |
|------|-----|--------------------------------------|
| Two implementations of the same rules | The till must price offline; the server must recompute authoritatively | One shared runtime (KMP/WASM) adds a toolchain and couples the release of both apps |
| Hand-derived expected values | Cases must be an independent check of both engines | Generating expectations from one engine would make it its own oracle |

## Constitution re-check after delivery (T023)

All gates still pass against the delivered code: one pricing module per side (`HelloPos.Kernel/Pricing`,
Android `domain/pricing` + `domain/money`), exact arithmetic with half-up rounding only at the rule
points, 80 shared cases run by both suites, 16 of 16 deliberate rounding faults caught
([checklists/fault-injection.md](checklists/fault-injection.md)). Deviations from the design above:

- **Two refusal rules added during case writing**: duplicate line ids (`duplicateLineId`), and a cash
  payment that covers the exact balance but not the rounded cash due (`invalidPayment`); both are in
  research R9/R10 and the shared cases.
- **Arithmetic uses exact integer fractions** (BigInteger numerators over powers of ten) rather than
  `decimal`/`BigDecimal` division, so the two platforms cannot differ on a rounding tie; money is still
  `decimal`/`BigDecimal` at the edges (Constitution I). Kotlin avoids `BigInteger.longValueExact`
  and `BigInteger.TWO` (API 31/33) for minSdk 26.
- **Android lint now runs in CI** (`lintDebug` in the `android` job). Running it found two errors in
  feature 007, fixed here: `Duration.toSeconds()` (API 31) in the sign-in lockout message would crash
  on Android 8-11, and the idle tracker overrode the restricted `dispatchKeyEvent`; it now uses
  `Activity.onUserInteraction()`.

