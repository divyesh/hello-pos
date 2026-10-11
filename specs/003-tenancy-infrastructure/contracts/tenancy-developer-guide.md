# Tenancy Developer Guide (contract for every later feature)

No API contract changes in this feature. This is the contract feature slices build on; the
implementation copies it to `docs/tenancy.md` (FR-021).

## Adding a business-owned table

1. Entity implements `IBusinessOwned` (`Guid BusinessId`) and has `Guid Id` (UUIDv7).
2. Its `IEntityTypeConfiguration<T>` calls `builder.ConfigureBusinessOwned()` (key, business link,
   index) and declares links to other business-owned entities with
   `HasForeignKey(x => new { x.BusinessId, x.OtherId }).HasPrincipalKey(o => new { o.BusinessId, o.Id })`.
3. Money properties use `.HasPrecision(19, 4)`; times are `DateTimeOffset` (UTC).
4. Add a migration (`scripts/dotnet.ps1 ef migrations add <Name> ...`, command in the docs) and add
   `migrationBuilder.EnableBusinessRowSecurity("<table>")` for each new table.
5. Run the backend tests: the convention check fails if a rule is missed.

## Writing an endpoint that touches business data

- Add `.RequireCurrentBusiness()` to the endpoint.
- Never accept a business id from the request; never set `BusinessId` yourself (it is stamped).
- Reading another business's record looks like "not found"; writes referring to one fail with
  `404`. Do not catch or reinterpret those errors.
- Never call `IgnoreQueryFilters`. Cross-business reads use `AcrossAllBusinesses()` and need
  explicit review approval (and, at the database level, a platform role that does not exist yet).

## Writing the slice's isolation test

```csharp
public sealed class ListProductsIsolationTests(IsolationScenario scenario) : IsolationTests(scenario)
{
    // Supply the slice's operations; the harness seeds businesses A and B and asserts.
    [Fact] public Task List_DoesNotShowOtherBusiness() => AssertCannotList(c => c.GetAsync("/products"));
    [Fact] public Task Get_OtherBusinessRecord_IsNotFound() => AssertCannotRead(id => $"/products/{id}");
}
```

(Shape is indicative; the exact API is fixed during implementation and documented in `docs/tenancy.md`.)

## Status codes

| Situation | Response |
|-----------|----------|
| No current business on an endpoint that needs one | `401` problem details |
| Record of another business, or unknown id | `404` problem details (identical) |
| Write referring to another business's record | `404` problem details, nothing stored |
