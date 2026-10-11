using System.Net;
using System.Net.Http.Json;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>FR-006–FR-008: the current business comes only from the identity; none means 401.</summary>
[Collection(PostgresTests.Name)]
public sealed class CurrentBusinessTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutIdentity_BusinessEndpoints_Return401_AndTouchNoData()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var anonymous = app.Client();

        using var list = await anonymous.GetAsync(new Uri("/samples/categories", UriKind.Relative), Ct);
        using var create = await anonymous.PostAsJsonAsync("/samples/categories", new CategoryRequest("x"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal("application/problem+json", create.Content.Headers.ContentType?.MediaType);
        await using var owner = app.OwnerDb();
        Assert.Equal(0, await owner.SampleCategories.CountAsync(Ct));
    }

    [Fact]
    public async Task BusinessIdInBodyQueryOrHeader_IsIgnored()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        using var asA = app.Client(a);
        asA.DefaultRequestHeaders.Add("X-Business-Id", b.ToString());
        asA.DefaultRequestHeaders.Add("business_id", b.ToString());

        using var created = await asA.PostAsJsonAsync($"/samples/categories?business_id={b}", new CategoryRequest("from A", BusinessId: b), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using var owner = app.OwnerDb();
        var stored = await owner.SampleCategories.AcrossAllBusinessesForTests().SingleAsync(Ct);
        Assert.Equal(a, stored.BusinessId);
    }

    [Fact]
    public async Task ConcurrentRequests_EachSeeOnlyTheirOwnBusiness()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        using (var asA = app.Client(a)) { (await asA.PostAsJsonAsync("/samples/categories", new CategoryRequest("A-only"), Ct)).Dispose(); }
        using (var asB = app.Client(b)) { (await asB.PostAsJsonAsync("/samples/categories", new CategoryRequest("B-only"), Ct)).Dispose(); }

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(async i =>
        {
            var business = i % 2 == 0 ? a : b;
            using var client = app.Client(business);
            var names = (await client.GetFromJsonAsync<List<SampleCategory>>("/samples/categories", Ct))!.Select(c => c.Name).ToList();
            return (business, names);
        }));

        foreach (var (business, names) in results)
        {
            Assert.Equal([business == a ? "A-only" : "B-only"], names);
        }
    }
}
