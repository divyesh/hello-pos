using System.Net.Http.Json;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>SC-008: a pooled connection never carries one request's business into the next.</summary>
[Collection(PostgresTests.Name)]
public sealed class SessionResetTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoHundredInterleavedRequests_OverATwoConnectionPool_NeverMixBusinesses()
    {
        await using var app = await TenancyApp.CreateAsync(postgres, maxPoolSize: 2);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        await StampingAndReferencesTests.CreateCategoryAsync(app, a, "A-only");
        await StampingAndReferencesTests.CreateCategoryAsync(app, b, "B-only");

        using var gate = new SemaphoreSlim(16);
        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            await gate.WaitAsync(Ct);
            try
            {
                var business = i % 2 == 0 ? a : b;
                using var client = app.Client(business);
                var names = (await client.GetFromJsonAsync<List<SampleCategory>>("/samples/categories", Ct))!.Select(c => c.Name).ToList();
                return (business, names);
            }
            finally
            {
                gate.Release();
            }
        }));

        Assert.All(results, r => Assert.Equal([r.business == a ? "A-only" : "B-only"], r.names));
    }

    [Fact]
    public async Task AConnectionThatServedBusinessA_ShowsNothingToTheNextContextWithoutABusiness()
    {
        await using var app = await TenancyApp.CreateAsync(postgres, maxPoolSize: 1);
        var a = await app.CreateBusinessAsync("A");
        await StampingAndReferencesTests.CreateCategoryAsync(app, a, "A1");

        await using (var asA = app.Db(a))
        {
            Assert.Equal(1, await asA.SampleCategories.CountAsync(Ct));
        }

        await using var nobody = app.Db(null);
        Assert.Equal(0, await nobody.SampleCategories.AcrossAllBusinesses().CountAsync(Ct)); // RLS alone, same pooled connection
    }
}
