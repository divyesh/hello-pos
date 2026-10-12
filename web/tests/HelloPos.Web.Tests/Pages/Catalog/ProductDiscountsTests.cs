using Bunit;
using HelloPos.Web.Pages.Catalog;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Catalog;

/// <summary>Feature #11 US3 web: scheduling, ending and removing item discounts on a product.</summary>
public sealed class ProductDiscountsTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-000000000101");
    private static readonly Guid Product = Guid.Parse("0199d1c4-8000-7000-8000-000000000102");

    private readonly FakeCatalogService catalog = new();

    public ProductDiscountsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ICatalogService>(catalog);
    }

    [Fact]
    public void Lists_WithStatus_AndActionsPerStatus()
    {
        catalog.Discounts.Add(new DiscountItem(Guid.NewGuid(), "fixed", "50", new DateOnly(2026, 11, 10), null, "future"));
        catalog.Discounts.Add(new DiscountItem(Guid.NewGuid(), "percent", "20", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), "running"));
        catalog.Discounts.Add(new DiscountItem(Guid.NewGuid(), "percent", "10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), "ended"));

        var section = Render();

        var rows = section.FindAll("[data-testid=discount]");
        Assert.Contains("0.50 off each", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("onward", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("20% off", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Single(section.FindAll("[data-testid=remove-discount]"));
        Assert.Single(section.FindAll("[data-testid=end-today]"));
    }

    [Fact]
    public void Schedule_APercent_FromAndTo()
    {
        var section = Render();

        Field(section, "discount-value").Change("20");
        Field(section, "discount-from").Change("2026-11-05");
        Field(section, "discount-to").Change("2026-11-08");
        section.Find("[data-testid=schedule]").Click();

        Assert.Equal("create-discount percent 20 2026-11-05 2026-11-08", Assert.Single(catalog.Calls));
        Assert.Single(section.FindAll("[data-testid=discount]"));
    }

    [Fact]
    public async Task AFixedAmount_GoesInMinorUnits_FromToday_WithNoEnd()
    {
        var section = Render();

        var kind = section.FindComponent<MudBlazor.MudSelect<string>>();
        await section.InvokeAsync(() => kind.Instance.ValueChanged.InvokeAsync("fixed"));
        Field(section, "discount-value").Change("0.50");
        section.Find("[data-testid=schedule]").Click();

        Assert.Equal("create-discount fixed 50 2026-11-02", Assert.Single(catalog.Calls));
    }

    [Fact]
    public void EndToday_AndRemove()
    {
        var running = new DiscountItem(Guid.NewGuid(), "percent", "20", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), "running");
        catalog.Discounts.Add(running);
        catalog.Discounts.Add(new DiscountItem(Guid.NewGuid(), "fixed", "50", new DateOnly(2026, 11, 10), null, "future"));
        var section = Render();

        section.Find("[data-testid=end-today]").Click();
        section.Find("[data-testid=remove-discount]").Click();

        Assert.Equal(["update-discount percent 20 2026-11-01 2026-11-02", "delete-discount"], catalog.Calls);
    }

    [Fact]
    public void AnOverlap_FromTheServer_IsShownOnTheDate()
    {
        catalog.NextChange = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["fromDate"] = "This overlaps the discount from 2026-11-01 to 2026-11-07." });
        var section = Render();

        Field(section, "discount-value").Change("10");
        section.Find("[data-testid=schedule]").Click();

        Assert.Contains("This overlaps the discount", section.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<ProductDiscounts> Render() => Render<ProductDiscounts>(p => p
        .Add(x => x.PageBusiness, Shop)
        .Add(x => x.ProductId, Product)
        .Add(x => x.Currency, "CAD"));

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<ProductDiscounts> section, string testId)
    {
        var element = section.Find($"[data-testid={testId}]");
        return element.TagName is "INPUT" or "TEXTAREA" ? element : element.QuerySelector("input, textarea")!;
    }
}
