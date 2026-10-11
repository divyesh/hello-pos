using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Businesses;

/// <summary>US3 open, switch and edit: FR-012–FR-015; research R7.</summary>
[Collection(PostgresTests.Name)]
public sealed class OpenAndEditTests(PostgresFixture postgres)
{
    private const string BusinessHeader = "X-HelloPos-Business";

    [Fact]
    public async Task Open_MakesItTheCurrentBusiness()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var shop = await CreateIdAsync(owner, "Patel Grocers");

        await AssertContractAsync(await OpenAsync(owner, shop), HttpStatusCode.NoContent, "ownerOpenBusiness");

        var current = JsonNode.Parse(await AssertContractAsync(await GetCurrentAsync(owner), HttpStatusCode.OK, "getCurrentBusiness"))!;
        Assert.Equal(shop.ToString(), (string?)current["id"]);
        Assert.Equal("Patel Grocers", (string?)current["name"]);
        Assert.Equal(shop.ToString(), (string?)JsonNode.Parse(await (await MeAsync(owner)).Content.ReadAsStringAsync(Ct))!["currentBusinessId"]);
    }

    [Fact]
    public async Task WithNoBusinessOpen_CurrentIs401()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        await CreateIdAsync(owner, "Patel Grocers");

        await AssertContractAsync(await GetCurrentAsync(owner), HttpStatusCode.Unauthorized, "getCurrentBusiness");
    }

    [Fact]
    public async Task OpeningAnotherOwnersBusiness_LooksExactlyLikeAnUnknownOne()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var asha = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        using var ben = await app.SignUpVerifiedOwnerAsync("ben@example.com");
        var bens = await CreateIdAsync(ben, "Ben's Diner");

        var theirs = await AssertContractAsync(await OpenAsync(asha, bens), HttpStatusCode.NotFound, "ownerOpenBusiness");
        var unknown = await AssertContractAsync(await OpenAsync(asha, Guid.CreateVersion7()), HttpStatusCode.NotFound, "ownerOpenBusiness");

        Assert.Equal(WithoutTraceId(theirs), WithoutTraceId(unknown));
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetCurrentAsync(asha)).StatusCode);
    }

    [Fact]
    public async Task Edit_SavesTheOpenBusinessOnly()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var first = await CreateIdAsync(owner, "First Shop");
        var second = await CreateIdAsync(owner, "Second Shop");
        await OpenAsync(owner, first);
        var changed = Settings("First Shop Renamed", "CA", "CAD", "tax_exclusive", 4, "America/Vancouver", 10);

        var saved = JsonNode.Parse(await AssertContractAsync(await UpdateAsync(owner, changed, first), HttpStatusCode.OK, "updateCurrentBusiness"))!;

        foreach (var (key, value) in changed)
        {
            Assert.Equal(value!.ToJsonString(), saved[key]!.ToJsonString());
        }

        await OpenAsync(owner, second);
        var other = JsonNode.Parse(await (await GetCurrentAsync(owner)).Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("Second Shop", (string?)other["name"]);
        Assert.Equal("IN", (string?)other["countryCode"]);
        Assert.Equal(["First Shop Renamed", "Second Shop"],
            JsonNode.Parse(await (await ListAsync(owner)).Content.ReadAsStringAsync(Ct))!.AsArray().Select(b => (string)b!["name"]!));
    }

    [Fact]
    public async Task Edit_RejectsInvalidSettingsByField_AndChangesNothing()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var shop = await CreateIdAsync(owner, "Patel Grocers");
        await OpenAsync(owner, shop);
        var before = await (await GetCurrentAsync(owner)).Content.ReadAsStringAsync(Ct);

        var body = await AssertContractAsync(
            await UpdateAsync(owner, Settings("", "IN", "INR", "tax_inclusive", 13, "Asia/Kolkata", 100), shop),
            HttpStatusCode.BadRequest, "updateCurrentBusiness");

        Assert.Equal(["financialYearStartMonth", "name"], ErrorFields(body).Order(StringComparer.Ordinal));
        Assert.Equal(before, await (await GetCurrentAsync(owner)).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task StaleTab_IsRefused_AndChangesNothing()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var tab1 = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var first = await CreateIdAsync(tab1, "First Shop");
        var second = await CreateIdAsync(tab1, "Second Shop");
        await OpenAsync(tab1, first);

        // Another tab of the same browser session switches to the second business.
        await OpenAsync(tab1, second);

        await AssertContractAsync(await GetCurrentAsync(tab1, first), HttpStatusCode.Conflict, "getCurrentBusiness");
        var refused = await AssertContractAsync(
            await UpdateAsync(tab1, Settings("Wrong Shop", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), first),
            HttpStatusCode.Conflict, "updateCurrentBusiness");
        Assert.Equal("urn:hellopos:problem:business-changed", (string?)JsonNode.Parse(refused)!["type"]);
        Assert.Equal(["First Shop", "Second Shop"],
            JsonNode.Parse(await (await ListAsync(tab1)).Content.ReadAsStringAsync(Ct))!.AsArray().Select(b => (string)b!["name"]!));
    }

    [Fact]
    public async Task OpenAndEdit_NeedTheClientHeader()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var shop = await CreateIdAsync(owner, "Patel Grocers");
        await OpenAsync(owner, shop);
        owner.DefaultRequestHeaders.Remove("X-HelloPos-Client");

        await AssertContractAsync(await OpenAsync(owner, shop), HttpStatusCode.Forbidden, "ownerOpenBusiness");
        await AssertContractAsync(
            await UpdateAsync(owner, Settings("X", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), shop),
            HttpStatusCode.Forbidden, "updateCurrentBusiness");
    }

    internal static async Task<Guid> CreateIdAsync(HttpClient owner, string name)
    {
        using var response = await CreateAsync(owner, Settings(name, "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100));
        response.EnsureSuccessStatusCode();
        return Guid.Parse((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!);
    }

    internal static Task<HttpResponseMessage> OpenAsync(HttpClient owner, Guid business) =>
        owner.PostAsync(new Uri($"/owner/businesses/{business}/open", UriKind.Relative), null, Ct);

    internal static async Task<HttpResponseMessage> GetCurrentAsync(HttpClient client, Guid? pageBusiness = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/business");
        if (pageBusiness is { } id)
        {
            request.Headers.Add(BusinessHeader, id.ToString());
        }

        return await client.SendAsync(request, Ct);
    }

    internal static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, JsonObject settings, Guid pageBusiness)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/business") { Content = JsonContent.Create(settings) };
        request.Headers.Add(BusinessHeader, pageBusiness.ToString());
        return await client.SendAsync(request, Ct);
    }
}
