using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Staff;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Staff;

/// <summary>Feature 006 US2 web: edit, PIN, deactivate/reactivate, refusals shown, role-aware actions.</summary>
public sealed class StaffChangesTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000cc");

    private readonly FakeStaffService staff = new();
    private readonly StaffListItem owner = new(Guid.NewGuid(), "Asha", StaffRole.Owner, true, null, false, true, 1);
    private readonly StaffListItem ravi = new(Guid.NewGuid(), "Ravi", StaffRole.Cashier, true, "ravi", true, false, 4);
    private readonly StaffListItem mina = new(Guid.NewGuid(), "Mina", StaffRole.Manager, true, null, false, true, 2);

    public StaffChangesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IStaffService>(staff);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<OpenBusinessState>();
        staff.People.AddRange([owner, ravi, mina]);
    }

    [Fact]
    public async Task NoErrorIsShown_BeforeAnythingIsSent()
    {
        var page = await RenderAsAsync(StaffRole.Owner);

        Assert.Empty(page.FindAll("[data-testid=add-error]"));
        Assert.DoesNotContain("addError", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_PromotesACashier_SendingTheVersionRead()
    {
        var page = await RenderAsAsync(StaffRole.Owner);
        Row(page, "Ravi").QuerySelector("[data-testid=edit]")!.Click();

        Assert.Equal(["Cashier", "Manager"], page.FindAll("[data-testid=edit-role] option").Select(o => o.TextContent).Order());
        page.Find("[data-testid=edit-role]").Change("Manager");
        page.Find("[data-testid=save-person]").Click();

        Assert.Equal((ravi.Id, "update Ravi Manager v4"), Assert.Single(staff.Changes));
        Assert.Contains("Ravi was saved", page.Find("[data-testid=change-message]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=staff-editor]"));
    }

    [Fact]
    public async Task Owner_CanMakeABackOfficeManagerAnOwner()
    {
        var page = await RenderAsAsync(StaffRole.Owner);
        Row(page, "Mina").QuerySelector("[data-testid=edit]")!.Click();

        Assert.Contains("Owner", page.FindAll("[data-testid=edit-role] option").Select(o => o.TextContent));
    }

    [Fact]
    public async Task SettingAPin_ChecksTheKernelRules_ThenSends()
    {
        var page = await RenderAsAsync(StaffRole.Owner);
        Row(page, "Ravi").QuerySelector("[data-testid=pin]")!.Click();
        InputOf(page, "edit-pin").Change("1234");
        page.Find("[data-testid=save-person]").Click();
        Assert.Empty(staff.Changes);
        Assert.Contains("run of digits", page.Markup, StringComparison.Ordinal);

        InputOf(page, "edit-pin").Change("9071");
        page.Find("[data-testid=save-person]").Click();

        Assert.Equal((ravi.Id, "pin ravi 9071 v4"), Assert.Single(staff.Changes));
    }

    [Fact]
    public async Task DeactivateThenReactivate()
    {
        var page = await RenderAsAsync(StaffRole.Owner);

        Row(page, "Ravi").QuerySelector("[data-testid=deactivate]")!.Click();
        Assert.Contains("Ravi was deactivated", page.Find("[data-testid=change-message]").TextContent, StringComparison.Ordinal);

        page.Find("[data-testid=filter-inactive]").Click();
        Row(page, "Ravi").QuerySelector("[data-testid=reactivate]")!.Click();

        Assert.Equal(["deactivate v4", "reactivate v5"], staff.Changes.Select(c => c.What));
    }

    [Fact]
    public async Task Refusals_AreShown_AndTheListReloads()
    {
        var page = await RenderAsAsync(StaffRole.Owner);
        staff.NextChange = new OwnerActionResult.Failed("A business needs at least one active owner.");
        var listsBefore = staff.ListedStatuses.Count;

        Row(page, "Asha").QuerySelector("[data-testid=deactivate]")!.Click();

        Assert.Contains("at least one active owner", page.Find("[data-testid=change-message]").TextContent, StringComparison.Ordinal);
        Assert.True(staff.ListedStatuses.Count > listsBefore);
    }

    [Fact]
    public async Task Manager_SeesActionsOnCashiersOnly_AndCannotChangeRoles()
    {
        var page = await RenderAsAsync(StaffRole.Manager);

        Assert.Empty(Row(page, "Asha").QuerySelectorAll("button"));
        Assert.Empty(Row(page, "Mina").QuerySelectorAll("button"));
        Row(page, "Ravi").QuerySelector("[data-testid=edit]")!.Click();
        Assert.Empty(page.FindAll("[data-testid=edit-role]"));
        Assert.NotNull(Row(page, "Ravi").QuerySelector("[data-testid=pin]"));
        Assert.NotNull(Row(page, "Ravi").QuerySelector("[data-testid=deactivate]"));
    }

    private async Task<IRenderedComponent<StaffPage>> RenderAsAsync(StaffRole role)
    {
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Patel Grocers", role);
        return Render<StaffPage>();
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<StaffPage> page, string name) =>
        page.FindAll("[data-testid=staff-member]").Single(e => e.QuerySelector("b")?.TextContent == name);

    private static AngleSharp.Dom.IElement InputOf(IRenderedComponent<StaffPage> page, string testId)
    {
        var element = page.Find($"[data-testid={testId}]");
        return element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!;
    }
}
