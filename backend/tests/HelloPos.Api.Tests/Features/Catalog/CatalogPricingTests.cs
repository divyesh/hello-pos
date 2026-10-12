using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Pricing;
using Microsoft.Extensions.DependencyInjection;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>
/// FR-008 / SC-005: the catalog gives the pricing engine exactly its inputs for a moment in time, so
/// selling (#14) and the server's recalculation (#15) read the same values.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogPricingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ACatalogBuiltSale_PricesLikeTheSharedCase()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst5, category) = await BasicsAsync(shop);
        var (product, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Dal 1 kg", category, 10500));

        var feed = await FeedAsync(shop, [product], shop.App.Clock.GetUtcNow());
        var sale = feed.Sale([feed.Line(product, "a", 1m)]);
        var priced = Assert.IsType<PricingResult.Priced>(PricingEngine.Price(sale)).Sale;

        // Same as shared case incl-single-gst5: 105.00 incl. GST 5% = CGST 2.50 + SGST 2.50.
        Assert.Equal("INR", sale.Currency);
        Assert.Equal(PricingMode.TaxInclusive, sale.Pricing);
        Assert.Equal(100, sale.CashRoundingStep);
        Assert.Equal([250L, 250L], priced.Taxes.Select(t => t.Amount));
        Assert.Equal(10500, priced.Total);
        Assert.Equal("GST 5%", priced.Taxes[0].Profile);
        Assert.Equal(gst5, feed.Products[product].TaxProfileId);
    }

    [Fact]
    public async Task TheTaxVersionInForce_DependsOnTheMoment()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst, version) = await TaxProfileAsync(shop.Owner, shop.Id);
        var (category, _) = await CategoryAsync(shop.Owner, shop.Id, "General", gst);
        var (product, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Tea", category, 11200));
        var from = Today(shop.App).AddDays(5);
        await AddVersionAsync(shop.Owner, shop.Id, gst, from, Components(("CGST", "6"), ("SGST", "6")), version);

        var now = await FeedAsync(shop, [product], shop.App.Clock.GetUtcNow());
        var later = await FeedAsync(shop, [product], shop.App.Clock.GetUtcNow().AddDays(5));

        Assert.Equal([2.5m, 2.5m], now.Products[product].TaxProfile.Components.Select(c => c.RatePercent));
        Assert.Equal([6m, 6m], later.Products[product].TaxProfile.Components.Select(c => c.RatePercent));
    }

    [Fact]
    public async Task QuantityRules_Deposits_OpenPrices_AndTheDepositSetting_Flow_Through()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var bananas = ProductBody("Bananas", category, 69);
        (bananas["unit"], bananas["minQuantity"], bananas["step"], bananas["depositPerUnit"]) = ("kg", "0.005", "0.005", 0);
        var (weighed, _) = await ProductAsync(shop.Owner, shop.Id, bananas);
        var can = ProductBody("Soda can", category, 199);
        can["depositPerUnit"] = 10;
        var (soda, _) = await ProductAsync(shop.Owner, shop.Id, can);
        var (cake, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Cake", category, null));

        var feed = await FeedAsync(shop, [weighed, soda, cake], shop.App.Clock.GetUtcNow());

        var line = feed.Line(weighed, "b", 1.255m);
        Assert.Equal(("kg", 0.005m, 0.005m, 69L), (line.Unit, line.Step, line.MinQuantity, line.UnitPrice));
        Assert.Equal(10, feed.Line(soda, "s", 6m).DepositPerUnit);
        Assert.True(feed.Products[cake].OpenPrice);
        Assert.Equal(2500, feed.Line(cake, "c", 1m, enteredPrice: 2500).UnitPrice);
        Assert.Throws<InvalidOperationException>(() => feed.Line(cake, "c", 1m));
        Assert.False(feed.Sale([]).DepositsTaxable);
    }

    [Fact]
    public async Task UnknownIds_AreNotInTheFeed()
    {
        await using var shop = await ShopAsync(postgres);

        var feed = await FeedAsync(shop, [Guid.CreateVersion7()], shop.App.Clock.GetUtcNow());

        Assert.Empty(feed.Products);
    }

    private static async Task<CatalogPriceFeed> FeedAsync(CatalogShop shop, IReadOnlyList<Guid> products, DateTimeOffset at)
    {
        await using var scope = shop.App.Services.CreateAsyncScope();
        var db = TenancyApp.ActingAs(scope.ServiceProvider, shop.Id);
        return await CatalogPricing.ForAsync(db, products, at, TestContext.Current.CancellationToken);
    }
}
