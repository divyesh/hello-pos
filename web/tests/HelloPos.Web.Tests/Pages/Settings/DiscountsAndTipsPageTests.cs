using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Settings;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Settings;

/// <summary>Feature #12 US2 and US4 web: the discount approval limit and tips (owners edit, managers read).</summary>
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

    [Fact]
    public async Task Owner_TurnsTipsOn_WithPresets()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        Input(page.Find("[data-testid=tips-enabled]")).Change(true);
        Type(page.Find("[data-testid=tip-1]"), "15");
        Type(page.Find("[data-testid=tip-2]"), "10");
        Type(page.Find("[data-testid=tip-3]"), "20");
        page.Find("[data-testid=save-tips]").Click();

        Assert.Equal("selling 0% - tips 15/10/20 v1", Assert.Single(settings.Calls));
        Assert.Contains("Tips were saved", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_TurnsTipsOff_KeepingThePresets()
    {
        settings.Selling = settings.Selling with { TipsEnabled = true, TipPresets = ["10", "15"] };
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();
        Assert.Equal("15", Input(page.Find("[data-testid=tip-2]")).GetAttribute("value"));

        Input(page.Find("[data-testid=tips-enabled]")).Change(false);
        page.Find("[data-testid=save-tips]").Click();

        Assert.Equal("selling 0% - tips off v1", Assert.Single(settings.Calls));
        Assert.Empty(page.FindAll("[data-testid=tip-1]"));
    }

    [Theory]
    [InlineData("10", "10.00", "")]
    [InlineData("0", "", "")]
    [InlineData("12.555", "", "")]
    [InlineData("ten", "", "")]
    [InlineData("", "", "")]
    public async Task BadTips_AreRefusedBeforeSending(string first, string second, string third)
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DiscountsAndTips>();

        Input(page.Find("[data-testid=tips-enabled]")).Change(true);
        Type(page.Find("[data-testid=tip-1]"), first);
        Type(page.Find("[data-testid=tip-2]"), second);
        Type(page.Find("[data-testid=tip-3]"), third);
        page.Find("[data-testid=save-tips]").Click();

        Assert.Empty(settings.Calls);
        Assert.NotEmpty(page.Find("[data-testid=tips-error]").TextContent);
    }

    [Theory]
    [InlineData(true, "Tips offered: 10%, 15%")]
    [InlineData(false, "Tips are off.")]
    public async Task Manager_SeesTips_ReadOnly(bool enabled, string expected)
    {
        settings.Selling = settings.Selling with { TipsEnabled = enabled, TipPresets = ["10", "15"] };
        await OpenAsync(StaffRole.Manager);

        var page = Render<DiscountsAndTips>();

        Assert.Equal(expected, page.Find("[data-testid=tips-text]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=save-tips]"));
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) => Input(element).Change(value);

    private static AngleSharp.Dom.IElement Input(AngleSharp.Dom.IElement element) =>
        element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!;
}
