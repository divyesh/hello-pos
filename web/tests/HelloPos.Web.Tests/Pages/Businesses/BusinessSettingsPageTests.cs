using AngleSharp.Dom;
using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Web.Pages.Businesses;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Businesses;

/// <summary>US3 web: edit the open business; the stale-tab guard (FR-014, research R7).</summary>
public sealed class BusinessSettingsPageTests : BunitContext
{
    private static readonly Guid ShopId = Guid.Parse("0199d1c4-8000-7000-8000-0000000000aa");

    private readonly FakeBusinessService api = new();
    private readonly FakeOwnerAccountService accounts = new();

    public BusinessSettingsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IBusinessService>(api);
        Services.AddSingleton<IOwnerAccountService>(accounts);
        Services.AddSingleton<OpenBusinessState>();
        api.Settings[ShopId] = new BusinessSettings("Patel Grocers", "IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1m);
    }

    [Fact]
    public void NoOpenBusiness_PointsToTheList()
    {
        var page = Render<BusinessSettingsPage>();

        Assert.NotNull(page.Find("[data-testid=no-open-business]"));
    }

    [Fact]
    public void ShowsTheOpenBusinessSettings()
    {
        OpenShop();

        var page = Render<BusinessSettingsPage>();

        Assert.Equal("Patel Grocers", InputOf(page.Find("[data-testid=business-name]")).GetAttribute("value"));
        Assert.Equal("IN", page.Find("[data-testid=country]").GetAttribute("value"));
        Assert.Equal("1.00", page.Find("[data-testid=rounding]").GetAttribute("value"));
    }

    [Fact]
    public void Save_SendsThePageBusiness_AndRenamesItInTheLayout()
    {
        OpenShop();
        var page = Render<BusinessSettingsPage>();
        InputOf(page.Find("[data-testid=business-name]")).Change("Patel Fresh");

        page.Find("[data-testid=save]").Click();

        var (pageBusiness, saved) = Assert.Single(api.Updated);
        Assert.Equal(ShopId, pageBusiness);
        Assert.Equal("Patel Fresh", saved.Name);
        Assert.NotNull(page.Find("[data-testid=saved]"));
        Assert.Equal("Patel Fresh", Services.GetRequiredService<OpenBusinessState>().Name);
    }

    [Fact]
    public void StaleTab_OnSave_SaysSo_AndOffersReload()
    {
        OpenShop();
        api.NextUpdate = new OwnerActionResult.BusinessChanged();
        var page = Render<BusinessSettingsPage>();

        page.Find("[data-testid=save]").Click();

        Assert.Contains("out of date", page.Find("[data-testid=stale]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-testid=reload]"));
        Assert.Empty(page.FindAll("[data-testid=saved]"));
    }

    [Fact]
    public void StaleTab_OnLoad_ShowsNoForm()
    {
        OpenShop();
        api.NextGetCurrent = new CurrentBusinessResult.BusinessChanged();

        var page = Render<BusinessSettingsPage>();

        Assert.NotNull(page.Find("[data-testid=stale]"));
        Assert.Empty(page.FindAll("[data-testid=save]"));
    }

    private void OpenShop() =>
        Services.GetRequiredService<OpenBusinessState>().OpenAsync(ShopId, "Patel Grocers").GetAwaiter().GetResult();

    private static IElement InputOf(IElement element) =>
        element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!;
}
