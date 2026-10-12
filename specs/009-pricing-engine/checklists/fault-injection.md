# Fault Injection Results (SC-003)

**Date**: 2026-10-11 · **Cases**: 80 in `contracts/pricing-cases.json`

Each row breaks one rounding point in one implementation, runs only the shared cases, and expects at
least one case to fail; the file is restored afterwards. A break that stops the code compiling would not
count as caught (none did).

| Rounding point broken | .NET kernel | Kotlin domain |
|-----------------------|-------------|---------------|
| Line amount truncated instead of rounded | Caught | Caught |
| Item discount percent truncated | Caught | Caught |
| Order-discount spread: ties to the later line | Caught | Caught |
| Tax-exclusive component tax truncated | Caught | Caught |
| Tax-inclusive tax added on top instead of extracted | Caught | Caught |
| Tip percentage base includes deposits | Caught | Caught |
| Cash rounding ties go down | Caught | Caught |
| Half-up rounding becomes half-down everywhere | Caught | Caught |

16 of 16 caught.
