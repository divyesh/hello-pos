# Quickstart: Pricing Engine

No running services are needed; the engine is pure computation.

## Validate

- .NET (kernel, used by the API and the web app):
  `scripts/dotnet.ps1 test --project backend/tests/HelloPos.Kernel.Tests`
  Expected: the schema test passes, every case in `contracts/pricing-cases.json` passes, and the
  "ran all cases" assertion equals the file's count (≥ 50).
- Kotlin (Android domain): `cd android; ./gradlew testDebugUnitTest --tests '*pricing*'`
  Expected: the same cases, the same count, all passing; the Konsist rule still finds no Android imports
  in `domain`.
- Full suites as in CI: backend solution, web solution, `./gradlew assembleDebug testDebugUnitTest`.

## Fault injection (SC-003)

For each rounding point (line amount, item discount, order spread, tax, tip, cash rounding), change the
rounding in one implementation (for example half-up → half-even, or drop a remainder unit) and run that
suite: at least one shared case must fail and name the case and amount. Revert.

## Adding a rule later

1. Add cases to `contracts/pricing-cases.json` (and extend the schema if needed); see both suites fail.
2. Change `specs/pricing-rules.md`.
3. Implement in both engines until both suites pass.
