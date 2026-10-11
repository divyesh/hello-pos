# Feature Specification: Continuous Integration

**Feature Branch**: `004-continuous-integration`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Continuous integration (roadmap #4, CI half only; staging deployment on
DigitalOcean is deferred to launch preparation and moved in the roadmap). Every pull request and
every push to main automatically builds and tests all three applications on hosted runners and
reports pass/fail per check, so main stays buildable with all tests passing. Checks: backend build
and tests including the real-PostgreSQL tests; web back office build and tests; Android build and
unit tests; contract drift (the committed web API client must match what the contract generates,
and the contract must be valid); the API container image builds; the local environment definition
is valid. Results within a reasonable time with caching; a later push to the same pull request
cancels the earlier run; checks run with read-only repository permissions and no secrets;
third-party automation pinned to exact versions. The repository owner can make these checks
required before merging (documented steps). Out of scope: deployment, staging, publishing images,
release signing, code coverage gates."

## User Scenarios & Testing *(mandatory)*

The users are the people who change Hello POS: the owner reviewing pull requests and anyone
contributing to the open-source repository. The value is that nothing reaches `main` without every
application building and every test passing, checked the same way for everyone (Constitution,
Development Workflow: "`main` MUST stay buildable with all tests passing").

### User Story 1 - Every change is checked before it can be merged (Priority: P1)

A contributor opens or updates a pull request. Within minutes the pull request shows one check per
application (backend, web back office, Android) plus the contract and container checks, each passing
or failing with a link to its log. The owner can see at a glance whether the change is safe to merge.

**Why this priority**: this is the whole point of the feature; everything else refines it.

**Independent Test**: open a pull request with a harmless change and confirm every check runs and
passes; push a commit that breaks one backend test and confirm only the backend check fails and its
log names the failing test.

**Acceptance Scenarios**:

1. **Given** a pull request targeting `main`, **When** it is opened or updated, **Then** every check
   runs automatically and reports pass or fail on the pull request.
2. **Given** a change that breaks a test in one application, **When** the checks run, **Then** that
   application's check fails, its log names the failing test, and the other checks still report
   their own results.
3. **Given** a change that only touches documentation, **When** the checks run, **Then** they still
   run and pass (no change can skip the checks by its file types).
4. **Given** a push to `main` (for example a merge), **When** it lands, **Then** the same checks run
   on `main`.
5. **Given** a pull request from a fork, **When** the checks run, **Then** they run without access to
   any secret and cannot change the repository.

---

### User Story 2 - The contract cannot silently drift (Priority: P2)

A contributor changes the API contract but forgets to regenerate the committed web client (or edits
the generated client by hand). The contract check fails and says which files differ and how to fix
it. An invalid contract also fails the check.

**Why this priority**: the constitution makes the contract the source of truth (Principle V); the
backend and Android already test against it on every build, but the committed web client can only be
checked here.

**Independent Test**: change an example in the contract without regenerating the client and confirm
the contract check fails naming the client files and the regeneration command; regenerate and
confirm it passes.

**Acceptance Scenarios**:

1. **Given** a contract change without a regenerated web client, **When** the checks run, **Then** the
   contract check fails, listing the files that differ and the command to regenerate them.
2. **Given** a hand edit to a generated client file, **When** the checks run, **Then** the contract
   check fails the same way.
3. **Given** a contract that is not a valid API description, **When** the checks run, **Then** the
   contract check fails naming the problem.

---

### User Story 3 - What ships can be built (Priority: P3)

The checks also prove that the API container image builds from the repository and that the local
environment definition is valid, so problems in packaging are found on the pull request, not when
someone next starts the environment or deploys.

**Why this priority**: valuable but secondary to tests; it protects feature 002's environment and the
future deployment.

**Independent Test**: break the container build file and confirm the image check fails; break the
environment definition and confirm its check fails.

**Acceptance Scenarios**:

1. **Given** a change, **When** the checks run, **Then** the API image is built (not published) and the
   check fails if the build fails.
2. **Given** an invalid environment definition, **When** the checks run, **Then** the environment check
   fails naming the problem.

---

### User Story 4 - Checks are fast, cheap and trustworthy (Priority: P3)

Repeated runs reuse downloaded dependencies so results arrive quickly; pushing a new commit to a pull
request cancels the now-outdated run; the checks run with read-only access and pinned third-party
automation, so the pipeline itself is not a supply-chain risk for an open-source project.

