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

Login arrives with roadmap #7. Until then nothing in production has a current business, so
business endpoints answer `401`.

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
| `hellopos_migrator` | `HelloPos.Api migrate` | owns schema `pos`; creates and alters tables |
| `pos_app` (group, no login) | — | row read/write on `pos` tables (granted by default privileges) |
| `hellopos_app` | the API | member of `pos_app`; no schema rights; subject to row-level security |
