using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Settings;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Settings;

/// <summary>Feature #12 US3 web: the tables switch, areas and tables (owners and managers).</summary>
public sealed class TablesPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000ef");

    private readonly FakeSettingsService settings = new();

    public TablesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ISettingsService>(settings);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task TablesOff_HidesTheSetup_AndAManagerTurnsThemOn()
    {
        settings.AddArea("Main hall", "1");
        await OpenAsync(StaffRole.Manager);
        var page = Render<Tables>();

        Assert.NotNull(page.Find("[data-testid=tables-off]"));
        Assert.Empty(page.FindAll("[data-testid=area]"));

        await page.InvokeAsync(() => page.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));

        Assert.Equal("selling 0% - v1", Assert.Single(settings.Calls));
        Assert.True(settings.Selling.UsesTables);
        Assert.Equal(["Main hall"], page.FindAll("[data-testid=area-name]").Select(e => e.TextContent));
        Assert.Equal(["1"], page.FindAll("[data-testid=table-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task Lists_AreasWithTheirTables_InOrder()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        var hall = settings.AddArea("Main hall", "1", "2");
        settings.AddArea("Patio");
        settings.AddTable(hall.Id, "3", seats: 1, active: false);
        await OpenAsync(StaffRole.Owner);

        var page = Render<Tables>();

        Assert.Equal(["Main hall", "Patio"], page.FindAll("[data-testid=area-name]").Select(e => e.TextContent));
        Assert.Equal(["1", "2", "3"], page.FindAll("[data-testid=table-name]").Select(e => e.TextContent));
        var rows = page.FindAll("[data-testid=table]");
        Assert.Contains("4 seats", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("1 seat", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("Inactive", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("No tables yet.", page.FindAll("[data-testid=area]")[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddsAnArea_AndATable()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();
        Assert.Empty(page.FindAll("[data-testid=new-tables]"));

        Type(page.Find("[data-testid=new-area-name]"), "Patio");
        page.Find("[data-testid=create-area]").Click();
        Type(page.Find("[data-testid=new-table-name]"), "P1");
        page.Find("[data-testid=create-table]").Click();

        Assert.Equal(["create area Patio", "create table P1 seats=4 in Patio"], settings.Calls);
        Assert.Equal(["P1"], page.FindAll("[data-testid=table-name]").Select(e => e.TextContent));
    }

    [Fact]
    public async Task AddsManyTables_WithAPreviewOfTheNames()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        settings.AddArea("Main hall");
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();

        await page.InvokeAsync(() => page.FindComponent<MudRadioGroup<bool>>().Instance.ValueChanged.InvokeAsync(true));
        TypeLive(page.Find("[data-testid=bulk-prefix]"), "T");
        TypeLive(page.Find("[data-testid=bulk-to]"), "20");

        Assert.Equal("Creates 20 tables: T1, T2, T3, …, T19, T20.", page.Find("[data-testid=bulk-preview]").TextContent);
        page.Find("[data-testid=create-tables]").Click();

        Assert.Equal("create tables T[1-20] seats=4 in Main hall", Assert.Single(settings.Calls));
        Assert.Equal(20, page.FindAll("[data-testid=table]").Count);
    }

    [Fact]
    public async Task ABadRange_IsExplained_AndCannotBeSent()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        settings.AddArea("Main hall");
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();

        await page.InvokeAsync(() => page.FindComponent<MudRadioGroup<bool>>().Instance.ValueChanged.InvokeAsync(true));
        TypeLive(page.Find("[data-testid=bulk-to]"), "300");

        Assert.Equal("Add at most 200 tables at once.", page.Find("[data-testid=bulk-preview]").TextContent);
        Assert.True(page.Find("[data-testid=create-tables]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ServerClashes_AreShown()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        settings.AddArea("Main hall");
        settings.NextChange = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["names"] = "These table names are already used: 4, 17." });
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();

        await page.InvokeAsync(() => page.FindComponent<MudRadioGroup<bool>>().Instance.ValueChanged.InvokeAsync(true));
        page.Find("[data-testid=create-tables]").Click();

        Assert.Contains("These table names are already used: 4, 17.", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamesAndMovesAnArea_AndOnlyAnAreaWithoutActiveTablesCanBeDeactivated()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        settings.AddArea("Main hall", "1");
        settings.AddArea("Patio");
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();

        var toggles = page.FindAll("[data-testid=toggle-area]");
        Assert.True(toggles[0].HasAttribute("disabled"));
        Assert.False(toggles[1].HasAttribute("disabled"));

        page.FindAll("[data-testid=area-up]")[1].Click();
        page.FindAll("[data-testid=rename-area]")[0].Click();
        Type(page.Find("[data-testid=edit-area-name]"), "Garden");
        page.Find("[data-testid=save-area]").Click();
        page.FindAll("[data-testid=toggle-area]")[0].Click();

        Assert.Equal(["update area Patio -> Patio at 0 v1", "update area Patio -> Garden at 0 v2", "deactivate area Garden"], settings.Calls);
        Assert.Equal(["Garden", "Main hall"], page.FindAll("[data-testid=area-name]").Select(e => e.TextContent));
        Assert.Contains("Inactive", page.FindAll("[data-testid=area]")[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditsATable_MovesItToAnotherArea_ReordersAndDeactivates()
    {
        settings.Selling = settings.Selling with { UsesTables = true };
        var hall = settings.AddArea("Main hall", "1", "2");
        var patio = settings.AddArea("Patio", "P1");
        await OpenAsync(StaffRole.Owner);
        var page = Render<Tables>();

        page.FindAll("[data-testid=table-up]")[1].Click();
        Assert.Equal(["2", "1", "P1"], page.FindAll("[data-testid=table-name]").Select(e => e.TextContent));

        page.FindAll("[data-testid=edit-table]")[0].Click();
        Type(page.Find("[data-testid=edit-table-name]"), "P2");
        Type(page.Find("[data-testid=edit-table-seats]"), "6");
        // The table's own area picker comes before the one in the "Add tables" form.
        await page.InvokeAsync(() => page.FindComponents<MudSelect<Guid>>()[0].Instance.ValueChanged.InvokeAsync(patio.Id));
        page.Find("[data-testid=save-table]").Click();
        page.FindAll("[data-testid=toggle-table]")[0].Click();

        Assert.Equal(
            ["update table 2 -> 2 seats=4 in Main hall at 0 v1", $"update table 2 -> P2 seats=6 in Patio at {int.MaxValue} v2", "deactivate table 1"],
            settings.Calls);
        Assert.Equal(["1", "P1", "P2"], page.FindAll("[data-testid=table-name]").Select(e => e.TextContent));
        Assert.Contains("6 seats", page.FindAll("[data-testid=table]")[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("Inactive", page.FindAll("[data-testid=table]")[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(hall.Id, settings.Tables.Single(t => t.Name == "1").AreaId);
    }

    [Fact]
    public async Task Cashier_IsNotAllowed()
    {
        await OpenAsync(StaffRole.Cashier);

        var page = Render<Tables>();

        Assert.NotNull(page.Find("[data-testid=not-allowed]"));
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", role);

    private static void Type(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);

    /// <summary>Types into a field that updates on every keystroke.</summary>
    private static void TypeLive(AngleSharp.Dom.IElement element, string value) =>
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Input(value);
}
