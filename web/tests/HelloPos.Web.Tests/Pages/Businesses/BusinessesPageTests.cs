using AngleSharp.Dom;
using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using BusinessesPage = HelloPos.Web.Pages.Businesses.Businesses;

namespace HelloPos.Web.Tests.Pages.Businesses;

/// <summary>US2 web: list, empty state, create form with country pre-fill and kernel validation (FR-009–FR-011).</summary>
public sealed class BusinessesPageTests : BunitContext
{
    private readonly FakeBusinessService api = new();

    public BusinessesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IBusinessService>(api);
        Services.AddSingleton<IOwnerAccountService>(accounts);
        Services.AddSingleton<OpenBusinessState>();
    }

    private readonly FakeOwnerAccountService accounts = new();

    [Fact]
    public void Anonymous_IsAskedToSignIn()
    {
        AddAuthorization();

        var page = Render<BusinessesPage>();

        Assert.NotNull(page.Find("[data-testid=sign-in-required]"));
        Assert.Equal(0, api.ListCalls);
    }

    [Fact]
    public void NoBusinesses_ShowsTheEmptyStateAndTheForm()
    {
        AddAuthorization().SetAuthorized("Asha");

        var page = Render<BusinessesPage>();

        Assert.NotNull(page.Find("[data-testid=no-businesses]"));
        Assert.NotNull(page.Find("[data-testid=save]"));
    }

    [Fact]
    public void Businesses_AreListed_AndTheFormOpensOnRequest()
    {
        AddAuthorization().SetAuthorized("Asha");
        api.Businesses.Add(new BusinessListItem(Guid.NewGuid(), "Patel Grocers", "IN", "INR"));

        var page = Render<BusinessesPage>();

        Assert.Contains("Patel Grocers", page.Find("[data-testid=business]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=save]"));
        page.Find("[data-testid=new-business]").Click();
        Assert.NotNull(page.Find("[data-testid=save]"));
    }

    [Theory]
    [InlineData("IN", "INR", "TaxInclusive", "4", "Asia/Kolkata", "1.00")]
    [InlineData("US", "USD", "TaxExclusive", "1", "America/New_York", "0.00")]
    [InlineData("CA", "CAD", "TaxExclusive", "1", "America/Toronto", "0.05")]
    public void ChoosingACountry_PrefillsItsDefaults(string country, string currency, string pricing, string month, string zone, string rounding)
    {
        AddAuthorization().SetAuthorized("Asha");
        var page = Render<BusinessesPage>();

        page.Find("[data-testid=country]").Change(country);

        Assert.Equal(currency, InputOf(page.Find("[data-testid=currency]")).GetAttribute("value"));
        Assert.Equal(pricing, page.Find("[data-testid=pricing]").GetAttribute("value"));
        Assert.Equal(month, page.Find("[data-testid=fy-month]").GetAttribute("value"));
        Assert.Equal(zone, page.Find("[data-testid=time-zone]").GetAttribute("value"));
        Assert.Equal(rounding, page.Find("[data-testid=rounding]").GetAttribute("value"));
    }

    [Fact]
    public void Saving_SendsTheSettings_AndReloadsTheList()
    {
        AddAuthorization().SetAuthorized("Asha");
        var page = Render<BusinessesPage>();
        InputOf(page.Find("[data-testid=business-name]")).Change("Patel Grocers");
        page.Find("[data-testid=country]").Change("IN");
        page.Find("[data-testid=time-zone]").Change("Asia/Kolkata");

        api.Businesses.Add(new BusinessListItem(Guid.NewGuid(), "Patel Grocers", "IN", "INR"));
        page.Find("[data-testid=save]").Click();

        Assert.Equal(
            new BusinessSettings("Patel Grocers", "IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1.00m),
            Assert.Single(api.Created));
        page.WaitForAssertion(() => Assert.Contains("Patel Grocers", page.Find("[data-testid=business]").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, api.ListCalls);
    }

    [Fact]
    public void InvalidSettings_AreShownByField_AndNotSent()
    {
        AddAuthorization().SetAuthorized("Asha");
        var page = Render<BusinessesPage>();

        page.Find("[data-testid=save]").Click();

        Assert.Empty(api.Created);
        Assert.Contains("Enter a business name", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Choose India, the United States or Canada.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerFieldErrors_AreShownOnTheirFields()
    {
        AddAuthorization().SetAuthorized("Asha");
        api.NextCreate = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["currencyCode"] = "Unknown currency." });
        var page = Render<BusinessesPage>();
        InputOf(page.Find("[data-testid=business-name]")).Change("Patel Grocers");
        page.Find("[data-testid=country]").Change("IN");

        page.Find("[data-testid=save]").Click();

        Assert.Contains("Unknown currency.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=business]"));
    }

    private static IElement InputOf(IElement element) =>
        element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!;

    [Fact]
    public void Open_OpensTheBusiness_AndGoesToItsSettings()
    {
        AddAuthorization().SetAuthorized("Asha");
        var shop = new BusinessListItem(Guid.NewGuid(), "Patel Grocers", "IN", "INR");
        api.Businesses.Add(shop);
        var page = Render<BusinessesPage>();

        page.Find("[data-testid=open]").Click();

        Assert.Equal([shop.Id], api.Opened);
        Assert.Equal(shop.Id, Services.GetRequiredService<OpenBusinessState>().Id);
        Assert.EndsWith("business/settings", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOpenBusiness_IsMarked()
    {
        AddAuthorization().SetAuthorized("Asha");
        var open = new BusinessListItem(Guid.NewGuid(), "Open Shop", "IN", "INR");
        api.Businesses.Add(open);
        api.Businesses.Add(new BusinessListItem(Guid.NewGuid(), "Other Shop", "IN", "INR"));
        api.Settings[open.Id] = new BusinessSettings("Open Shop", "IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1m);
        accounts.Session = new OwnerSession(Guid.NewGuid(), "Asha", "asha@example.com", open.Id);
        await Services.GetRequiredService<OpenBusinessState>().LoadAsync();

        var page = Render<BusinessesPage>();

        Assert.Single(page.FindAll("[data-testid=is-open]"));
        Assert.Single(page.FindAll("[data-testid=open]"));
    }
}
