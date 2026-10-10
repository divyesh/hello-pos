# Feature Specification: Repository Skeleton

**Feature Branch**: `001-repository-skeleton`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Repository skeleton (roadmap Phase 0, feature #1). Create the empty,
buildable monorepo structure for Hello POS so later features have a place to live: backend (API,
a shared library for primitives that the web app can also use, test projects), web back office,
Android app (application ID com.recbook.hellopos, package layout per README), and contracts
(API contract and an empty shared pricing test-case file with its schema). Toolchain versions are
pinned. Each app builds and its test suite runs from the command line. A minimal health check
exists in the contract and the API, and both the web app and the Android app show its result on a
start screen, proving the three apps are wired to the same contract. Out of scope: local
container environment (#2), tenancy infrastructure (#3), CI and staging deploy (#4), any business
features."

## User Scenarios & Testing *(mandatory)*

The users of this feature are the developers building Hello POS. Its value is that every later
feature starts from a working, consistent base instead of each one inventing its own structure.

### User Story 1 - Build and test everything from a fresh clone (Priority: P1)

A developer clones the repository, installs the documented prerequisites, and builds and tests the
backend, the web back office, and the Android app using only the commands in the README.

**Why this priority**: nothing else can be built until there is a place for code to live and a
known way to build and test it. This is the minimum useful outcome of the feature.

**Independent Test**: on a machine with only the documented prerequisites, follow the README's
build section and confirm each application builds and its test suite passes.

**Acceptance Scenarios**:

1. **Given** a fresh clone and the documented prerequisites, **When** the developer runs the
   documented build command for each application, **Then** each one builds without errors.
2. **Given** a successful build, **When** the developer runs the documented test command for each
   application, **Then** each test suite runs and reports zero failures.
3. **Given** a machine with a different, incompatible toolchain version installed, **When** the
   developer builds, **Then** the build stops with a message naming the required version.

---

### User Story 2 - See that all three apps share one contract (Priority: P2)

A developer starts the API, then opens the web back office and the Android app. Each shows a start
screen reporting the API's health (status and version), obtained through the operation defined in
the shared API contract.

**Why this priority**: proves the contract-first wiring (Constitution, Principle V) end to end
before any business feature depends on it.

**Independent Test**: start the API, open each client, and confirm both show the same status and
version; stop the API and confirm both report it as unavailable.

**Acceptance Scenarios**:

1. **Given** the API is running, **When** the developer opens the web back office, **Then** the
   start screen shows the API status as available, with its version.
2. **Given** the API is running, **When** the developer opens the Android app, **Then** the start
   screen shows the same status and version.
3. **Given** the API is not running, **When** either client opens, **Then** its start screen
   shows a clear "API unavailable" message and the app does not crash.
4. **Given** the API's health response is changed without updating the contract, **When** the
   backend tests run, **Then** a test fails, reporting the mismatch.

---

### User Story 3 - Shared building blocks are in place (Priority: P3)

A developer finds the shared pieces later features rely on: a shared library that both the API and
the web back office use, and the shared pricing test-case file that both the backend and Android
test suites already read.

**Why this priority**: the pricing engine (roadmap #10) and every money feature depend on these
being shared from day one, but nothing in this feature needs their contents yet.

**Independent Test**: confirm the API and the web back office both reference the shared library,
and that both the backend and Android test suites load the pricing test-case file and validate it
against its schema.

**Acceptance Scenarios**:

1. **Given** the repository, **When** the developer inspects the backend and web projects,
   **Then** both reference the same shared library, and that library references neither of them.
2. **Given** the pricing test-case file with an empty list of cases, **When** the backend and
   Android test suites run, **Then** each loads the file, validates it against its schema, and
   passes.
3. **Given** a pricing test-case file that does not match its schema, **When** either test suite
   runs, **Then** it fails, naming the problem.

### Edge Cases

- The API is unreachable, slow, or returns an error: both clients show "API unavailable" (or an
  equivalent message) within a bounded time and stay usable.
- The API address differs between environments: clients take it from configuration, so pointing
  them at another API needs no code change.
- The Android emulator and a physical device reach the developer's machine at different
  addresses: the documented configuration covers both.
- The pricing test-case file is missing: both test suites fail with a message naming the file.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain four top-level areas: the backend, the web back office,
  the Android app, and the shared contracts.
- **FR-002**: Each application MUST build from the command line with a single documented command.
- **FR-003**: Each application MUST have a test suite that runs from the command line with a single
  documented command and contains at least one passing test.
- **FR-004**: Toolchain versions (backend and web SDK, Android build tooling) MUST be pinned in the
  repository so every developer and later CI builds with the same versions.
- **FR-005**: The shared API contract MUST define a health operation that returns the service
  name, version, status, and current server time in UTC.
- **FR-006**: The API MUST implement the health operation, and a backend test MUST verify that its
  response conforms to the contract.
- **FR-007**: The health operation MUST NOT require authentication and MUST NOT expose secrets,
  configuration values, or internal details beyond the fields in FR-005.
- **FR-008**: The web back office MUST show a start screen with the health result (status and
  version), or a clear "API unavailable" message when the call fails or times out.
- **FR-009**: The Android app MUST show a start screen with the same information and the same
  failure behavior as FR-008.
- **FR-010**: The Android app MUST use the application ID `com.recbook.hellopos` and the package
  layout documented in the README (core, data, domain, feature), with the domain area free of
  Android framework dependencies.
- **FR-011**: The API address used by the web back office and the Android app MUST come from
  configuration per environment, not from code.
- **FR-012**: A shared library MUST exist that the API and the web back office both reference. It
  MUST NOT depend on either of them or on any web or UI framework. It contains no business logic
  in this feature.
- **FR-013**: The contracts area MUST contain the shared pricing test-case file with an empty list
  of cases, plus a schema describing a pricing case.
- **FR-014**: Both the backend and Android test suites MUST load the pricing test-case file and
  validate it against its schema.
- **FR-015**: The README's "Building" section MUST list the prerequisites and the build, test, and
  run commands for each application.

### Key Entities

- **Health status**: the API's self-report: service name, version, status (available or
  degraded), server time in UTC.
- **Pricing test case**: one language-neutral example of a priced sale: its inputs (business
  settings, lines, discounts, tip, payment method) and expected outputs (taxes, totals, rounding,
  change). Only the schema is defined in this feature; cases are added from roadmap #10 onward.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer with the documented prerequisites builds and tests all three
  applications from a fresh clone in under 30 minutes, using only the README.
- **SC-002**: All three test suites complete with zero failures.
- **SC-003**: With the API running, both clients show its status within 5 seconds of opening; with
  the API stopped, both show the unavailable message within 10 seconds and remain usable.
- **SC-004**: Every change to the health response that is not reflected in the contract is caught
  by a failing test (no silent drift).
- **SC-005**: A malformed pricing test-case file is rejected by both test suites.

## Assumptions

- The technology choices are those fixed in the constitution (Technology & Platform Constraints);
  this feature only creates the structure for them.
- "Version" in the health result is the application's build version; how versions are assigned is
  decided in CI work (roadmap #4). Until then a fixed development version is acceptable.
- The local container environment with the database and the mock API (#2), tenancy
  infrastructure (#3), and CI and staging deployment (#4) are separate features. The health
  operation does not check the database in this feature.
- The shared library starts empty. Money and pricing types arrive with the pricing engine (#10),
  in line with the constitution's Simplicity principle.
- Android targets the minimum version fixed in the constitution (minSdk 26), tablet and landscape
  first; the start screen only needs to be readable on both phone and tablet.
