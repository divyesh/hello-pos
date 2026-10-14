using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Settings;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Settings;

/// <summary>Feature #12 US1 web: payment types (owners change them, managers read them).</summary>
public sealed class PaymentTypesPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000ef");

    private readonly FakeSettingsService settings = new();

    public PaymentTypesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ISettingsService>(settings);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task Lists_InOrder_WithCashBuiltIn_AndCannotDeactivateCash()
    {
        settings.Add("UPI");
        await OpenAsync(StaffRole.Owner);

        var page = Render<PaymentTypes>();

        Assert.Equal(["Cash", "UPI"], page.FindAll("[data-testid=type-name]").Select(e => e.TextContent));
        var rows = page.FindAll("[data-testid=payment-type]");
        Assert.Contains("Built in", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("gives change", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Empty(rows[0].QuerySelectorAll("[data-testid=toggle-active]"));
        Assert.NotNull(rows[1].QuerySelector("[data-testid=toggle-active]"));
    }

    [Fact]
    public async Task Owner_AddsAType_AndChoosingAKindSetsTheReferenceDefault()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<PaymentTypes>();

        Type(page.Find("[data-testid=new-name]"), "Gift voucher");
        await page.InvokeAsync(() => page.FindComponents<MudBlazor.MudSelect<string>>().Single().Instance.ValueChanged.InvokeAsync("voucher"));
        page.Find("[data-testid=create-payment-type]").Click();

        Assert.Equal("create Gift voucher voucher drawer=False reference=False", Assert.Single(settings.Calls));
        Assert.Equal(["Cash", "Gift voucher"], page.FindAll("[data-testid=type-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task Owner_MovesATypeUp_EditsAndDeactivates()
    {
        settings.Add("UPI");
        await OpenAsync(StaffRole.Owner);
        var page = Render<PaymentTypes>();

        page.FindAll("[data-testid=move-up]")[1].Click();
        Assert.Equal(["UPI", "Cash"], page.FindAll("[data-testid=type-name]").Select(e => e.TextContent));

        page.FindAll("[data-testid=edit]")[0].Click();
        Type(page.Find("[data-testid=edit-name]"), "PhonePe");
        page.Find("[data-testid=save-edit]").Click();
        page.Find("[data-testid=toggle-active]").Click();

        Assert.Equal(
            ["reorder UPI, Cash", "update UPI -> PhonePe digitalWallet drawer=False reference=True v1", "deactivate PhonePe"],
            settings.Calls);
        Assert.Contains("Inactive", page.FindAll("[data-testid=payment-type]")[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_ReadsOnly()
    {
        settings.Add("UPI");
        await OpenAsync(StaffRole.Manager);

        var page = Render<PaymentTypes>();

        Assert.NotNull(page.Find("[data-testid=read-only]"));
        Assert.Equal(2, page.FindAll("[data-testid=payment-type]").Count);
        Assert.Empty(page.FindAll("[data-testid=edit]"));
        Assert.Empty(page.FindAll("[data-testid=move-up]"));
        Assert.Empty(page.FindAll("[data-testid=new-payment-type]"));
    }

    [Fact]
    public async Task Cashier_IsNotAllowed()
    {
        await OpenAsync(StaffRole.Cashier);

        var page = Render<PaymentTypes>();

        Assert.NotNull(page.Find("[data-testid=not-allowed]"));
    }

    [Fact]
    public async Task ServerFieldErrors_AreShown()
    {
        settings.NextChange = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["name"] = "Another payment type has this name." });
        await OpenAsync(StaffRole.Owner);
        var page = Render<PaymentTypes>();

        Type(page.Find("[data-testid=new-name]"), "cash");
        page.Find("[data-testid=create-payment-type]").Click();

        Assert.Contains("Another payment type has this name.", page.Markup, StringComparison.Ordinal);
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);
}
