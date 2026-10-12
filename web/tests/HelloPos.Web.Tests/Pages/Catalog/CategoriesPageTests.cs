using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Catalog;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Catalog;

/// <summary>Feature #11 US1 web: categories with a default tax profile.</summary>
public sealed class CategoriesPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000ee");

    private readonly FakeCatalogService catalog = new();

    public CategoriesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ICatalogService>(catalog);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task Lists_InSortOrder_WithTheirTaxProfile()
    {
        var gst = AddProfile("GST 5%");
        var exempt = AddProfile("Exempt");
        catalog.Categories.Add(new CategoryItem(Guid.NewGuid(), "Snacks", 2, gst.Id, true, 1));
        catalog.Categories.Add(new CategoryItem(Guid.NewGuid(), "Produce", 1, exempt.Id, true, 1));
        await OpenAsync(StaffRole.Manager);

        var page = Render<Categories>();

        Assert.Equal(["Produce", "Snacks"], page.FindAll("[data-testid=category-name]").Select(e => e.TextContent));
        Assert.Contains("Exempt", page.FindAll("[data-testid=category]")[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutATaxProfile_ItPointsToTaxProfiles()
    {
        await OpenAsync(StaffRole.Manager);

        var page = Render<Categories>();

        Assert.NotNull(page.Find("[data-testid=needs-profile]"));
        Assert.True(page.Find("[data-testid=create-category]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Manager_AddsACategory_WithTheFirstActiveProfileByDefault()
    {
        AddProfile("Old", active: false);
        AddProfile("GST 5%");
        await OpenAsync(StaffRole.Manager);
        var page = Render<Categories>();

        Type(page.Find("[data-testid=new-name]"), "Beverages");
        page.Find("[data-testid=create-category]").Click();

        Assert.Equal("create-category Beverages 0 GST 5%", Assert.Single(catalog.Calls));
        Assert.Equal(["Beverages"], page.FindAll("[data-testid=category-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task Manager_EditsAndDeactivates()
    {
        var gst = AddProfile("GST 5%");
        catalog.Categories.Add(new CategoryItem(Guid.NewGuid(), "Snacks", 0, gst.Id, true, 4));
        await OpenAsync(StaffRole.Manager);
        var page = Render<Categories>();

        page.Find("[data-testid=edit]").Click();
        Type(page.Find("[data-testid=edit-name]"), "Chips");
        page.Find("[data-testid=save-edit]").Click();
        page.Find("[data-testid=toggle-active]").Click();

        Assert.Equal(["update-category Snacks -> Chips 0 v4", "deactivate-category Chips"], catalog.Calls);
        Assert.Contains("Inactive", page.Find("[data-testid=category]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerFieldErrors_AreShownOnTheField()
    {
        AddProfile("GST 5%");
        catalog.NextChange = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["name"] = "Another category has this name." });
        await OpenAsync(StaffRole.Owner);
        var page = Render<Categories>();

        Type(page.Find("[data-testid=new-name]"), "Snacks");
        page.Find("[data-testid=create-category]").Click();

        Assert.Contains("Another category has this name.", page.Markup, StringComparison.Ordinal);
    }

    private TaxProfileItem AddProfile(string name, bool active = true)
    {
        var profile = FakeCatalogService.Profile(name, "taxable", active, new TaxComponentItem("GST", "5"));
        catalog.Profiles.Add(profile);
        return profile;
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);
}
