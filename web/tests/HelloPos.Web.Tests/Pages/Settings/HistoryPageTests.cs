using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Settings;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Settings;

/// <summary>Feature #12 US5 web: the settings history (FR-016), read by owners and managers.</summary>
public sealed class HistoryPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000f0");
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsService settings = new();

    public HistoryPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ISettingsService>(settings);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task Changes_ShowWhatChanged_WhoAndEachValueBeforeAndAfter()
    {
        var upi = settings.Add("UPI / QR");
        settings.Selling = settings.Selling with { Currency = "INR" };
        settings.History.Add(Change("selling", Shop, "updated", "manager", ("usesTables", "false", "true"), ("discountLimitAmount", null, "2000")));
        settings.History.Add(Change("paymentType", upi.Id, "updated", "Owner", ("name", "UPI", "UPI / QR"), ("kind", "other", "digitalWallet")));
        settings.History.Add(Change("paymentType", upi.Id, "created", "Owner", ("name", null, "UPI")));
        await OpenAsync(StaffRole.Manager);

        var page = Render<History>();

        var entries = page.FindAll("[data-testid=change]");
        Assert.Equal(3, entries.Count);
        Assert.Equal(["Tables, discounts and tips changed", "Payment type UPI / QR changed", "Payment type UPI / QR added"],
            page.FindAll("[data-testid=change-title]").Select(t => t.TextContent));
        Assert.Contains("manager", entries[0].QuerySelector("[data-testid=change-who]")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(["Uses tables: no → yes", "Discount limit (amount): – → 20.00 INR"],
            entries[0].QuerySelectorAll("[data-testid=change-field]").Select(f => f.TextContent.Trim()));
        Assert.Equal(["Name: UPI → UPI / QR", "Kind: Other → UPI or digital wallet"],
            entries[1].QuerySelectorAll("[data-testid=change-field]").Select(f => f.TextContent.Trim()));
        Assert.Equal(["all 1"], settings.HistoryRequests);
    }

    [Fact]
    public async Task BulkAndReorderEntries_SayWhatHappened()
    {
        var hall = settings.AddArea("Hall");
        settings.History.Add(Change("table", hall.Id, "bulkCreated", "Owner", ("count", null, "20"), ("names", null, "1–20")));
        settings.History.Add(Change("paymentType", Shop, "reordered", "Owner", ("order", "Cash, UPI", "UPI, Cash")));
        await OpenAsync(StaffRole.Owner);

        var page = Render<History>();

        Assert.Equal(["Tables added in bulk to Hall", "Payment types reordered"], page.FindAll("[data-testid=change-title]").Select(t => t.TextContent));
        Assert.Contains("Order: Cash, UPI → UPI, Cash", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoChanges_SaySo()
    {
        await OpenAsync(StaffRole.Owner);

        var page = Render<History>();

        Assert.Equal("No changes recorded yet.", page.Find("[data-testid=empty]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=older]"));
        Assert.Empty(page.FindAll("[data-testid=newer]"));
    }

    [Fact]
    public async Task AFullPage_OffersOlderChanges_AndBackToNewer()
    {
        for (var i = 1; i <= History.PageSize + 3; i++)
        {
            settings.History.Add(Change("tableArea", Guid.NewGuid(), "created", "Owner", ("name", null, $"Area {i}")));
        }

        await OpenAsync(StaffRole.Owner);
        var page = Render<History>();
        Assert.Equal(History.PageSize, page.FindAll("[data-testid=change]").Count);

        page.Find("[data-testid=older]").Click();

        Assert.Equal(3, page.FindAll("[data-testid=change]").Count);
        Assert.Contains("Area 51", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=older]"));

        page.Find("[data-testid=newer]").Click();

        Assert.Equal(["all 1", "all 2", "all 1"], settings.HistoryRequests);
        Assert.Equal(History.PageSize, page.FindAll("[data-testid=change]").Count);
    }

    [Fact]
    public async Task OneItemsChanges_AreShown_WhenAskedForByItsId()
    {
        var upi = settings.Add("UPI");
        settings.History.Add(Change("paymentType", upi.Id, "deactivated", "Owner", ("active", "true", "false")));
        settings.History.Add(Change("paymentType", Guid.NewGuid(), "created", "Owner", ("name", null, "Cheque")));
        await OpenAsync(StaffRole.Owner);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"settings/history?entityId={upi.Id}");

        var page = Render<History>();

        Assert.Equal([$"{upi.Id} 1"], settings.HistoryRequests);
        Assert.Equal(["Payment type UPI deactivated"], page.FindAll("[data-testid=change-title]").Select(t => t.TextContent));
        Assert.NotNull(page.Find("[data-testid=filtered]"));
    }

    [Fact]
    public async Task Cashiers_DoNotSeeTheHistory()
    {
        await OpenAsync(StaffRole.Cashier);

        var page = Render<History>();

        Assert.NotNull(page.Find("[data-testid=not-allowed]"));
        Assert.Empty(settings.HistoryRequests);
    }

    [Fact]
    public async Task AFailedLoad_SaysWhy()
    {
        settings.NextHistory = new LoadResult<IReadOnlyList<SettingsChangeItem>>.Failed("The server could not be reached.");
        await OpenAsync(StaffRole.Owner);

        var page = Render<History>();

        Assert.Equal("The server could not be reached.", page.Find("[data-testid=load-error]").TextContent);
    }

    private static SettingsChangeItem Change(string entity, Guid entityId, string action, string actor, params (string Field, string? Old, string? New)[] fields) =>
        new(Noon, actor, entity, entityId, action, fields);

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);
}
