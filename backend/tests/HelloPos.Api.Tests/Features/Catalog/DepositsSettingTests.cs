using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>FR-016: the owner decides whether container deposits are taxed; omitting the field keeps it.</summary>
[Collection(PostgresTests.Name)]
public sealed class DepositsSettingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Owner_TurnsDepositTaxOn_AndOmittingTheFieldKeepsIt()
    {
        await using var shop = await ShopAsync(postgres);
        var settings = Settings("Shop", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100);
        Assert.False((bool?)(await JsonAsync(await GetCurrentAsync(shop.Owner, shop.Id)))["depositsTaxable"]);

        settings["depositsTaxable"] = true;
        var on = await JsonAsync(await AssertContractAsync(await UpdateAsync(shop.Owner, settings, shop.Id), HttpStatusCode.OK, "updateCurrentBusiness"));
        Assert.True((bool?)on["depositsTaxable"]);

        settings.Remove("depositsTaxable");
        var kept = await JsonAsync(await UpdateAsync(shop.Owner, settings, shop.Id));
        Assert.True((bool?)kept["depositsTaxable"]);
        Assert.Equal(["business:updated"], await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task Managers_CannotChangeIt()
    {
        await using var shop = await ShopAsync(postgres);
        var settings = Settings("Shop", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100);
        settings["depositsTaxable"] = true;

        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateAsync(shop.Manager, settings, shop.Id)).StatusCode);
    }
}
