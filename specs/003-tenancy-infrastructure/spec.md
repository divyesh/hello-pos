# Feature Specification: Tenancy and API Infrastructure

**Feature Branch**: `003-tenancy-infrastructure`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Tenancy and API infrastructure (roadmap #3). The foundation that makes
cross-business data access structurally impossible before any business feature exists
(Constitution, Principle III): database schema and migration workflow (versioned migrations
applied by a dedicated migrator login; the API's application login only reads and writes rows);
a 'current business' per request that only an authenticated identity can set (login is #7; until
then no production request can act as a business, tests set it explicitly), never accepted from
request bodies, routes or query strings; automatic scoping of every query on business-owned data
to the current business, with any bypass needing an explicit, reviewed opt-out; business-owned
tables always carrying the business id, with links between them that include it so the database
rejects cross-business references; validation that every id referenced by a write belongs to the
current business; a reusable cross-business isolation test harness for every later slice;
conventions for ids (client-generatable, time-ordered), table naming, UTC timestamps and money. A
minimal businesses table is in scope as the tenant root. Out of scope: login, tokens and roles
(#7), sign-up and business screens (#5), CI (#4)."

## Background

Hello POS stores many businesses' data in one database. An audit of the old system found that
one business could take over another, read its payment credentials, and change its data, because
every query had to remember its own business check and some did not. This feature makes isolation
structural: correct by construction, enforced by the database where possible, and proven by tests,
before any business data exists.

## Clarifications

### Session 2026-10-11

- Q: Should the database enforce the business scope on every row (row-level security) in this
  feature? → A: Yes, now, as a second layer under application scoping and composite links (FR-014).
- Q: What sample data proves isolation before real business tables exist? → A: Test-only sample
  tables shaped like the old system's categories → products, present only in test databases
  (FR-017).

## User Scenarios & Testing *(mandatory)*

The direct users are the developers who build every later feature; the people protected are the
business owners whose data must never be visible to another business.

### User Story 1 - Evolve the database safely (Priority: P1)

A developer adds or changes tables by writing a versioned migration. A dedicated migrator login
applies pending migrations, in order, to the local environment, to test databases, and later to
staging and production. The API itself runs with a login that can read and write rows but cannot
change the schema.

**Why this priority**: every later feature needs tables; separating schema rights from data
rights is the base of least privilege (Principle VIII) and must exist before the first table.

**Independent Test**: from an empty database, apply all migrations with the migrator login and
confirm the schema matches; confirm the application login can read and write rows of a migrated
table but cannot create, alter or drop tables; apply again and confirm nothing changes.

**Acceptance Scenarios**:

1. **Given** an empty application database, **When** migrations are applied, **Then** every
   migration runs once, in order, and the database records which versions are applied.
2. **Given** an up-to-date database, **When** migrations are applied again, **Then** nothing
   changes and the run succeeds.
3. **Given** a migrated database, **When** the API's application login tries to create, alter or
   drop a table, **Then** it is refused; reading and writing rows of application tables succeeds.
4. **Given** the local environment is started, **When** it becomes ready, **Then** pending
   migrations have been applied without a separate manual step.
5. **Given** a migration fails part-way, **When** the run ends, **Then** that migration's changes
   are rolled back and the run reports which migration failed.

---

### User Story 2 - Every request is confined to one business (Priority: P1)

Each request that touches business-owned data acts for exactly one current business, which comes
only from the caller's authenticated identity. Every read of business-owned data is automatically
limited to that business, every write is stamped with it, and any id a write refers to must belong
to it. A request without an identity cannot reach business-owned data at all.

**Why this priority**: this is the core of Principle III; every later feature relies on it.

**Independent Test**: with two businesses holding data, act as business A and confirm that reads
return only A's rows, writes are stamped with A, writes referring to B's records are rejected,
and supplying B's id in a request body, route or query string has no effect; with no identity,
confirm business-owned data is unreachable.

**Acceptance Scenarios**:

1. **Given** businesses A and B both have records, **When** a request acting as A lists records,
   **Then** only A's records are returned.
2. **Given** a request acting as A, **When** it asks for one of B's records by id, **Then** the
   result is "not found", indistinguishable from an id that does not exist.
3. **Given** a request acting as A, **When** it creates a record, **Then** the record belongs to
   A regardless of any business id present in the request.
4. **Given** a request acting as A, **When** it creates or updates a record that refers to one of
   B's records, **Then** the write is rejected and nothing is stored.
5. **Given** a request with no authenticated identity, **When** any operation on business-owned
   data runs, **Then** it fails as unauthorized and no data is read or written.
6. **Given** code that must legitimately read across businesses (for example a future platform
   report), **When** it does so, **Then** it uses an explicit, named opt-out that is easy to find
   in review; there is no implicit way to skip the business scope.

---

### User Story 3 - Prove isolation for every slice (Priority: P2)

A developer building any feature that touches business-owned data adds an isolation test using a
shared harness. The harness creates two businesses with data and checks that, through that
feature, business A can neither read nor change business B's data.

**Why this priority**: the constitution requires an isolation test for every slice; a shared
harness makes that cheap and uniform. It depends on User Stories 1 and 2.

**Independent Test**: write an isolation test for a sample slice with the harness; confirm it
passes; deliberately remove the slice's scoping and confirm the test fails, naming the leak.

**Acceptance Scenarios**:

1. **Given** the harness, **When** a developer writes an isolation test for a slice, **Then** the
   test needs only the slice's operations and sample data, not its own setup of businesses.
2. **Given** a slice that leaks data across businesses, **When** its isolation test runs, **Then**
   it fails, naming the operation and the foreign record that leaked.
3. **Given** a business-owned table, **When** a row would refer to a record of another business,
   **Then** the database itself rejects it, even if application checks were bypassed.

---

4. **Given** a database session acting as business A, **When** any query on a business-owned
   table runs, even one written without the application's scoping, **Then** only A's rows are
   visible and only A's rows can be written.
5. **Given** a database session with no current business, **When** it queries business-owned
   tables, **Then** it sees and changes no rows.

---

### User Story 4 - Conventions are followed automatically (Priority: P3)

Every table follows the same conventions: ids that a device can generate offline and that sort by
creation time, consistent naming, timestamps stored in UTC, money stored as exact decimals, and a
business id on every business-owned table. An automated check fails the build when a new table
breaks a convention.

**Why this priority**: conventions keep later features consistent with the constitution (Money
Is Exact, UTC times, tenant isolation) without relying on memory; they are cheapest to enforce
from the first table.

**Independent Test**: run the convention check against the migrated schema (passes); add a
sample table that breaks each rule in turn and confirm the check fails naming the table and rule.

**Acceptance Scenarios**:

1. **Given** the migrated schema, **When** the convention check runs, **Then** it passes.
2. **Given** a business-owned table without a business id, or a link between business-owned
   tables that does not include the business id, **When** the check runs, **Then** it fails
   naming the table.
3. **Given** a timestamp column without time zone, or a money column stored as floating point,
   **When** the check runs, **Then** it fails naming the column.

### Edge Cases

- A business is referred to that does not exist: business-owned writes for it are rejected.
- Two requests for different businesses run at the same time: each sees only its own business;
  the current business never leaks between concurrent requests.
- A background job (future outbox processing, reports) runs outside a request: it has no current
  business unless it is started for one explicitly; otherwise business-owned data is unreachable.
- Migration history disagrees with the code (the database records a migration the code does not
  contain): the run stops and reports the mismatch instead of guessing. Applied migrations are never
  edited; changes go in a new migration (documented rule, enforced in review).
- The migrator login's password is missing from the environment: migration fails clearly; the
  API still starts and reports `degraded` if it cannot use the database (feature 002).

## Requirements *(mandatory)*

### Functional Requirements

**Schema and migrations**

- **FR-001**: Schema changes MUST be delivered only as versioned, ordered migrations kept in the
  repository; the database MUST record which versions are applied.
- **FR-002**: Migrations MUST be applied by a dedicated migrator login that owns the schema. The
  API's application login MUST be able to read and write rows of application tables and MUST NOT
  be able to create, alter or drop tables or other schema objects.
- **FR-003**: Applying migrations MUST be repeatable (already-applied versions are skipped) and each
  migration MUST either complete fully or leave no partial changes.
- **FR-004**: Starting the local environment MUST apply pending migrations automatically; test
  databases MUST be migrated the same way. The API MUST NOT apply migrations itself.
- **FR-005**: The schema MUST contain a `businesses` table as the tenant root with only the fields
  needed now: id, name, country, currency (ISO 4217), pricing mode (tax-inclusive or
  tax-exclusive), financial-year start month, time zone, created and updated times (UTC).

**Current business**

- **FR-006**: Every request MUST have at most one current business, derived only from the caller's
  authenticated identity. Request bodies, routes, query strings and headers MUST NOT be able to set
  or change it.
- **FR-007**: Until login exists (roadmap #7), no production request MUST be able to act as a
  business; tests MUST be able to set the current business explicitly through a test-only
  mechanism that does not exist in production builds or configuration.
- **FR-008**: Operations on business-owned data without a current business MUST fail as
  unauthorized without reading or writing data. Work outside requests MUST start with no current
  business unless explicitly given one.

**Scoping and integrity**

- **FR-009**: Every read of business-owned data MUST be limited automatically to the current
  business. Skipping this MUST require an explicit, named opt-out whose every use is visible in
  review.
- **FR-010**: Every new business-owned record MUST be stamped with the current business; a
  business id supplied by the caller MUST be ignored or rejected.
- **FR-011**: Every business-owned table MUST carry the business id, and every link between
  business-owned tables MUST include the business id, so the database rejects a reference to
  another business's record.
- **FR-012**: Every id referenced by a write MUST be checked to belong to the current business;
  a foreign or unknown id MUST make the whole write fail with nothing stored, reported as "not
  found" without revealing that the record exists elsewhere.
- **FR-013**: Reading another business's record by id MUST behave exactly like reading an id that
  does not exist.
- **FR-014**: As a second layer, the database itself MUST enforce the business scope on every row
  read and written in business-owned tables, using the current business that the API sets for each
  database session. A session without a current business MUST see and change no business-owned
  rows. The current business MUST be cleared before a connection is reused for another request.
  Only the migrator and explicitly authorised platform work (FR-009 opt-out) may bypass it.

**Isolation harness**

- **FR-015**: A shared test harness MUST create two businesses with sample data and let a slice's
  isolation test check, with only that slice's operations, that business A cannot read, list,
  update, delete or reference business B's data.
- **FR-016**: A harness test MUST fail, naming the operation and the leaked record, when a slice
  leaks data across businesses.
- **FR-017**: This feature MUST demonstrate the harness and every rule above (FR-009 to FR-014)
  on two linked, business-owned sample tables shaped like the old system's categories and
  products (a parent and a child that refers to it). The sample tables MUST exist only in test
  databases, never in the local environment, staging or production; the real catalog tables are
  designed in roadmap #11.

**Conventions**

- **FR-018**: Ids MUST be generatable by a device offline, unique without coordination, and sort by
  creation time.
- **FR-019**: Table and column names MUST follow one documented naming convention; timestamps MUST
  be stored with time zone in UTC; money MUST be stored as exact decimals with explicit precision
  and scale (Constitution I).
- **FR-020**: An automated check MUST fail the build when any table breaks FR-011, FR-018 or FR-019,
  naming the table or column and the rule.
- **FR-021**: The conventions, the migration workflow and how to write an isolation test MUST be
  documented for feature developers.

### Key Entities

- **Business**: the tenant root. Every business-owned record belongs to exactly one business.
  Holds the settings later features need for pricing and reporting (country, currency, pricing
  mode, financial-year start, time zone).
- **Current business**: the business a request or job acts for; derived from the authenticated
  identity, never from request data; absent by default.
- **Migration**: a versioned, ordered schema change with a record of when it was applied.
- **Isolation test**: a per-slice test, built on the shared harness, proving business A cannot
  reach business B's data through that slice.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of business-owned tables carry the business id and 100% of links between them
  include it, verified automatically on every build.
- **SC-002**: In the isolation harness, 0 records of business B are returned, changed or
  referenceable while acting as business A, across reads, lists, updates, deletes and references.
- **SC-003**: Removing the business scope from any sample operation makes at least one isolation
  test fail (the harness detects 100% of the leaks it is given).
- **SC-004**: The application login is refused 100% of attempts to change the schema.
- **SC-005**: An empty database is migrated to the latest version in under 1 minute, and a second
  run makes no changes.
- **SC-006**: Writing a new slice's isolation test takes no more than one short test file using the
  harness (no per-slice business setup code).
- **SC-007**: With the application's scoping deliberately removed, the database alone still returns
  0 rows of another business and rejects 100% of writes to another business's rows.
- **SC-008**: A connection reused after a request for business A never acts as A for the next
  request (0 occurrences across concurrent test runs).

## Assumptions

- Technology choices come from the constitution (PostgreSQL, EF Core, .NET 10, vertical slices);
  feature 002's local environment and Testcontainers setup are reused.
- The local environment gains a migrator login alongside the existing admin and application logins;
  the application login receives row read/write rights on application tables through migrations.
- Login, tokens and roles arrive with roadmap #7, which will fill the current business from the
  token. Owner accounts that own several businesses are modelled then; the `businesses` table here
  has no owner link yet.
- No business-facing endpoints are added; `/health` is unchanged except that it keeps reporting
  database reachability.
- Production and staging migration runs are wired in CI and deployment (#4); this feature provides
  the migration workflow they will call.
