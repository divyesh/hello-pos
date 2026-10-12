using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Catalog;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Catalog;

/// <summary>Feature #11 US1 web: tax profiles (owners change, managers read) and the deposits setting.</summary>
public sealed class TaxProfilesPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000dd");

    private readonly FakeCatalogService catalog = new();
    private readonly FakeBusinessService businesses = new();

    public TaxProfilesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<ICatalogService>(catalog);
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
        businesses.Settings[Shop] = new BusinessSettings("Shop", "CA", "CAD", PricingMode.TaxExclusive, 1, "America/Toronto", 0.05m);
        catalog.Profiles.Add(FakeCatalogService.Profile("GST 5%", "taxable", true, new TaxComponentItem("CGST", "2.5"), new TaxComponentItem("SGST", "2.5")));
        catalog.Profiles.Add(FakeCatalogService.Profile("Exempt", "exempt"));
    }

    [Fact]
    public async Task Owner_SeesProfilesWithTheirRates()
    {
        await OpenAsync(StaffRole.Owner);

        var page = Render<TaxProfiles>();

        Assert.Equal(["GST 5%", "Exempt"], page.FindAll("[data-testid=profile-name]").Select(e => e.TextContent));
        Assert.Equal(["CGST 2.5% + SGST 2.5%", "Exempt"], page.FindAll("[data-testid=rates]").Select(e => e.TextContent.Trim()));
        Assert.NotNull(page.Find("[data-testid=new-profile]"));
    }

    [Fact]
    public async Task Managers_ReadOnly()
    {
        await OpenAsync(StaffRole.Manager);

        var page = Render<TaxProfiles>();

        Assert.NotNull(page.Find("[data-testid=owners-only]"));
        Assert.Empty(page.FindAll("[data-testid=new-profile]"));
        Assert.Empty(page.FindAll("[data-testid=toggle-active]"));
        Assert.Empty(page.FindAll("[data-testid=deposits]"));
    }

    [Fact]
    public async Task Owner_AddsATaxableProfile()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<TaxProfiles>();

        Type(page.Find("[data-testid=new-name]"), "HST 13%");
        Type(page.Find("[data-testid=component-label]"), "HST");
        Type(page.Find("[data-testid=component-rate]"), "13");
        page.Find("[data-testid=create-profile] ").Click();

        Assert.Equal("create-profile HST 13% taxable HST:13", Assert.Single(catalog.Calls));
        Assert.Contains("HST 13%", page.FindAll("[data-testid=profile-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task AnInvalidRate_KeepsAddDisabled()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<TaxProfiles>();

        Type(page.Find("[data-testid=new-name]"), "Odd");
        Type(page.Find("[data-testid=component-label]"), "X");
        Type(page.Find("[data-testid=component-rate]"), "120");

        Assert.True(page.Find("[data-testid=create-profile]").HasAttribute("disabled"));
        Assert.Contains("0 to 100", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_RemovesAFutureVersion_AndDeactivates()
    {
        var gst = catalog.Profiles[0];
        catalog.Profiles[0] = gst with { Versions = [.. gst.Versions, new TaxProfileVersionItem(Guid.NewGuid(), new DateOnly(2027, 4, 1), [new("CGST", "3"), new("SGST", "3")], false, true)] };
        await OpenAsync(StaffRole.Owner);
        var page = Render<TaxProfiles>();
        Assert.Single(page.FindAll("[data-testid=future-version]"));

        page.Find("[data-testid=remove-version]").Click();
        page.FindAll("[data-testid=toggle-active]")[0].Click();

        Assert.Equal(["delete-version GST 5%", "deactivate-profile GST 5%"], catalog.Calls);
        Assert.Empty(page.FindAll("[data-testid=future-version]"));
    }

    [Fact]
    public async Task Owner_TurnsDepositTaxOn()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<TaxProfiles>();

        page.Find("[data-testid=deposits] input").Change(true);

        Assert.Equal([true], businesses.DepositsTaxableSent);
        Assert.Contains("taxed with the product", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);
}
