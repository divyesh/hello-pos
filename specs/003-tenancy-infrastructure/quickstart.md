# Quickstart: Tenancy and API Infrastructure

## 1. Automated proof (most of the feature)

```text
scripts/dotnet.ps1 test --solution backend/HelloPos.slnx
```

Expected to pass, against real PostgreSQL started by Testcontainers:

- migrations apply from empty, a second run changes nothing, a failing migration leaves nothing;
- the app login can read/write rows but cannot create/alter/drop tables (SC-004);
- isolation harness on the sample tables: no reads, lists, updates, deletes or references across
  businesses (SC-002); negative tests prove the harness catches leaks (SC-003);
- with EF scoping bypassed, RLS alone returns 0 foreign rows and rejects foreign writes (SC-007);
- concurrent requests for two businesses over a small pool never mix (SC-008);
- the convention check passes, and each rule fires on deliberately broken tables (SC-001);
- no `IgnoreQueryFilters` outside `AcrossAllBusinesses()`; no pending model changes.

## 2. Local environment

1. `scripts/env.ps1 up` (an existing feature 002 database is upgraded; no reset needed).
   Expected: after the database is healthy, `migrate` runs, applied migrations are listed, then the
   addresses print.
2. As the app login:
   `podman exec hellopos-postgres-1 psql -U hellopos_app -d hellopos -c "select count(*) from pos.businesses"`
   returns `0` (no current business set: RLS hides everything).
3. `... -c "create table pos.x (id int)"` is refused.
4. `scripts/env.ps1 up` again: `migrate` reports nothing to apply.
5. `scripts/env.ps1 up --api`: `/health` is `available`.

## 3. Documentation

`docs/tenancy.md` explains adding a business-owned table, the migration command, endpoint rules and
the isolation test, matching [contracts/tenancy-developer-guide.md](contracts/tenancy-developer-guide.md).
