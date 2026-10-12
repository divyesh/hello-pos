using System.Net;
using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Pricing;
using Microsoft.Extensions.DependencyInjection;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US3 scheduled item discounts: FR-007, FR-008, FR-015 (research R6).</summary>
[Collection(PostgresTests.Name)]
public sealed class ItemDiscountTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Percent_AndFixed_AreScheduled_AndListedWithTheirStatus()
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);

        var percent = await JsonAsync(await AssertContractAsync(
            await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "20", today, today.AddDays(6))), HttpStatusCode.Created, "createItemDiscount"));
        await AssertContractAsync(
            await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("fixed", "50", today.AddDays(10))), HttpStatusCode.Created, "createItemDiscount");

        Assert.Equal("running", (string?)percent["status"]);
        var listed = await JsonAsync(await AssertContractAsync(await ListDiscountsAsync(shop.Manager, shop.Id, product), HttpStatusCode.OK, "listItemDiscounts"));
        Assert.Equal(["future", "running"], listed.AsArray().Select(d => (string)d!["status"]!));
        Assert.Equal(["50", "20"], listed.AsArray().Select(d => (string)d!["value"]!));
        Assert.Null(listed[0]!["toDate"]);
        Assert.Equal(2, (await HistoryAsync(shop.App)).Count(h => h == "itemDiscount:created"));
    }

    [Theory]
    [InlineData("percent", "101", 0, null, "value")]
    [InlineData("percent", "12.3456", 0, null, "value")]
    [InlineData("fixed", "0.5", 0, null, "value")]
    [InlineData("half", "10", 0, null, "kind")]
    [InlineData("percent", "10", -1, null, "fromDate")]
    [InlineData("percent", "10", 3, 2, "toDate")]
    public async Task InvalidDiscounts_AreRefused(string kind, string value, int fromOffset, int? toOffset, string field)
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);

        var problem = await JsonAsync(await AssertContractAsync(
            await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody(kind, value, today.AddDays(fromOffset), toOffset is { } t ? today.AddDays(t) : null)),
            HttpStatusCode.BadRequest, "createItemDiscount"));

        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task OverlappingPeriods_AreRefused_NamingTheOther()
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);
        await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "20", today.AddDays(2), today.AddDays(8)));

        var overlapping = await JsonAsync(await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("fixed", "10", today.AddDays(8))));
        var adjacent = await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("fixed", "10", today.AddDays(9)));

        Assert.Contains(Day(today.AddDays(2)), (string)overlapping["errors"]!["fromDate"]![0]!, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOverlappingCreates_OneWins()
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);

        var results = await Task.WhenAll(
            CreateDiscountAsync(shop.Owner, shop.Id, product, DiscountBody("percent", "10", today.AddDays(1), today.AddDays(5))),
            CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "15", today.AddDays(3), today.AddDays(7))));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.BadRequest], results.Select(r => r.StatusCode).Order());
        Assert.Single((await JsonAsync(await ListDiscountsAsync(shop.Owner, shop.Id, product))).AsArray());
    }

    [Fact]
    public async Task Future_CanChangeOrGo_Running_OnlyEndsEarly_Ended_IsReadOnly()
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);
        var future = await IdOfAsync(await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "10", today.AddDays(5), today.AddDays(9))));
        var running = await IdOfAsync(await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "20", today, today.AddDays(3))));

        var changed = await JsonAsync(await AssertContractAsync(
            await UpdateDiscountAsync(shop.Manager, shop.Id, product, future, DiscountBody("fixed", "25", today.AddDays(6), today.AddDays(9))), HttpStatusCode.OK, "updateItemDiscount"));
        Assert.Equal("fixed", (string?)changed["kind"]);

        // A running discount: changing the amount is refused; ending it early is fine.
        await AssertContractAsync(await UpdateDiscountAsync(shop.Manager, shop.Id, product, running, DiscountBody("percent", "30", today, today.AddDays(3))),
            HttpStatusCode.Conflict, "updateItemDiscount");
        await AssertContractAsync(await DeleteDiscountAsync(shop.Manager, shop.Id, product, running), HttpStatusCode.Conflict, "deleteItemDiscount");
        var ended = await JsonAsync(await UpdateDiscountAsync(shop.Manager, shop.Id, product, running, DiscountBody("percent", "20", today, today)));
        Assert.Equal(Day(today), (string?)ended["toDate"]);

        await AssertContractAsync(await DeleteDiscountAsync(shop.Manager, shop.Id, product, future), HttpStatusCode.NoContent, "deleteItemDiscount");
        Assert.Contains("itemDiscount:deleted", await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task TheDiscountInForce_FeedsThePricingEngine_ByDate()
    {
        await using var shop = await ShopAsync(postgres);
        var product = await ColaAsync(shop);
        var today = Today(shop.App);
        await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("percent", "20", today.AddDays(2), today.AddDays(4)));
        await CreateDiscountAsync(shop.Manager, shop.Id, product, DiscountBody("fixed", "50", today.AddDays(5)));

        var now = shop.App.Clock.GetUtcNow();
        Assert.Null((await FeedAsync(shop, product, now)).ItemDiscount);
        Assert.Equal(new Discount(DiscountKind.Percent, 20m), (await FeedAsync(shop, product, now.AddDays(3))).ItemDiscount);
        Assert.Equal(new Discount(DiscountKind.Fixed, 50m), (await FeedAsync(shop, product, now.AddDays(30))).ItemDiscount);
    }

    [Fact]
    public async Task AnotherBusinessesProduct_IsUnreachable()
    {
        await using var shop = await ShopAsync(postgres);
        var theirs = await ColaAsync(shop);
        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);

        Assert.Equal(HttpStatusCode.NotFound, (await ListDiscountsAsync(shop.Owner, other, theirs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await CreateDiscountAsync(shop.Owner, other, theirs, DiscountBody("percent", "10", Today(shop.App)))).StatusCode);
    }

    private static async Task<Guid> ColaAsync(CatalogShop shop)
    {
        var (_, category) = await BasicsAsync(shop);
        var (product, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Cola", category, 199));
        return product;
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response) => Guid.Parse((string)(await JsonAsync(response))["id"]!);

    private static async Task<ProductPricing> FeedAsync(CatalogShop shop, Guid product, DateTimeOffset at)
    {
        await using var scope = shop.App.Services.CreateAsyncScope();
        return (await CatalogPricing.ForAsync(TenancyApp.ActingAs(scope.ServiceProvider, shop.Id), [product], at, TestContext.Current.CancellationToken)).Products[product];
    }
}
