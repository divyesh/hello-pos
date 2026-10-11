using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Businesses;
using HelloPos.Web.Pages.Staff;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Staff;

/// <summary>Feature 006 US3 web: invitation revoke/resend for owners; managers read settings only.</summary>
public sealed class InvitationActionsTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000dd");

    private readonly FakeStaffService staff = new();
    private readonly FakeBusinessService businesses = new();
    private readonly InvitationItem pending = new(Guid.NewGuid(), "mina@example.com", StaffRole.Manager, "Mina", "pending", DateTimeOffset.UtcNow.AddDays(3));
    private readonly InvitationItem expired = new(Guid.NewGuid(), "late@example.com", StaffRole.Owner, "Late", "expired", DateTimeOffset.UtcNow.AddDays(-1));
    private readonly InvitationItem accepted = new(Guid.NewGuid(), "done@example.com", StaffRole.Manager, "Done", "accepted", DateTimeOffset.UtcNow);

    public InvitationActionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IStaffService>(staff);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<OpenBusinessState>();
        staff.Invitations.AddRange([pending, expired, accepted]);
        businesses.Settings[Shop] = new BusinessSettings("Patel Grocers", "IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1m);
    }

    [Fact]
    public async Task Pending_CanBeRevokedOrResent_ExpiredResent_AcceptedNeither()
    {
        var page = await RenderAsync<StaffPage>(StaffRole.Owner);

        var rows = page.FindAll("[data-testid=invitation]");
        Assert.NotNull(rows[0].QuerySelector("[data-testid=revoke]"));
        Assert.NotNull(rows[0].QuerySelector("[data-testid=resend]"));
        Assert.Null(rows[1].QuerySelector("[data-testid=revoke]"));
        Assert.NotNull(rows[1].QuerySelector("[data-testid=resend]"));
        Assert.Empty(rows[2].QuerySelectorAll("button"));

        rows[0].QuerySelector("[data-testid=revoke]")!.Click();

        Assert.Equal((pending.Id, "revoke"), Assert.Single(staff.Changes));
        Assert.Contains("was revoked", page.Find("[data-testid=change-message]").TextContent, StringComparison.Ordinal);
        Assert.Contains("revoked", page.FindAll("[data-testid=invitation]")[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resend_SaysANewLinkWasSent()
    {
        var page = await RenderAsync<StaffPage>(StaffRole.Owner);

        page.FindAll("[data-testid=invitation]")[1].QuerySelector("[data-testid=resend]")!.Click();

        Assert.Equal((expired.Id, "resend"), Assert.Single(staff.Changes));
        Assert.Contains("new link was sent to late@example.com", page.Find("[data-testid=change-message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_SeesSettingsReadOnly()
    {
        var page = await RenderAsync<BusinessSettingsPage>(StaffRole.Manager);

        Assert.Contains("Patel Grocers", page.Find("[data-testid=read-only-settings]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=save]"));
    }

    [Fact]
    public async Task Owner_StillEditsSettings()
    {
        var page = await RenderAsync<BusinessSettingsPage>(StaffRole.Owner);

        Assert.NotNull(page.Find("[data-testid=save]"));
        Assert.Empty(page.FindAll("[data-testid=read-only-settings]"));
    }

    private async Task<IRenderedComponent<TPage>> RenderAsync<TPage>(StaffRole role)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Patel Grocers", role);
        return Render<TPage>();
    }
}
