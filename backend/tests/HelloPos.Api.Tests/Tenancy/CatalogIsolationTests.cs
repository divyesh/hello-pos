using System.Net;
using HelloPos.Api.Tests.Features.Catalog;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature #11 FR-014 / SC-006 (Constitution III): no business can read, change or link another
/// business's catalog records, by any catalog operation.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogIsolationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task TaxProfilesAndCategories_OfAnotherBusiness_AreInvisibleAndUnreachable()
    {
        await using var shop = await ShopAsync(postgres);
        var (theirProfile, profileVersion) = await TaxProfileAsync(shop.Owner, shop.Id, "Theirs");
        var (theirCategory, categoryVersion) = await CategoryAsync(shop.Owner, shop.Id, "Their category", theirProfile);

        // The same owner opens a second business: the first one's catalog must vanish from every view.
        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);
        var (mine, _) = await TaxProfileAsync(shop.Owner, other, "Mine");

        Assert.Equal(["Mine"], (await JsonAsync(await ListTaxProfilesAsync(shop.Owner, other))).AsArray().Select(p => (string)p!["name"]!));
        Assert.Empty((await JsonAsync(await ListCategoriesAsync(shop.Owner, other))).AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await GetTaxProfileAsync(shop.Owner, other, theirProfile)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetCategoryAsync(shop.Owner, other, theirCategory)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RenameTaxProfileAsync(shop.Owner, other, theirProfile, "Hacked", profileVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetTaxProfileActiveAsync(shop.Owner, other, theirProfile, false, profileVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await AddVersionAsync(shop.Owner, other, theirProfile, Today(shop.App).AddDays(2), Components(("X", "1")), profileVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateCategoryAsync(shop.Owner, other, theirCategory, "Hacked", 0, mine, categoryVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetCategoryActiveAsync(shop.Owner, other, theirCategory, false, categoryVersion)).StatusCode);

        // Their tax profile id cannot be linked from this business.
        var linked = await JsonAsync(await CreateCategoryAsync(shop.Owner, other, "Linked", theirProfile));
        Assert.NotNull(linked["errors"]?["defaultTaxProfileId"]);

        // And nothing of theirs changed.
        await OpenAsync(shop.Owner, shop.Id);
        var theirs = await JsonAsync(await GetTaxProfileAsync(shop.Owner, shop.Id, theirProfile));
        Assert.Equal("Theirs", (string?)theirs["name"]);
        Assert.True((bool?)theirs["active"]);
    }

    [Fact]
    public async Task Products_OfAnotherBusiness_AreInvisibleAndUnreachable_AndTheirBarcodesDoNotCollide()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, theirCategory) = await BasicsAsync(shop);
        var (theirProduct, version) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Their cola", theirCategory, 199, "067000004568"));

        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);
        var (profile, _) = await TaxProfileAsync(shop.Owner, other, "Mine");
        var (category, _) = await CategoryAsync(shop.Owner, other, "Mine", profile);

        Assert.Equal(0, (int)(await JsonAsync(await ListProductsAsync(shop.Owner, other)))["total"]!);
        Assert.Equal(0, (int)(await JsonAsync(await ListProductsAsync(shop.Owner, other, "?q=0067000004568")))["total"]!);
        Assert.Equal(HttpStatusCode.NotFound, (await GetProductAsync(shop.Owner, other, theirProduct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateProductAsync(shop.Owner, other, theirProduct, ProductBody("Hacked", category), version)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetProductActiveAsync(shop.Owner, other, theirProduct, false, version)).StatusCode);

        // Their category cannot be linked; the same barcode is free in this business.
        Assert.NotNull((await JsonAsync(await CreateProductAsync(shop.Owner, other, ProductBody("Linked", theirCategory))))["errors"]?["categoryId"]);
        var (mine, _) = await ProductAsync(shop.Owner, other, ProductBody("My cola", category, 249, "067000004568"));
        var found = await JsonAsync(await ListProductsAsync(shop.Owner, other, "?q=067000004568"));
        Assert.Equal([mine.ToString()], found["items"]!.AsArray().Select(p => (string)p!["id"]!));

        // The pricing feed of this business knows nothing of theirs.
        await using var scope = shop.App.Services.CreateAsyncScope();
        var feed = await HelloPos.Api.Features.Catalog.CatalogPricing.ForAsync(
            TenancyApp.ActingAs(scope.ServiceProvider, other), [theirProduct, mine], shop.App.Clock.GetUtcNow(), TestContext.Current.CancellationToken);
        Assert.Equal([mine], feed.Products.Keys);
    }

    [Fact]
    public async Task AnImport_OnlyEverSeesTheOpenBusiness()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "Their GST");
        var (_, theirCategory) = await BasicsAsync(shop);
        var cola = ProductBody("Their cola", theirCategory, 199, "067000004568");
        cola["sku"] = "COLA";
        await ProductAsync(shop.Owner, shop.Id, cola);

        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);
        await TaxProfileAsync(shop.Owner, other, "Mine");

        // Their tax profile name is unknown here; their SKU, barcode and category name do not match anything.
        var preview = await JsonAsync(await CatalogImportTests.PreviewAsync(shop.Owner, other, CatalogImportTests.Csv(
            "X1,Taxed,Drinks,Their GST,,piece,1.00,false,1,1,,,0,true",
            "COLA,My cola,General,Mine,,piece,2.49,false,1,1,067000004568,,0,true")));

        Assert.Equal(["error", "create"], preview["rows"]!.AsArray().Select(r => (string)r!["outcome"]!));
        await CatalogImportTests.ImportAsync(shop.Owner, other, CatalogImportTests.Csv("COLA,My cola,General,Mine,,piece,2.49,false,1,1,067000004568,,0,true"));

        await OpenAsync(shop.Owner, shop.Id);
        var theirs = (await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id, "?q=COLA")))["items"]![0]!;
        Assert.Equal(("Their cola", 199L), ((string)theirs["name"]!, (long)theirs["unitPrice"]!));
        Assert.Single((await JsonAsync(await ListCategoriesAsync(shop.Owner, shop.Id))).AsArray());
    }
}
