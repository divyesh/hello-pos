using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Tests.Tenancy.Samples;

public sealed record CategoryRequest(string Name, Guid? BusinessId = null);

public sealed record ProductRequest(string Name, Guid CategoryId, decimal UnitPrice, Guid? BusinessId = null);

/// <summary>
/// A test-only feature slice over the sample tables, written the way real slices must be written
/// (docs/tenancy.md): <c>RequireCurrentBusiness()</c>, no business id taken from the request,
/// "not found" for anything outside the current business. Request records deliberately carry a
/// <c>BusinessId</c> so tests can prove it is ignored.
/// </summary>
public sealed class SampleEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/samples").RequireCurrentBusiness();

        group.MapGet("/categories", async (SampleDbContext db) =>
            await db.SampleCategories.OrderBy(c => c.Id).ToListAsync());

        group.MapGet("/categories/{id:guid}", async (Guid id, SampleDbContext db) =>
            await db.SampleCategories.SingleOrDefaultAsync(c => c.Id == id) is { } category
                ? Results.Ok(category)
                : NotFound());

        group.MapPost("/categories", async (CategoryRequest request, SampleDbContext db) =>
        {
            var category = new SampleCategory { Name = request.Name };
            db.SampleCategories.Add(category);
            await db.SaveChangesAsync();
            return Results.Created($"/samples/categories/{category.Id}", category);
        });

        group.MapPut("/categories/{id:guid}", async (Guid id, CategoryRequest request, SampleDbContext db) =>
        {
            var category = await db.SampleCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            category.Name = request.Name;
            await db.SaveChangesAsync();
            return Results.Ok(category);
        });

        group.MapDelete("/categories/{id:guid}", async (Guid id, SampleDbContext db) =>
            await db.SampleCategories.Where(c => c.Id == id).ExecuteDeleteAsync() == 1 ? Results.NoContent() : NotFound());

        group.MapGet("/products", async (SampleDbContext db) =>
            await db.SampleProducts.OrderBy(p => p.Id).ToListAsync());

        group.MapPost("/products", async (ProductRequest request, SampleDbContext db) =>
        {
            var product = new SampleProduct { Name = request.Name, CategoryId = request.CategoryId, UnitPrice = request.UnitPrice };
            db.SampleProducts.Add(product);
            await db.SaveChangesAsync(); // a foreign category violates the composite key -> 404 (PersistenceExceptionHandler)
            return Results.Created($"/samples/products/{product.Id}", product);
        });
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
}
