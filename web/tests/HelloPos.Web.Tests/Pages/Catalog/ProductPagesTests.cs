using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Catalog;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Catalog;

/// <summary>Feature #11 US2 web: product list (search, filters, paging) and the product form.</summary>
public sealed class ProductPagesTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000ff");

    private readonly FakeCatalogService catalog = new();
    private readonly FakeBusinessService businesses = new();
    private readonly CategoryItem drinks;

    public ProductPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<ICatalogService>(catalog);
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
        businesses.Settings[Shop] = new BusinessSettings("Shop", "CA", "CAD", PricingMode.TaxExclusive, 1, "America/Toronto", 0.05m);
        var hst = FakeCatalogService.Profile("HST 13%", "taxable", true, new TaxComponentItem("HST", "13"));
        catalog.Profiles.Add(hst);
        drinks = new CategoryItem(Guid.NewGuid(), "Drinks", 0, hst.Id, true, 1);
        catalog.Categories.Add(drinks);
    }

    [Fact]
    public async Task List_ShowsPricesInTheCurrency_AndPages()
    {
        catalog.Products.Add(FakeCatalogService.Product("Cola", drinks.Id, 199));
        catalog.Products.Add(FakeCatalogService.Product("Cake", drinks.Id, null));
        catalog.Products.Add(FakeCatalogService.Product("Water", drinks.Id, 99));
        await OpenAsync(StaffRole.Manager);

        var page = Render<Products>();

        Assert.Equal(["1.99", "Open price"], page.FindAll("[data-testid=price]").Select(e => e.TextContent));
        Assert.Contains("Page 1 of 2", page.Find("[data-testid=page]").TextContent, StringComparison.Ordinal);
        page.Find("[data-testid=next]").Click();
        Assert.Equal(["0.99"], page.FindAll("[data-testid=price]").Select(e => e.TextContent));
        Assert.Equal(("", null, true, 2), catalog.Searches[^1]);
    }

    [Fact]
    public async Task SearchAndFilters_GoToTheService()
    {
        catalog.Products.Add(FakeCatalogService.Product("Cola", drinks.Id));
        await OpenAsync(StaffRole.Manager);
        var page = Render<Products>();

        Field(page, "search").Change("col");
        page.Find("[data-testid=filter-all]").Click();

        Assert.Equal(("col", null, null, 1), catalog.Searches[^1]);
        Assert.Equal(["Cola"], page.FindAll("[data-testid=product-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task NewProduct_SendsMinorUnits_AndTheBarcodes()
    {
        await OpenAsync(StaffRole.Manager);
        var page = Render<ProductEdit>();

        Type(page, "name", "Cola 355 ml");
        Type(page, "price", "1.99");
        Type(page, "deposit", "0.10");
        Field(page, "barcodes").Input("067000004568\n4011");
        page.Find("[data-testid=save]").Click();

        var saved = Assert.Single(catalog.Saved);
        Assert.Equal(("Cola 355 ml", 199L, 10L, drinks.Id), (saved.Name, saved.UnitPrice!.Value, saved.DepositPerUnit, saved.CategoryId));
        Assert.Equal(["067000004568", "4011"], saved.Barcodes);
        Assert.Null(saved.TaxProfileId);
        Assert.EndsWith("catalog/products", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABadBarcode_IsExplained_AndBlocksSaving()
    {
        await OpenAsync(StaffRole.Manager);
        var page = Render<ProductEdit>();

        Field(page, "barcodes").Input("5901234123458");

        Assert.Contains("check digit", page.Find("[data-testid=barcode-error]").TextContent, StringComparison.Ordinal);
        Assert.True(page.Find("[data-testid=save]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task TooManyDecimals_IsAFieldError_NothingSent()
    {
        await OpenAsync(StaffRole.Manager);
        var page = Render<ProductEdit>();

        Type(page, "name", "Cola");
        Type(page, "price", "1.999");
        page.Find("[data-testid=save]").Click();

        Assert.Empty(catalog.Saved);
        Assert.Contains("at most 2 decimals", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_LoadsTheProduct_SavesWithItsVersion_AndDeactivates()
    {
        var cola = FakeCatalogService.Product("Cola", drinks.Id, 199, "CAD", "067000004568") with { Version = 3 };
        catalog.Products.Add(cola);
        await OpenAsync(StaffRole.Owner);
        var page = Render<ProductEdit>(p => p.Add(x => x.ProductId, cola.Id));

        Assert.Equal("1.99", Field(page, "price").GetAttribute("value"));
        Type(page, "price", "2.19");
        page.Find("[data-testid=save]").Click();
        page.Find("[data-testid=toggle-active]").Click();

        Assert.Equal(["update-product Cola v3", "deactivate-product Cola"], catalog.Calls);
        Assert.Equal(219, catalog.Saved[0].UnitPrice);
    }

    [Fact]
    public async Task Export_OffersTheCsvAsADownload()
    {
        catalog.ExportCsv = "sku,name\nCOLA,\"Cola, 355 ml\"\n";
        await OpenAsync(StaffRole.Manager);
        var page = Render<Products>();

        page.Find("[data-testid=export]").Click();

        var href = page.Find("[data-testid=download]").GetAttribute("href")!;
        Assert.StartsWith("data:text/csv;charset=utf-8,", href, StringComparison.Ordinal);
        Assert.Equal(catalog.ExportCsv, Uri.UnescapeDataString(href["data:text/csv;charset=utf-8,".Length..]));
    }

    [Fact]
    public async Task TheProductPage_ShowsItsHistory()
    {
        var cola = FakeCatalogService.Product("Cola", drinks.Id, 219);
        catalog.Products.Add(cola);
        catalog.Changes.Add(new ChangeItem(DateTimeOffset.UtcNow, "Mina", "updated", [("unitPrice", "199", "219")]));
        catalog.Changes.Add(new ChangeItem(DateTimeOffset.UtcNow.AddDays(-1), "Asha", "created", [("name", null, "Cola")]));
        await OpenAsync(StaffRole.Owner);

        var page = Render<ProductEdit>(p => p.Add(x => x.ProductId, cola.Id));

        Assert.Equal(2, page.FindAll("[data-testid=change]").Count);
        Assert.Contains("Mina", page.FindAll("[data-testid=change]")[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(["unitPrice: 199 → 219", "name: – → Cola"], page.FindAll("[data-testid=change-field]").Select(e => e.TextContent.Trim()));
    }

    [Fact]
    public void Money_FollowsTheCurrencysDecimals()
    {
        Assert.Equal("1.99", MoneyText.Format(199, "CAD"));
        Assert.Equal("149", MoneyText.Format(149, "JPY"));
        Assert.Equal("1.250", MoneyText.Format(1250, "KWD"));
        Assert.Equal((149L, (string?)null), MoneyText.Parse("149", "JPY"));
        Assert.NotNull(MoneyText.Parse("1.5", "JPY").Error);
        Assert.Equal((1250L, (string?)null), MoneyText.Parse("1.25", "KWD"));
        Assert.NotNull(MoneyText.Parse("1,99", "CAD").Error);
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type<T>(IRenderedComponent<T> page, string testId, string value)
        where T : IComponent => Field(page, testId).Change(value);

    /// <summary>The input or textarea of a MudBlazor field (the test id sits on it or on its wrapper).</summary>
    private static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> page, string testId)
        where T : IComponent
    {
        var element = page.Find($"[data-testid={testId}]");
        return element.TagName is "INPUT" or "TEXTAREA" ? element : element.QuerySelector("input, textarea")!;
    }
}
