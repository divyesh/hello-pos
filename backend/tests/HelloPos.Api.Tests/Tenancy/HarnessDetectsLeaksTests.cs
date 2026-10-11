using System.Net.Http.Json;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// SC-003: the harness catches leaks. These "leaky" endpoints deliberately bypass BOTH layers: they
/// use the migrator connection (table owner, so row-level security does not apply) together with
/// <see cref="QueryableExtensions.AcrossAllBusinesses{T}"/>. Every assertion must fail against them,
/// naming the operation and the leaked record.
/// </summary>
public sealed class HarnessDetectsLeaksTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        using var response = await asBusiness.PostAsJsonAsync("/samples/categories", new CategoryRequest($"{label} category"), Ct);
        response.EnsureSuccessStatusCode();
        return [(await response.Content.ReadFromJsonAsync<SampleCategory>(Ct))!.Id];
    }

    protected override void ConfigureServices(IServiceCollection services, DatabaseLogins logins) =>
        services.AddSingleton<IEndpointMapper>(new LeakyEndpoints(logins.Migrator));

    [Fact]
    public async Task LeakyList_IsCaught() => AssertNamesLeak(await Assert.ThrowsAsync<IsolationViolationException>(() =>
        AssertCannotList("GET /leaky/categories", c => c.GetAsync(new Uri("/leaky/categories", UriKind.Relative), Ct))), "GET /leaky/categories");

    [Fact]
    public async Task LeakyRead_IsCaught() => AssertNamesLeak(await Assert.ThrowsAsync<IsolationViolationException>(() =>
        AssertCannotRead("GET /leaky/categories/{id}", id => $"/leaky/categories/{id}")), "GET /leaky/categories/{id}");

    [Fact]
    public async Task LeakyUpdate_IsCaught() => AssertNamesLeak(await Assert.ThrowsAsync<IsolationViolationException>(() =>
        AssertCannotModify("PUT /leaky/categories/{id}",
            id => new HttpRequestMessage(HttpMethod.Put, $"/leaky/categories/{id}") { Content = JsonContent.Create(new CategoryRequest("taken")) },
            id => $"/samples/categories/{id}")), "PUT /leaky/categories/{id}");

    [Fact]
    public async Task LeakyReference_IsCaught() => AssertNamesLeak(await Assert.ThrowsAsync<IsolationViolationException>(() =>
        AssertCannotReference("POST /leaky/products",
            id => new HttpRequestMessage(HttpMethod.Post, "/leaky/products") { Content = JsonContent.Create(new ProductRequest("p", id, 1m)) },
            c => c.GetAsync(new Uri("/samples/products", UriKind.Relative), Ct))), "POST /leaky/products");

    private void AssertNamesLeak(IsolationViolationException error, string operation)
    {
        Assert.Contains(operation, error.Message, StringComparison.Ordinal);
        Assert.Contains(RecordsOfB[0].ToString(), error.Message, StringComparison.Ordinal);
    }

    /// <summary>Deliberately broken slice: owner connection plus the filter bypass.</summary>
    private sealed class LeakyEndpoints(string ownerConnectionString) : IEndpointMapper
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapGet("/leaky/categories", async () =>
            {
                await using var db = SampleDbContext.Create(ownerConnectionString, null);
                return await db.SampleCategories.AcrossAllBusinesses().ToListAsync();
            });
            app.MapGet("/leaky/categories/{id:guid}", async (Guid id) =>
            {
                await using var db = SampleDbContext.Create(ownerConnectionString, null);
                return await db.SampleCategories.AcrossAllBusinesses().SingleOrDefaultAsync(c => c.Id == id) is { } c
                    ? Results.Ok(c) : Results.NotFound();
            });
            app.MapPut("/leaky/categories/{id:guid}", async (Guid id, CategoryRequest request) =>
            {
                await using var db = SampleDbContext.Create(ownerConnectionString, null);
                await db.SampleCategories.AcrossAllBusinesses().Where(c => c.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, request.Name));
                return Results.Ok();
            });
            app.MapPost("/leaky/products", async (ProductRequest request) =>
            {
                await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(ownerConnectionString));
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "insert into pos.sample_products (business_id, id, category_id, name, unit_price) " +
                    "select business_id, @id, id, @name, @price from pos.sample_categories where id = @category", connection);
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("name", request.Name);
                command.Parameters.AddWithValue("price", request.UnitPrice);
                command.Parameters.AddWithValue("category", request.CategoryId);
                await command.ExecuteNonQueryAsync();
                return Results.Created();
            });
        }
    }
}