**Why this priority**: keeps the checks pleasant to use and safe to run on untrusted contributions.

**Independent Test**: push twice in quick succession and confirm the first run is cancelled; compare
a cold and a warm run's duration; inspect the pipeline definition for permissions and pinned versions.

**Acceptance Scenarios**:

1. **Given** a run in progress for a pull request, **When** a new commit is pushed to it, **Then** the
   older run is cancelled and only the newest result counts.
2. **Given** a second run with unchanged dependencies, **When** it runs, **Then** it reuses cached
   dependencies and finishes faster than the first.
3. **Given** the pipeline definition, **When** it is reviewed, **Then** it grants only read access to
   repository contents, uses no secrets, and refers to every third-party action by an exact,
   immutable version.

### Edge Cases

- A test is flaky: the check fails; reruns are possible from the pull request page, and flakiness is
  fixed in the test, not hidden by automatic retries.
- The hosted runner's container service is slow to start: database tests wait for it within their
  existing timeouts; a genuine failure still fails the check.
- A dependency source is temporarily unreachable: the check fails clearly (download error) and can be
  rerun; it never passes without running the tests.
- The owner has not yet made checks required: the checks still run and report, but merging is not
  blocked until the documented setting is applied.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The checks MUST run automatically on every pull request targeting `main` (opened,
  updated, reopened) and on every push to `main`, regardless of which files changed.
- **FR-002**: There MUST be a separately reported check for each of: backend (build and all tests,
  including those against a real database), web back office (build and all tests), Android (build and
  all unit tests), contract (validity and generated-client drift), API container image (builds), and
  local environment definition (valid).
- **FR-003**: A failing check MUST show which step failed and, for test failures, which tests failed;
  test results MUST be available from the check.
- **FR-004**: The contract check MUST regenerate the committed web client from the contract with the
  pinned generator and fail when anything differs, listing the differing files and the regeneration
  command.
- **FR-005**: The checks MUST build with the same pinned toolchain versions as developers (SDK pin,
  build-tool wrapper, version catalog), so a check passes exactly when a correct local build passes.
- **FR-006**: The image check MUST build the API image without publishing it anywhere.
- **FR-007**: A newer run for the same pull request or branch MUST cancel an older one still running.
- **FR-008**: Dependency downloads MUST be cached between runs and keyed so that a dependency change
  invalidates the cache.
- **FR-009**: The checks MUST run with read-only repository permissions, MUST NOT use secrets, and
  MUST be safe to run on pull requests from forks.
- **FR-010**: Every third-party automation step MUST be pinned to an exact, immutable version.
- **FR-011**: Automatic retries of failed tests MUST NOT be used.
- **FR-012**: The README MUST document the checks, how to reproduce each locally, and the steps for the
  repository owner to make them required before merging.
- **FR-013**: The roadmap MUST record that staging deployment is deferred to launch preparation.

### Key Entities

- **Check**: one independently reported verification on a pull request or push (backend, web,
  Android, contract, image, environment), with a pass/fail status, a log, and test results where
  applicable.
- **Run**: one execution of all checks for a specific commit; superseded runs are cancelled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of pull requests and pushes to `main` get a result for every check.
- **SC-002**: With warm caches, all checks finish within 15 minutes of a push (the slowest check, not the
  sum).
- **SC-003**: A deliberately broken test, contract client, image definition or environment definition
  each makes exactly its own check fail (4 out of 4 detected).
- **SC-004**: Zero secrets are available to the checks, and 100% of third-party automation references
  are exact, immutable versions.
- **SC-005**: Once the owner applies the documented setting, 0 pull requests can be merged with a failing
  or missing check.

## Assumptions

- Hosted runners are GitHub's standard Linux runners, which provide a container engine (used by the
  real-database tests) and the Android SDK. Usage is free for public repositories.
- The repository is public (open source under Apache-2.0); no private dependencies are needed.
- The checks run the tools directly on the runner; the developer container wrapper is for local
  machines and is not required in CI.
- Android instrumented (on-device) tests do not exist yet; only unit tests run.
- Branch protection is a repository setting the owner applies by hand; this feature documents it.
- Staging and production deployment move to launch preparation (roadmap Phase 8); nothing is deployed
  or published here.
