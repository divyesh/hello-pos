using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>FR-010, FR-012, FR-013: stamping, foreign references, and "not found" for other businesses.</summary>
[Collection(PostgresTests.Name)]
public sealed class StampingAndReferencesTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProductReferringToAnotherBusinessCategory_Is404_AndNothingIsStored()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        var categoryOfB = await CreateCategoryAsync(app, b, "B's category");

        using var asA = app.Client(a);
        using var response = await asA.PostAsJsonAsync("/samples/products", new ProductRequest("sneaky", categoryOfB, 1.5m), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var owner = app.OwnerDb();
        Assert.Equal(0, await owner.SampleProducts.AcrossAllBusinessesForTests().CountAsync(Ct));
    }

    [Fact]
    public async Task ReadingAnotherBusinessRecord_LooksExactlyLikeAnUnknownId()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        var categoryOfB = await CreateCategoryAsync(app, b, "B's category");
        using var asA = app.Client(a);

        using var foreign = await asA.GetAsync(new Uri($"/samples/categories/{categoryOfB}", UriKind.Relative), Ct);
        using var unknown = await asA.GetAsync(new Uri($"/samples/categories/{Guid.CreateVersion7()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(WithoutTraceId(await unknown.Content.ReadAsStringAsync(Ct)), WithoutTraceId(await foreign.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task ChangingBusinessIdOfAnExistingRecord_IsRejected()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        var categoryOfA = await CreateCategoryAsync(app, a, "A's category");

        await using var db = app.Db(a);
        var category = await db.SampleCategories.SingleAsync(c => c.Id == categoryOfA, Ct);
        category.BusinessId = b;

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync(Ct));
        await using var owner = app.OwnerDb();
        Assert.Equal(a, (await owner.SampleCategories.AcrossAllBusinessesForTests().SingleAsync(Ct)).BusinessId);
    }

    [Fact]
    public async Task SavingWithoutACurrentBusiness_Throws()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        await using var db = app.Db(null);
        db.SampleCategories.Add(new SampleCategory { Name = "orphan" });

        await Assert.ThrowsAsync<NoCurrentBusinessException>(() => db.SaveChangesAsync(Ct));
    }

    internal static async Task<Guid> CreateCategoryAsync(TenancyApp app, Guid business, string name)
    {
        using var client = app.Client(business);
        using var response = await client.PostAsJsonAsync("/samples/categories", new CategoryRequest(name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SampleCategory>(Ct))!.Id;
    }

    private static string WithoutTraceId(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .Select(p => $"{p.Name}={p.Value}"));
    }
}
