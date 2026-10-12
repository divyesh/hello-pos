using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US1 tax profiles: FR-001, FR-002 (owners only, dated versions), FR-009, FR-010, FR-015.</summary>
[Collection(PostgresTests.Name)]
public sealed class TaxProfileTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Owner_CreatesProfilesOfEachKind()
    {
        await using var shop = await ShopAsync(postgres);

        var gst = await JsonAsync(await CreateTaxProfileAsync(shop.Owner, shop.Id, "GST 5%", "taxable", Components(("CGST", "2.5"), ("SGST", "2.5"))));
        Assert.Equal("taxable", (string?)gst["kind"]);
        var current = Assert.Single(gst["versions"]!.AsArray())!;
        Assert.True((bool?)current["current"]);
        Assert.False((bool?)current["editable"]);
        Assert.Equal(Day(Today(shop.App)), (string?)current["effectiveFrom"]);
        Assert.Equal(["2.5", "2.5"], current["components"]!.AsArray().Select(c => (string)c!["ratePercent"]!));

        await AssertContractAsync(await CreateTaxProfileAsync(shop.Owner, shop.Id, "Nil", "zeroRated", []), HttpStatusCode.Created, "createTaxProfile");
        await AssertContractAsync(await CreateTaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt", []), HttpStatusCode.Created, "createTaxProfile");
        var listed = await JsonAsync(await AssertResponseAsync(await ListTaxProfilesAsync(shop.Owner, shop.Id), "listTaxProfiles"));
        Assert.Equal(["Exempt", "GST 5%", "Nil"], listed.AsArray().Select(p => (string)p!["name"]!));
        Assert.Equal(["taxProfile:created", "taxProfile:created", "taxProfile:created"], await HistoryAsync(shop.App));
    }

    [Theory]
    [InlineData("taxable", "[]", "components")]
    [InlineData("exempt", """[{"label":"X","ratePercent":"5"}]""", "components")]
    [InlineData("taxable", """[{"label":"GST","ratePercent":"100.5"}]""", "components")]
    [InlineData("taxable", """[{"label":"GST","ratePercent":"5"},{"label":"gst","ratePercent":"5"}]""", "components")]
    [InlineData("taxable", """[{"label":"","ratePercent":"5"}]""", "components")]
    public async Task InvalidComponents_AreRefused(string kind, string components, string field)
    {
        await using var shop = await ShopAsync(postgres);

        var problem = await JsonAsync(await AssertContractAsync(
            await CreateTaxProfileAsync(shop.Owner, shop.Id, "P", kind, JsonNode.Parse(components)!.AsArray()), HttpStatusCode.BadRequest, "createTaxProfile"));

        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task Names_AreUniqueIgnoringCase()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");

        var problem = await JsonAsync(await AssertContractAsync(
            await CreateTaxProfileAsync(shop.Owner, shop.Id, " gst 5% ", "taxable", Components(("GST", "5"))), HttpStatusCode.BadRequest, "createTaxProfile"));

        Assert.NotNull(problem["errors"]!["name"]);
    }

    [Fact]
    public async Task Managers_ReadProfiles_ButCannotChangeThem()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id);

        await AssertContractAsync(await ListTaxProfilesAsync(shop.Manager, shop.Id), HttpStatusCode.OK, "listTaxProfiles");
        await AssertContractAsync(await GetTaxProfileAsync(shop.Manager, shop.Id, id), HttpStatusCode.OK, "getTaxProfile");
        await AssertContractAsync(await CreateTaxProfileAsync(shop.Manager, shop.Id, "X", "exempt", []), HttpStatusCode.Forbidden, "createTaxProfile");
        await AssertContractAsync(await RenameTaxProfileAsync(shop.Manager, shop.Id, id, "X", version), HttpStatusCode.Forbidden, "renameTaxProfile");
        await AssertContractAsync(await AddVersionAsync(shop.Manager, shop.Id, id, Today(shop.App).AddDays(5), Components(("GST", "5")), version),
            HttpStatusCode.Forbidden, "addTaxProfileVersion");
        await AssertContractAsync(await SetTaxProfileActiveAsync(shop.Manager, shop.Id, id, false, version), HttpStatusCode.Forbidden, "deactivateTaxProfile");
    }

    [Fact]
    public async Task AFutureVersion_TakesOverOnItsDate_AndThenCannotChange()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id, "HST", components: Components(("HST", "13")));
        var from = Today(shop.App).AddDays(10);

        var added = await JsonAsync(await AssertContractAsync(
            await AddVersionAsync(shop.Owner, shop.Id, id, from, Components(("HST", "15")), version), HttpStatusCode.Created, "addTaxProfileVersion"));

        var versions = added["versions"]!.AsArray();
        Assert.Equal([true, false], versions.Select(v => (bool)v!["current"]!));
        Assert.Equal([false, true], versions.Select(v => (bool)v!["editable"]!));
        var futureId = Guid.Parse((string)versions[1]!["id"]!);
        version = (int)added["version"]!;

        var changed = await JsonAsync(await AssertContractAsync(
            await UpdateVersionAsync(shop.Owner, shop.Id, id, futureId, from, Components(("HST", "14")), version), HttpStatusCode.OK, "updateTaxProfileVersion"));
        version = (int)changed["version"]!;

        // Ten days later the back-office session has timed out: the owner signs in again.
        shop.App.Clock.Advance(TimeSpan.FromDays(10));
        using var owner = shop.App.OwnerClient();
        Assert.True((await SignInAsync(owner, "owner@example.com")).IsSuccessStatusCode);
        await OpenAsync(owner, shop.Id);
        var now = await JsonAsync(await GetTaxProfileAsync(owner, shop.Id, id));
        var current = Assert.Single(now["versions"]!.AsArray())!;
        Assert.Equal("14", (string?)current["components"]![0]!["ratePercent"]);
        Assert.True((bool?)current["current"]);

        await AssertContractAsync(await UpdateVersionAsync(owner, shop.Id, id, futureId, from, Components(("HST", "16")), version),
            HttpStatusCode.Conflict, "updateTaxProfileVersion");
        await AssertContractAsync(await DeleteVersionAsync(owner, shop.Id, id, futureId), HttpStatusCode.Conflict, "deleteTaxProfileVersion");
    }

    [Fact]
    public async Task VersionsCannotStartInThePast_OrTwiceOnOneDay()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id);

        await AssertContractAsync(await AddVersionAsync(shop.Owner, shop.Id, id, Today(shop.App).AddDays(-1), Components(("GST", "5")), version),
            HttpStatusCode.BadRequest, "addTaxProfileVersion");
        await AssertContractAsync(await AddVersionAsync(shop.Owner, shop.Id, id, Today(shop.App), Components(("GST", "5")), version),
            HttpStatusCode.BadRequest, "addTaxProfileVersion");
    }

    [Fact]
    public async Task AFutureVersion_CanBeRemoved()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id);
        var added = await JsonAsync(await AddVersionAsync(shop.Owner, shop.Id, id, Today(shop.App).AddDays(3), Components(("CGST", "6"), ("SGST", "6")), version));
        var futureId = Guid.Parse((string)added["versions"]![1]!["id"]!);

        var removed = await JsonAsync(await AssertContractAsync(await DeleteVersionAsync(shop.Owner, shop.Id, id, futureId), HttpStatusCode.OK, "deleteTaxProfileVersion"));

        Assert.Single(removed["versions"]!.AsArray());
        Assert.Contains("taxProfileVersion:deleted", await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task Rename_Deactivate_Reactivate_WithStaleVersionsRefused()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id);

        var renamed = await JsonAsync(await AssertContractAsync(await RenameTaxProfileAsync(shop.Owner, shop.Id, id, "GST 5% (food)", version), HttpStatusCode.OK, "renameTaxProfile"));
        await AssertContractAsync(await RenameTaxProfileAsync(shop.Owner, shop.Id, id, "Stale", version), HttpStatusCode.Conflict, "renameTaxProfile");
        var off = await JsonAsync(await AssertContractAsync(await SetTaxProfileActiveAsync(shop.Owner, shop.Id, id, false, (int)renamed["version"]!), HttpStatusCode.OK, "deactivateTaxProfile"));
        Assert.False((bool?)off["active"]);
        Assert.Empty((await JsonAsync(await ListTaxProfilesAsync(shop.Owner, shop.Id, "?active=true"))).AsArray());
        var on = await JsonAsync(await AssertContractAsync(await SetTaxProfileActiveAsync(shop.Owner, shop.Id, id, true, (int)off["version"]!), HttpStatusCode.OK, "reactivateTaxProfile"));
        Assert.True((bool?)on["active"]);
        Assert.Equal(["taxProfile:created", "taxProfile:updated", "taxProfile:deactivated", "taxProfile:reactivated"], await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task Unknown_Is404()
    {
        await using var shop = await ShopAsync(postgres);

        await AssertContractAsync(await GetTaxProfileAsync(shop.Owner, shop.Id, Guid.CreateVersion7()), HttpStatusCode.NotFound, "getTaxProfile");
    }

    private static async Task<HttpResponseMessage> AssertResponseAsync(HttpResponseMessage response, string operation)
    {
        await AssertContractAsync(response, HttpStatusCode.OK, operation);
        return response;
    }
}
