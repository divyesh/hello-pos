using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Settings;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Settings;

/// <summary>Feature #12 US2 web: the discount approval limit (owners edit, managers read).</summary>
public sealed class DiscountsAndTipsPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000f0");

    private readonly FakeSettingsService settings = new();

    public DiscountsAndTipsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ISettingsService>(settings);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task Owner_SetsAPercentageAndAnAmount_InCurrencyUnits()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        Type(page.Find("[data-testid=limit-percent]"), "12.5");
        Type(page.Find("[data-testid=limit-amount]"), "20.00");
        page.Find("[data-testid=save-limit]").Click();

        Assert.Equal("selling 12.5% 2000 v1", Assert.Single(settings.Calls));
        Assert.Contains("was saved", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_ClearsTheAmount_ToKeepOnlyThePercentage()
    {
        settings.Selling = settings.Selling with { DiscountLimitPercent = "10", DiscountLimitAmount = 5000 };
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        Type(page.Find("[data-testid=limit-amount]"), string.Empty);
        page.Find("[data-testid=save-limit]").Click();

        Assert.Equal("selling 10% - v1", Assert.Single(settings.Calls));
    }

    [Theory]
    [InlineData("101")]
    [InlineData("10.555")]
    [InlineData("ten")]
    public async Task ABadPercentage_IsRefusedBeforeSending(string percent)
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        Type(page.Find("[data-testid=limit-percent]"), percent);
        page.Find("[data-testid=save-limit]").Click();

        Assert.Empty(settings.Calls);
        Assert.Contains("mud-input-error", page.Find("[data-testid=discount-limit]").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_SeesTheLimit_ReadOnly()
    {
        settings.Selling = settings.Selling with { DiscountLimitPercent = "10", DiscountLimitAmount = 2000 };
        await OpenAsync(StaffRole.Manager);

        var page = Render<DiscountsAndTips>();

        Assert.Equal("Up to 10% of the sale and at most 20.00 INR", page.Find("[data-testid=limit-text]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=save-limit]"));
    }

    [Fact]
    public async Task AStaleVersion_ShowsTheConflict()
    {
        settings.NextChange = new OwnerActionResult.Failed("Someone else changed this in the meantime. Reload and try again.");
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        page.Find("[data-testid=save-limit]").Click();

        Assert.Contains("Someone else changed this", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);
}
