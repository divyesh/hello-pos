using System.Net.Http.Json;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// The sample slice's isolation test: what every feature's isolation test looks like (SC-002, SC-006).
/// </summary>
public sealed class SampleIsolationTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        var ids = new List<Guid>();
        foreach (var name in new[] { $"{label} drinks", $"{label} snacks" })
        {
            using var response = await asBusiness.PostAsJsonAsync("/samples/categories", new CategoryRequest(name), Ct);
            response.EnsureSuccessStatusCode();
            ids.Add((await response.Content.ReadFromJsonAsync<SampleCategory>(Ct))!.Id);
        }

        return ids;
    }

    [Fact]
    public Task ListCategories() => AssertCannotList("GET /samples/categories", c => c.GetAsync(new Uri("/samples/categories", UriKind.Relative), Ct));

    [Fact]
    public Task GetCategory() => AssertCannotRead("GET /samples/categories/{id}", id => $"/samples/categories/{id}");

    [Fact]
    public Task UpdateCategory() => AssertCannotModify("PUT /samples/categories/{id}",
        id => new HttpRequestMessage(HttpMethod.Put, $"/samples/categories/{id}") { Content = JsonContent.Create(new CategoryRequest("taken")) },
        id => $"/samples/categories/{id}");

    [Fact]
    public Task DeleteCategory() => AssertCannotModify("DELETE /samples/categories/{id}",
        id => new HttpRequestMessage(HttpMethod.Delete, $"/samples/categories/{id}"),
        id => $"/samples/categories/{id}");

    [Fact]
    public Task CreateProductInAnotherBusinessCategory() => AssertCannotReference("POST /samples/products",
        id => new HttpRequestMessage(HttpMethod.Post, "/samples/products") { Content = JsonContent.Create(new ProductRequest("p", id, 1m)) },
        c => c.GetAsync(new Uri("/samples/products", UriKind.Relative), Ct));
}
