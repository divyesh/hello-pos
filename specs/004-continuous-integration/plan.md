# Implementation Plan: Continuous Integration

**Branch**: `004-continuous-integration` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-continuous-integration/spec.md`

## Summary

One GitHub Actions workflow with six parallel jobs (`backend`, `web`, `android`, `contract`, `image`,
`environment`) on every pull request and push to `main`. Toolchains come from the repository's own
pins; dependencies are cached; superseded runs are cancelled; the workflow is read-only, secret-free
and pins every action by commit SHA. The contract job lints the OpenAPI document and regenerates the
committed Kiota client to detect drift (with contract files pinned to LF so hashes match across
platforms). README documents the checks and the owner's branch-protection steps; the roadmap moves
staging to launch preparation. Decisions: [research.md](research.md); check names:
[contracts/ci-checks.md](contracts/ci-checks.md).

## Technical Context

**Language/Version**: GitHub Actions YAML; existing .NET 10, JDK 21/Gradle 9.8.1, Node 24 (lint only)

**Primary Dependencies**: actions listed in research.md (SHA-pinned); `@redocly/cli` 2.62.1; Kiota 1.36.0
(tool manifest)

**Storage**: N/A

**Testing**: the workflow runs existing suites (backend 53, web 10, Android 10); verification by
deliberate breakages on throwaway commits

**Target Platform**: GitHub-hosted `ubuntu-24.04` runners

**Project Type**: repository automation

**Performance Goals**: slowest job ≤ 15 min warm (SC-002)

**Constraints**: read-only token, no secrets, fork-safe, no path filters, no retries

**Scale/Scope**: 1 workflow, 1 lint config, `.gitattributes` change, README and roadmap edits

## Constitution Check

| Principle | Applies here | Status |
|-----------|--------------|--------|
| I–IV | No product logic. | N/A |
| V. Contract-First | Contract validity and client drift are checked on every PR. | Pass |
| VI. Test-First | Every suite runs on every PR; failures block merge once required. | Pass |
| VII. Vertical Slices | N/A | N/A |
| VIII. Secure by Default | Read-only permissions, no secrets, `pull_request` trigger, SHA-pinned actions. | Pass |
| IX. No Silent Failures | No retries; failing tests named; results uploaded. | Pass |
| X. Simplicity | One workflow; native tools; no extra services. | Pass |
| Workflow gate "CI MUST build all three apps, run tests, verify contract conformance" | Delivered here. | Pass |

Result: **pass** (also after design).

## Project Structure

```text
.github/workflows/ci.yml          # six jobs
redocly.yaml                      # structural lint rules only
.gitattributes                    # + contracts/*.yaml, contracts/*.json eol=lf
web/src/HelloPos.Web/ApiClient/   # regenerated once from the LF contract
README.md                         # "Continuous integration" section + branch protection steps
specs/roadmap.md                  # #4 = CI; staging moved to launch preparation
```

**Structure Decision**: a single workflow file at the conventional location.

## Complexity Tracking

None.
