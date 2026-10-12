# Tenancy: how to build on business-owned data

Hello POS keeps every business's data in one database. Isolation is enforced in five layers, so a
single mistake cannot leak data (Constitution, Principle III):

| Layer | What it does | Where |
|-------|--------------|-------|
| Current business | Comes only from the authenticated identity (`business_id` claim), never from request data | `ICurrentBusiness` |
| Query scope | EF Core named filter `BusinessScope` on every business-owned entity | `HelloPosDbContext` |
| Stamping | New records get the current business; records never change business | `BusinessStampingInterceptor` |
| Composite keys | Links include `business_id`, so the database rejects cross-business references | `ConfigureBusinessOwned()` |
| Row-level security | PostgreSQL itself shows and changes only the session's business, even for raw SQL | `pos.current_business_id()`, `BusinessSessionInterceptor` |

Owners (feature 005) sign in to the web back office and open one of their businesses: the API checks
ownership, then puts `business_id` in the session cookie, so every layer above works unchanged.
POS devices and the people signing in on them (feature 007) carry their business inside their own
secret instead; see "POS devices and sessions" below.

## The account dimension: staff and roles

A back-office account (schema `identity`, not business-owned; the C# type is still `OwnerAccount`) can
work at several businesses. Who works where, and in which role, is the business-owned table
`pos.staff_members` (feature 006; it replaced 005's `business_owners`): one row per person per
business, role `owner`, `manager` or `cashier`, an `active` flag, an optional link to an account
(`account_id`, back-office access) and an optional POS username and hashed PIN (POS access, #7).
Before a business is opened, an account must still see its own businesses, so the session also
carries the signed-in account:

| Piece | What it does |
|-------|--------------|
| `ICurrentOwner` | the `owner_id` claim of the session (the signed-in account); never from request data |
| `pos.owner_id`, `pos.current_owner_id()` | set by `BusinessSessionInterceptor` next to `pos.business_id` |
| Policy `owner_read` on `staff_members` | **read-only**: an account sees its own staff rows |
| Policy `owner_read` on `businesses` | **read-only**: an account sees businesses where it is an *active owner or manager* (a cashier row grants nothing) |
| `ICurrentStaff` + `RequireRole(...)` | the caller's active staff row in the open business, **read from the database on every request**; `403` for the wrong role |

Account access never writes. Every change runs in a session acting as the business:

- **Creating a business**: the server generates the id and saves the business, the creator's owner
  staff row and its audit entry in one transaction through `FixedCurrentBusiness(id)`.
- **Opening a business** (`POST /owner/businesses/{id}/open`) needs an active owner or manager row of
  the account (`404` otherwise). Once `business_id` is in the session, row-level security trusts it, so
  this check and its tests (`OwnerIsolationTests`, `OpenAndEditTests`, `BusinessRoleTests`) matter most.
- **Roles** come from `ICurrentStaff` on each request, never from a claim, so a role change or a
  deactivation applies on the very next request. Rules (who may do what to whom) live in the kernel
  (`StaffRules`), shared by the API and the web.

Querying across businesses for one account (`Memberships.BackOfficeMemberships`) uses
`AcrossAllBusinesses()` with an explicit account filter; `owner_read` keeps it to that account even if
the filter were wrong.

**Invitations** (`pos.staff_invitations`): the person accepting has no business open, and the business
must not come from the request. The emailed token's SHA-256 is looked up by `pos.invitation_business()`,
a `SECURITY DEFINER` function owned by the migrator that returns only the business id of a pending,
unexpired invitation; the API then acts as that business. Treat any new definer function the same way:
narrow input, minimal output, pinned `search_path`, `EXECUTE` only for `pos_app`.

**Last owner**: changes that can remove an active owner lock the business row (`LastOwnerGuard`) before
counting owners, so two owners deactivating each other at once cannot leave a business without one.

**Audit**: `pos.staff_changes` is append-only for the app (`UPDATE`, `DELETE` revoked) and never holds a
PIN or token.

**Stale tabs**: business pages send `X-HelloPos-Business` with the business they were opened for.
`RequireSameBusiness()` answers `409 business-changed` when the session has since opened another
business. The header can only refuse a request; the business still comes from the session.

## POS devices and sessions

The Android POS has no cookie session. It authenticates with opaque secrets of the form
`dev_|pat_|prt_<business id>.<record id>.<random>`, stored only as SHA-256 hashes:

| Secret | Header | Lives |
|--------|--------|-------|
| Device credential (`dev_`) | `Authorization: Device …` | until the device is revoked |
| Access token (`pat_`) | `Authorization: Bearer …` | 15 minutes |
| Refresh token (`prt_`) | body of `POST /pos/refresh` | rotated on every use; a reused one ends the session |

The handlers (`DeviceCredentialHandler`, `PosSessionHandler`) read the business id from the secret,
open a context acting as that business (`PosAuthentication.ActingAs`), and accept only if the record
exists **in that business** and the hash matches, so a secret edited to name another business finds no
row under row-level security and gets `401`. Only then is `business_id` put in the claims, and every
layer above works unchanged. A session is also checked on each request against the events that end it
(device revoked, person deactivated, PIN changed after the session started, 12 hours) in
`SessionChecks`.

**Pairing** (`POST /pos/pair`, anonymous): like invitations, the business must not come from the
request. The code's SHA-256 is looked up by `pos.pairing_business()`, a `SECURITY DEFINER` function that
returns only the business id of an unused, unexpired code; the API then acts as that business and claims
the code with a conditional update so it works once.

**Offline access** (`GET /pos/offline-access`): the active staff of the business with their PIN hashes,
encrypted to the device's RSA public key (stored at pairing). It never holds a plain PIN, and only that
device's Keystore key can open it. The accepted residual risk (a rooted, stolen device can attack the
hashes offline) is in `specs/007-login-sessions/research.md` R4; revoking the device is the remedy.

**Audit**: device registration, renames and revocations, sign-ins (online and offline), failures,
lockouts, sign-outs and own PIN changes go to `pos.staff_changes` with the device id. Never a PIN,
code, credential or token (`PosSecretsNeverLeakTests`).

## Catalog (feature #11)

The catalog is the first large set of business-owned tables, and a good example of the rules above:

- Every link carries the business id (`products (business_id, category_id) -> categories (business_id, id)`,
  tax profile versions, components, barcodes, discounts), so a product cannot point at another
  business's category even if a check were forgotten. Slices still check every referenced id against
  the open business and answer "unknown" for other businesses' ids (row-level security hides them).
- Uniqueness is per business: names and SKUs through a normalized lower-case column, barcodes on
  `(business_id, code)`. The same barcode may exist in two businesses.
- **Overlap rules belong in the database too.** Two scheduled discounts of one product may never cover
  the same day: the slice checks under a row lock on the product, and an exclusion constraint
  (`EXCLUDE USING gist (business_id WITH =, product_id WITH =, daterange(from_date, to_date, '[]') WITH &&)`,
  extension `btree_gist`) guarantees it when two saves race.
- **Imports never name a business.** A CSV row's category, tax profile, SKU and barcodes are looked up
  in the open business only; a name from another business is simply unknown.
- **Selecting the open business row.** On the web an owner can also *read* their other businesses
  (the owner-read policy), so `Businesses.Single()` is not "the open one": select it by
  `db.CurrentBusinessId` (`CatalogSupport.BusinessAsync`).
- `pos.catalog_changes` is append-only for the app login, like `staff_changes`.

## Catalog on tills (feature #13)

Tills download their business's catalog with `GET /pos/catalog` and keep a copy for offline selling.

- **The business comes from the device credential only** (`PosAuthentication.DevicePolicy`); a revoked till
  gets 401. Row-level security scopes every query, so a till paired to A never receives B's rows, in full
  or change downloads (`Tenancy/CatalogSyncIsolationTests`).
- **Change tracking lives in the database.** Triggers stamp each catalog root (`tax_profiles`,
  `categories`, `products`, `item_discounts`) with the writing transaction's id (`pg_current_xact_id()`);
  children stamp their root (a barcode its product, a tax version or component its profile), and a deleted
  discount is written to `catalog_removals`. Nothing in a slice has to remember it, so a new catalog write
  (#28 promotions, imports) is covered automatically as long as it writes these tables.
- **Reading changes.** A download is read in one `REPEATABLE READ` snapshot together with
  `pg_snapshot_xmin(pg_current_snapshot())`, the oldest transaction still running. That is the till's next
  `since`; rows with `revision >= since` are returned next time. No commit is ever skipped, whatever order
  transactions finish in; a row may be sent twice, which is harmless because tills replace whole roots.
- **New catalog tables** that tills need: add a `revision` column with the stamping trigger (or a trigger
  that stamps its root), and include the table in `CatalogSync`.
- **On the till** the copy records its business; every read erases it if the till is now paired to
  another business or not at all, and unpairing erases it explicitly. App data is excluded from backups and
  device-to-device transfer (`res/xml/data_extraction_rules.xml`).

## Add a business-owned table

1. The entity implements `IBusinessOwned` and creates its id with `Guid.CreateVersion7()`:

   ```csharp
   public sealed class Product : IBusinessOwned
   {
       public Guid BusinessId { get; set; }            // stamped on save; never set it yourself
       public Guid Id { get; init; } = Guid.CreateVersion7();
       public Guid CategoryId { get; set; }
       public required string Name { get; set; }
       public decimal UnitPrice { get; set; }
       public DateTimeOffset CreatedAt { get; set; }
   }
   ```

2. Its configuration, next to the slice, calls `ConfigureBusinessOwned()` and declares links to other
   business-owned tables with the business id on both sides:

   ```csharp
   builder.ToTable("products");
   builder.ConfigureBusinessOwned();                       // key (business_id, id), link to businesses
   builder.HasOne<Category>().WithMany()
       .HasForeignKey(p => new { p.BusinessId, p.CategoryId })
       .HasPrincipalKey(c => new { c.BusinessId, c.Id })
       .OnDelete(DeleteBehavior.Restrict);
   builder.Property(p => p.UnitPrice).HasPrecision(19, 4); // money: exact, numeric(19,4)
   builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
   ```

   Times are `DateTimeOffset` (stored as `timestamptz`, UTC). Never `double`/`float` for money.

3. Add a migration, from the repository root:

   ```text
   scripts/dotnet.ps1 ef migrations add AddProducts --project backend/src/HelloPos.Api --output-dir Infrastructure/Persistence/Migrations
   ```

   then, in the generated `Up`, after the table is created:

   ```csharp
   migrationBuilder.EnableBusinessRowSecurity("products");
   ```

   and in `Down`, before dropping it: `migrationBuilder.DisableBusinessRowSecurity("products");`.
   Never edit a migration that has been applied anywhere; add a new one.

4. Run the backend tests. The convention check fails the build if a rule is missed, and the model
   snapshot test fails if the model changed without a migration.

`scripts/env.ps1 up` applies migrations locally (as the migrator login). The API itself never
migrates and never has schema rights.

## Write an endpoint

```csharp
public sealed class ProductEndpoints : IEndpointMapper           // registered in DI
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").RequireCurrentBusiness();   // 401 without a business
        group.MapGet("/{id:guid}", async (Guid id, HelloPosDbContext db) =>
            await db.Set<Product>().SingleOrDefaultAsync(p => p.Id == id) is { } product
                ? Results.Ok(product)
                : TypedResults.Problem(statusCode: 404));
    }
}
```

Rules:

- Always `RequireCurrentBusiness()` on endpoints that touch business-owned data.
- Never accept a business id from the request, and never set `BusinessId`.
- Another business's record is indistinguishable from an unknown id: return the same `404`.
- A write that refers to another business's record fails with `404` and stores nothing (the
  composite key rejects it; `PersistenceExceptionHandler` maps it). Do not catch it.
- Never call `IgnoreQueryFilters`. `AcrossAllBusinesses()` is the only opt-out, needs explicit review
  approval, and still sees only the current business at the database level (a platform role for
  genuine cross-business reports will be added when one is needed).

| Situation | Response |
|-----------|----------|
| No current business | `401` problem details |
| Another business's record, or an unknown id | `404` problem details (identical) |
| Write referring to another business's record | `404`, nothing stored |

## Write the slice's isolation test

Every slice that touches business-owned data gets one, built on the shared harness
(`backend/tests/HelloPos.Api.Tests/Tenancy/IsolationTests.cs`). The harness creates businesses A and
B, seeds both through your API, and checks that acting as A never reaches B's records.

```csharp
public sealed class ProductIsolationTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        // create a few records through your own endpoints, return their ids
    }

    [Fact] public Task List() => AssertCannotList("GET /products", c => c.GetAsync(new Uri("/products", UriKind.Relative), Ct));
    [Fact] public Task Get() => AssertCannotRead("GET /products/{id}", id => $"/products/{id}");
    [Fact] public Task Update() => AssertCannotModify("PUT /products/{id}",
        id => new HttpRequestMessage(HttpMethod.Put, $"/products/{id}") { Content = JsonContent.Create(...) },
        id => $"/products/{id}");
    [Fact] public Task Delete() => AssertCannotModify("DELETE /products/{id}",
        id => new HttpRequestMessage(HttpMethod.Delete, $"/products/{id}"), id => $"/products/{id}");
    [Fact] public Task Reference() => AssertCannotReference("POST /sale-items",
        id => new HttpRequestMessage(HttpMethod.Post, "/sale-items") { Content = JsonContent.Create(new { productId = id }) },
        c => c.GetAsync(new Uri("/sale-items", UriKind.Relative), Ct));
}
```

A complete example is `SampleIsolationTests.cs`; `HarnessDetectsLeaksTests.cs` proves each assertion
catches a real leak. Tests always connect as the app login: a superuser bypasses row-level security
and would make the test meaningless.

## Database logins

| Login | Used by | Rights |
|-------|---------|--------|
| admin (`POSTGRES_USER` locally) | bootstrap only | creates roles |
| `hellopos_migrator` | `HelloPos.Api migrate` | owns schemas `pos` and `identity`; creates and alters tables; owns `pos.invitation_business()` (applies `identity` migrations first) |
| `pos_app` (group, no login) | — | row read/write on `pos` and `identity` tables (granted by default privileges) |
| `hellopos_app` | the API | member of `pos_app`; no schema rights; subject to row-level security |
