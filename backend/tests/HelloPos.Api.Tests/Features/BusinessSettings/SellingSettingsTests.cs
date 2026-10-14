using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.BusinessSettings.SettingsApi;

namespace HelloPos.Api.Tests.Features.BusinessSettings;

/// <summary>Feature #12 US2 (discount approval limit, FR-006–FR-008, clarification Q2) and the selling settings row.</summary>
[Collection(PostgresTests.Name)]
public sealed class SellingSettingsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ANewBusiness_StartsAtZeroPercent_NoTables_NoTips()
    {
        await using var shop = await ShopAsync(postgres);

        var selling = await JsonAsync(await AssertContractAsync(await GetSellingAsync(shop.Manager, shop.Id), HttpStatusCode.OK, "getSellingSettings"));

        Assert.Equal(("0", false, false, "INR"), ((string)selling["discountLimitPercent"]!, (bool)selling["usesTables"]!, (bool)selling["tipsEnabled"]!, (string)selling["currency"]!));
        Assert.Null(selling["discountLimitAmount"]);
        Assert.Empty(selling["tipPresets"]!.AsArray());
    }

    [Fact]
    public async Task AnOwner_SetsTheLimit_AsAPercentageAndAnAmount()
    {
        await using var shop = await ShopAsync(postgres);
        var body = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)));
        body["discountLimitPercent"] = "12.5";
        body["discountLimitAmount"] = 2000;

        var saved = await JsonAsync(await AssertContractAsync(await UpdateSellingAsync(shop.Owner, shop.Id, body), HttpStatusCode.OK, "updateSellingSettings"));

        Assert.Equal(("12.5", 2000L, 2), ((string)saved["discountLimitPercent"]!, (long)saved["discountLimitAmount"]!, (int)saved["version"]!));
        Assert.Equal(["selling:updated"], await SettingsHistoryAsync(shop.App));

        // Removing the amount keeps only the percentage.
        var again = SellingBody(saved);
        again.Remove("discountLimitAmount");
        var cleared = await JsonAsync(await UpdateSellingAsync(shop.Owner, shop.Id, again));
        Assert.Null(cleared["discountLimitAmount"]);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("10.555")]
    [InlineData("ten")]
    public async Task ABadPercentage_IsAFieldError(string percent)
    {
        await using var shop = await ShopAsync(postgres);
        var body = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)));
        body["discountLimitPercent"] = percent;

        var problem = await JsonAsync(await AssertContractAsync(await UpdateSellingAsync(shop.Owner, shop.Id, body), HttpStatusCode.BadRequest, "updateSellingSettings"));

        Assert.NotNull(problem["errors"]!["discountLimitPercent"]);
    }

    [Fact]
    public async Task ANegativeAmount_IsAFieldError_AndAStaleVersionAConflict()
    {
        await using var shop = await ShopAsync(postgres);
        var current = await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id));
        var negative = SellingBody(current);
        negative["discountLimitAmount"] = -5;
        var stale = SellingBody(current);
        stale["version"] = 7;

        Assert.Equal(HttpStatusCode.BadRequest, (await UpdateSellingAsync(shop.Owner, shop.Id, negative)).StatusCode);
        await AssertContractAsync(await UpdateSellingAsync(shop.Owner, shop.Id, stale), HttpStatusCode.Conflict, "updateSellingSettings");
    }

    [Fact]
    public async Task AManager_SeesTheLimit_ButCannotChangeIt()
    {
        await using var shop = await ShopAsync(postgres);
        var body = SellingBody(await JsonAsync(await GetSellingAsync(shop.Manager, shop.Id)));
        body["discountLimitPercent"] = "50";

        await AssertContractAsync(await UpdateSellingAsync(shop.Manager, shop.Id, body), HttpStatusCode.Forbidden, "updateSellingSettings");

        Assert.Equal("0", (string)(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)))["discountLimitPercent"]!);
        Assert.Empty(await SettingsHistoryAsync(shop.App));
    }
}
