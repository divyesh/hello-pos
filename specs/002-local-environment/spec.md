# Feature Specification: Local Environment

**Feature Branch**: `002-local-environment`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Local environment (roadmap Phase 0, feature #2). One command starts
everything a developer needs locally with Podman (Docker also works): PostgreSQL (the database
later features use; created with a dedicated least-privilege application login, data persisted in
a named volume), a mock API served from the API contract so web and Android can be built without
the real backend, Seq receiving the API's logs, and optionally the real API itself built from a
container image (the same image later deployed in #4). The API reports its health as 'degraded'
when it cannot reach the database, using the existing contract's status field. One command stops
everything; another resets data. Documented in the README; secrets for local use come from an
untracked local settings file with a committed example. Out of scope: database schema, migrations
(#3), seed data for business entities (added by the features that create them), CI and staging
(#4)."

## User Scenarios & Testing *(mandatory)*

The users of this feature are the developers building Hello POS. Its value is that the services
every later feature needs (database, log viewer, a stand-in API) are available with one command,
the same way on every machine.

### User Story 1 - Start and stop the shared services with one command (Priority: P1)

A developer runs one command and gets the database and the log viewer running locally. Another
command stops them; data survives a stop and start. A separate command wipes local data to start
fresh.

**Why this priority**: from roadmap #3 onward every backend feature needs the database, and the
log viewer is where developers read the API's logs. Nothing else in this feature is useful without
these running.

**Independent Test**: from a fresh clone with only the documented prerequisites, run the start
command, confirm the database accepts the application login and the log viewer opens; stop and
start again and confirm data written before is still there; run reset and confirm it is gone.

**Acceptance Scenarios**:

1. **Given** a fresh clone and the documented prerequisites, **When** the developer runs the start
   command, **Then** the database and the log viewer are running and the command reports each one
   as ready, with its address.
2. **Given** the services are running, **When** the developer connects with the application login,
   **Then** the connection succeeds on the application database, and the login cannot create
   databases, create logins, or access other databases.
3. **Given** data was written to the database, **When** the developer stops and starts the
   environment, **Then** the data is still there.
4. **Given** the services are running or stopped, **When** the developer runs the reset command,
   **Then** all local data of this environment is deleted and the next start begins from an empty
   database, after the command has asked for confirmation (skippable with an explicit flag).
5. **Given** the local settings file is missing, **When** the developer runs the start command,
   **Then** it creates the file from the committed example, says so, and continues.

---

### User Story 2 - Build clients against a stand-in API (Priority: P2)

A web or Android developer starts the environment and points the app at the mock API, which
answers every operation in the API contract with the contract's own examples, so client work can
continue before the real backend implements an operation.

**Why this priority**: the constitution requires a mock API so client work is not blocked by the
backend (Principle V). It matters from roadmap #5 onward, when clients and backend are built in
parallel.

**Independent Test**: start the environment, call each contract operation on the mock API, and
confirm the responses match the contract examples; point the web app and the Android app at the
mock API through their existing configuration and confirm their start screens show "available".

**Acceptance Scenarios**:

1. **Given** the environment is running, **When** a client calls `GET /health` on the mock API,
   **Then** it receives the example response from the contract.
2. **Given** a developer changes the contract, **When** the mock API is restarted (or reloads),
   **Then** it serves the changed contract.
3. **Given** the web app or the Android app is configured with the mock API's address, **When**
   it starts, **Then** its start screen shows the API as available, with no code changes.
4. **Given** the contract is invalid, **When** the mock API starts, **Then** it fails with a
   message naming the problem rather than serving partial responses.

---

### User Story 3 - Run the real API in the environment (Priority: P3)

A developer starts the environment with the real API included. The API runs from the same
container image that later environments deploy, connects to the local database, and sends its
logs to the log viewer automatically. Its health check reports whether the database is reachable.

**Why this priority**: proves the deployable image and its configuration before CI and staging
(#4), and gives the health check a real dependency. Not needed for client work (User Story 2) or
for running the API from the SDK container as in feature 001.

**Independent Test**: start the environment with the API included; call the health check
(available); stop only the database (degraded within seconds); start it again (available);
open the log viewer and find the API's request events.

**Acceptance Scenarios**:

1. **Given** the environment is started with the API included, **When** a client calls the health
   check, **Then** it returns `available` with the API's version.
2. **Given** the API is running, **When** the database becomes unreachable, **Then** the health
   check still answers successfully but reports `degraded`, without exposing database details,
   and returns to `available` once the database is back.
3. **Given** the API is running, **When** it handles requests, **Then** its request events appear
   in the log viewer without any manual configuration.
4. **Given** the API image, **When** it is inspected, **Then** it contains no secrets, runs as a
   non-administrator user, and takes all settings (database address and login, log viewer
   address) from its environment.

### Edge Cases

- A port the environment needs is already in use: the start command fails naming the port, and
  ports can be changed in the local settings file.
- The database is not ready yet when the API starts: the API starts anyway and reports `degraded`
  until the database is reachable; it does not crash or loop.
- The health check's database probe is slow: it gives up after a bounded time and reports
  `degraded`, so the health check itself stays fast.
- The developer uses Docker instead of Podman: the same commands work.
- Reset is run while services are running: they are stopped first, then data is deleted.
- The log viewer is not running while the API is: the API keeps serving and logging to its
  console (as in feature 001).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: One documented command MUST start the database, the mock API and the log viewer;
  the same command with a documented option MUST also start the real API.
- **FR-002**: The start command MUST report when each service is ready (with its address), or
  name the service that failed to start.
- **FR-003**: One documented command MUST stop all services of the environment, keeping data.
- **FR-004**: One documented command MUST delete all local data of the environment (database and
  log viewer data) after confirmation, which an explicit flag can skip; it MUST NOT touch anything
  outside this environment.
- **FR-005**: The commands MUST work unchanged with Podman (default) and Docker.
- **FR-006**: On first start the database MUST be created with two logins: an administrator login
  used only for setup, and an application login limited to the application database (no rights
  to create databases or logins, no access to other databases).
- **FR-007**: Database data MUST persist across stop and start until reset.
- **FR-008**: Passwords, ports and other local settings MUST come from an untracked local settings
  file. A committed example file MUST list every setting with safe development placeholders. The
  start command MUST create the local file from the example when it is missing.
- **FR-009**: The mock API MUST serve every operation in the API contract using the contract's
  examples, from the contract file in the repository, at a documented address.
- **FR-010**: The web app and the Android app MUST be able to use the mock API through their
  existing API address configuration, with no code changes; the README MUST show how.
- **FR-011**: The real API MUST run from a container image built from the repository. The image
  MUST contain no secrets, MUST run as a non-administrator user, and MUST read all settings from
  its environment.
- **FR-012**: When the real API runs in the environment, its logs MUST reach the log viewer
  without manual configuration.
- **FR-013**: The health check MUST report `available` when the database is reachable and
  `degraded` when it is not, staying a successful, contract-conformant response in both cases.
  The database probe MUST give up within 2 seconds.
- **FR-014**: The health response MUST still expose only the fields in the contract; no database
  name, address, or error detail.
- **FR-015**: Every port the environment uses MUST be changeable in the local settings file.
- **FR-016**: The README MUST document the prerequisites, the start, stop and reset commands, every
  service address, where local settings live, and how to point the clients at the mock API.

### Key Entities

- **Local settings**: per-developer values for this environment: database administrator and
  application logins, application database name, ports, log viewer address. Never committed.
- **Environment service**: one of database, mock API, log viewer, real API; each has an address,
  a readiness signal, and (for the database and log viewer) persistent local data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone, a developer has all services running in under 10 minutes on the
  first start (including downloads) and under 1 minute on later starts.
- **SC-002**: Data written to the database survives 100% of stop-and-start cycles and is gone after
  every reset.
- **SC-003**: After the database stops, the health check reports `degraded` within 10 seconds; after
  it starts again, `available` within 10 seconds. The health check answers within 3 seconds in both
  states.
- **SC-004**: The repository contains zero real secrets: every committed setting is a documented
  development placeholder.
- **SC-005**: The same start, stop and reset commands succeed on both Podman and Docker.

## Assumptions

- Technology choices come from the constitution (PostgreSQL, Podman with the docker-compose
  provider, Seq for logs per feature 001's research R14). This feature adds no application data.
- The web back office keeps running through the SDK container wrapper from feature 001; it is not
  part of this environment. It is a static site in production and needs no container locally.
- The log viewer runs without authentication locally; it is a development-only tool, not exposed
  beyond the developer's machine. Production logging is decided at launch (roadmap #26).
- The application database starts empty. Tables, migrations and the database part of the tenancy
  infrastructure arrive with roadmap #3; seed data for business entities arrives with the features
  that create those entities.
- The API image built here is the basis for staging deployment in roadmap #4; this feature does not
  publish it anywhere.
- The Android emulator reaches the developer's machine at `10.0.2.2`, as in feature 001.
