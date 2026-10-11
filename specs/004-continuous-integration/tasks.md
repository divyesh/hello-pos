---
description: "Task list for feature 004: continuous integration"
---

# Tasks: Continuous Integration

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[contracts/ci-checks.md](contracts/ci-checks.md), [quickstart.md](quickstart.md)

**Tests**: The workflow runs the existing suites. Its own behavior is verified by quickstart.md
(deliberate breakages on throwaway commits), which needs a pushed branch and an open pull request.

## Phase 1: Setup

- [X] T001 Add `contracts/*.yaml text eol=lf` and `contracts/*.json text eol=lf` to `.gitattributes`, renormalize (`git add --renormalize contracts`), and regenerate the Kiota client into `web/src/HelloPos.Web/ApiClient/` through `scripts/dotnet.ps1` so `kiota-lock.json` hashes the LF contract (research R5)
- [X] T002 [P] Create `redocly.yaml` at the repository root enabling only `struct` and `no-unresolved-refs` (errors); verify `npx @redocly/cli@2.62.1 lint contracts/openapi.yaml` passes locally (or in a Node container)

## Phase 2: User Story 1 - Every change is checked (P1) 🎯 MVP

- [X] T003 [US1] Create `.github/workflows/ci.yml`: name `CI`; triggers `pull_request` and `push` on `main` (no path filters); top-level `permissions: contents: read`; `concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }`; env `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT`; every `uses:` pinned to the SHAs in research.md with the tag as a comment (FR-001, FR-007, FR-009, FR-010)
- [X] T004 [US1] Add job `backend` (ubuntu-24.04, timeout 20 min): checkout; setup-dotnet with `global-json-file: global.json`; NuGet cache (`actions/cache`, key on `Directory.Packages.props`, `**/*.csproj`, `global.json`, `.config/dotnet-tools.json`); `dotnet test --solution backend/HelloPos.slnx -- --report-xunit-trx --results-directory TestResults`; upload `**/TestResults/**` as artifact `backend-test-results` with `if: always()` (FR-002, FR-003, FR-008)
- [X] T005 [US1] Add job `web`: same pattern for `web/HelloPos.Web.slnx`, artifact `web-test-results`
- [X] T006 [US1] Add job `android` (timeout 30 min): checkout; setup-java Temurin 21; `gradle/actions/setup-gradle`; `./gradlew assembleDebug testDebugUnitTest --console=plain` in `android/`; upload `android/app/build/reports/tests` and `android/app/build/test-results` with `if: always()`

## Phase 3: User Story 2 - The contract cannot silently drift (P2)

- [X] T007 [US2] Add job `contract`: checkout; setup-node 24; `npx --yes @redocly/cli@2.62.1 lint contracts/openapi.yaml`; setup-dotnet; NuGet cache; `dotnet tool restore`; the Kiota command from `web/src/HelloPos.Web/ApiClient/README.md`; fail if `git status --porcelain web/src/HelloPos.Web/ApiClient` is non-empty, printing `git diff --stat`, the file list and the regeneration command (FR-004)

## Phase 4: User Story 3 - What ships can be built (P3)

- [X] T008 [P] [US3] Add job `image`: checkout; `docker build -f backend/src/HelloPos.Api/Dockerfile -t hellopos-api:ci .` (no push, no login) (FR-006)
- [X] T009 [P] [US3] Add job `environment`: checkout; `cp .env.example .env`; `docker compose --profile api --profile tools config --quiet`

## Phase 5: User Story 4 and Polish

- [X] T010 Add a "Continuous integration" section to `README.md`: the six checks and what they prove, how to reproduce each locally (contracts/ci-checks.md), and branch-protection steps for the owner (research R7) (FR-012)
- [X] T011 [P] Update `specs/roadmap.md`: #4 is CI only; add staging deployment to Phase 8 launch preparation (#26) (FR-013)
- [X] T012 Validate the workflow locally where possible (YAML parse; every `uses:` is a 40-hex SHA; no `secrets.`; `permissions` read-only) and record in `specs/004-continuous-integration/checklists/ci-results.md`
- [ ] T013 After the branch is pushed and a PR is open: run quickstart.md steps 1–5 (first run green, cancellation, warm timing, four deliberate breakages each failing only its own check, review) and record results; step 6 (branch protection) is the owner's

## Dependencies

- T001 before T007 (drift check needs the LF-hashed client); T002 before T007.
- T003 before T004–T009 (same file; implement sequentially).
- T013 needs everything pushed.
