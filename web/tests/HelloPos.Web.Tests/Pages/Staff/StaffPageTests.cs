using AngleSharp.Dom;
using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Staff;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Staff;

/// <summary>Feature 006 US1 web: staff list, adding POS staff (kernel rules), invitations for owners.</summary>
public sealed class StaffPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000bb");

    private readonly FakeStaffService staff = new();
    private readonly FakeOwnerAccountService accounts = new();
    private readonly FakeBusinessService businesses = new();

    public StaffPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IStaffService>(staff);
        Services.AddSingleton<IOwnerAccountService>(accounts);
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<OpenBusinessState>();
        staff.People.Add(new StaffListItem(Guid.NewGuid(), "Asha", StaffRole.Owner, true, null, false, true, 1));
        staff.People.Add(new StaffListItem(Guid.NewGuid(), "Old Ravi", StaffRole.Cashier, false, "oldravi", true, false, 3));
    }

    [Fact]
    public void NoOpenBusiness_PointsToTheList()
    {
        var page = Render<StaffPage>();

        Assert.NotNull(page.Find("[data-testid=no-open-business]"));
    }

    [Fact]
    public async Task Owner_SeesActiveStaff_AndCanFilter()
    {
        await OpenAsync(StaffRole.Owner);

        var page = Render<StaffPage>();

        Assert.Equal(["Asha"], page.FindAll("[data-testid=staff-member] b").Select(e => e.TextContent));
        page.Find("[data-testid=filter-inactive]").Click();
        Assert.Equal(["Old Ravi"], page.FindAll("[data-testid=staff-member] b").Select(e => e.TextContent));
        Assert.Equal(["active", "inactive"], staff.ListedStatuses);
    }

    [Fact]
    public async Task AddingACashier_SendsTheForm_AndRefreshes()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<StaffPage>();
        Type(page, "staff-name", "Ravi");
        Type(page, "staff-username", "ravi");
        Type(page, "staff-pin", "4826");

        page.Find("[data-testid=add-staff]").Click();

        Assert.Equal(("Ravi", StaffRole.Cashier, "ravi", "4826"), Assert.Single(staff.Added));
        Assert.NotNull(page.Find("[data-testid=added]"));
        Assert.Contains("Ravi", page.FindAll("[data-testid=staff-member] b").Select(e => e.TextContent));
    }

    [Theory]
    [InlineData("1234", "run of digits")]
    [InlineData("12", "4 to 6 digits")]
    public async Task WeakPins_AreRefusedBeforeSending(string pin, string message)
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<StaffPage>();
        Type(page, "staff-name", "Ravi");
        Type(page, "staff-username", "ravi");
        Type(page, "staff-pin", pin);

        page.Find("[data-testid=add-staff]").Click();

        Assert.Empty(staff.Added);
        Assert.Contains(message, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerFieldErrors_AreShown()
    {
        await OpenAsync(StaffRole.Owner);
        staff.NextAdd = new OwnerActionResult.FieldErrors(new Dictionary<string, string> { ["posUsername"] = "Another person in this business already uses this username." });
        var page = Render<StaffPage>();
        Type(page, "staff-name", "Ravi");
        Type(page, "staff-username", "ravi");
        Type(page, "staff-pin", "4826");

        page.Find("[data-testid=add-staff]").Click();

        Assert.Contains("already uses this username", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_CanOfferManager_AndInvite()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<StaffPage>();

        Assert.Equal(["Cashier", "Manager"], page.FindAll("[data-testid=staff-role] option").Select(o => o.TextContent));
        Type(page, "invite-email", "mina@example.com");
        Type(page, "invite-name", "Mina");
        page.Find("[data-testid=invite]").Click();

        Assert.Equal(("mina@example.com", StaffRole.Manager, "Mina"), Assert.Single(staff.Invited));
        Assert.NotNull(page.Find("[data-testid=invited]"));
        Assert.Contains("mina@example.com", page.Find("[data-testid=invitation]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_OffersOnlyCashier_AndSeesNoInvitations()
    {
        await OpenAsync(StaffRole.Manager);

        var page = Render<StaffPage>();

        Assert.Equal(["Cashier"], page.FindAll("[data-testid=staff-role] option").Select(o => o.TextContent));
        Assert.Empty(page.FindAll("[data-testid=invitations]"));
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Patel Grocers", role);

    private static void Type(IRenderedComponent<StaffPage> page, string testId, string value)
    {
        var element = page.Find($"[data-testid={testId}]");
        (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? element : element.QuerySelector("input")!).Change(value);
    }
}
